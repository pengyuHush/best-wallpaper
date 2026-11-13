using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using BestWallpaper.Models;

namespace BestWallpaper.Services;

/// <summary>
/// 主状态机：负责每日换池、定时轮播、外部改动检测、兜底与重试。
/// 所有状态变更都在 UI 线程上完成（Dispatcher），网络与下载是 async 的。
/// </summary>
public sealed class Engine
{
    public AppConfig Config { get; private set; } = new();
    public DailyPool? Pool { get; private set; }
    public WallpaperItem? Current { get; private set; }

    public event Action? Changed;
    public event Action<string, string>? Notify;

    /// <summary>壁纸被系统/安全软件拒绝时触发，参数是给人看的原因。</summary>
    public event Action<string>? Blocked;

    private readonly Dispatcher _dispatcher;
    private readonly Random _rng = new();
    private readonly PoolBuilder _builder = new();

    private DispatcherTimer? _rotationTimer;
    private DispatcherTimer? _dayTimer;
    private DispatcherTimer? _externalTimer;
    private DispatcherTimer? _retryTimer;

    private string _status = "启动中…";
    private bool _busy;
    private bool _setThisSession;
    private bool _appliedThisRefresh;
    private DateTime _nextSwitchAt = DateTime.MinValue;

    public Engine(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public bool Busy => _busy;
    public string Status => _status;
    public int PoolCount => Pool?.Items.Count ?? 0;
    public string ThemeLabel => Pool?.ThemeDisplay ?? Config.LastThemeDisplay ?? "—";
    public string Today => DateTime.Today.ToString("yyyy-MM-dd");

    public TimeSpan NextSwitchIn
    {
        get
        {
            if (Config.Paused || _nextSwitchAt == DateTime.MinValue) return TimeSpan.Zero;
            var left = _nextSwitchAt - DateTime.Now;
            return left < TimeSpan.Zero ? TimeSpan.Zero : left;
        }
    }

    public string CurrentPath =>
        Current is null || Pool is null ? "" : Path.Combine(Paths.PoolDir(Pool.Date), Current.File);

    public void Initialize()
    {
        Paths.EnsureCreated();
        Config = ConfigStore.Load();

        // 决策：暂停只在本次运行内有效，重启后自动恢复轮播
        if (Config.Paused)
        {
            Log.Info("上次运行处于暂停状态，按决策在本次启动时自动恢复轮播");
            Config.Paused = false;
            Config.PausedReason = null;
            ConfigStore.Save(Config);
        }

        ApplyAutostart();

        Pool = ConfigStore.LoadPool(Today) ?? (Config.LastPoolDate is null ? null : ConfigStore.LoadPool(Config.LastPoolDate));
        if (Pool is not null && Config.CurrentIndex >= 0 && Config.CurrentIndex < Pool.Items.Count)
            Current = Pool.Items[Config.CurrentIndex];

        StartTimers();
        SetStatus(Pool is null ? "尚未下载壁纸" : $"今日主题：{Pool.ThemeDisplay}（{Pool.Items.Count} 张）");
    }

    private void StartTimers()
    {
        _rotationTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(Config.RotationMinutes) };
        _rotationTimer.Tick += (_, _) => OnRotationTick();
        _rotationTimer.Start();

        _dayTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _dayTimer.Tick += (_, _) => OnDayTick();
        _dayTimer.Start();

        _externalTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _externalTimer.Tick += (_, _) => OnExternalTick();
        _externalTimer.Start();
    }

    /// <summary>启动流程：有今天的池子就直接轮播，没有就拉一次；自启动模式下先延迟。</summary>
    public async Task StartAsync(bool autostart)
    {
        if (autostart && Config.StartDelaySeconds > 0)
        {
            SetStatus($"自启动延迟 {Config.StartDelaySeconds} 秒后开始…");
            Changed?.Invoke();
            await Task.Delay(TimeSpan.FromSeconds(Config.StartDelaySeconds));
        }

        if (Pool is null || !string.Equals(Config.LastPoolDate, Today, StringComparison.Ordinal))
        {
            await RefreshAsync(manual: false);
        }
        else
        {
            ApplyNext(force: false);
        }
    }

    private void OnRotationTick()
    {
        if (Config.Paused)
        {
            _nextSwitchAt = DateTime.MinValue;
            return;
        }

        if (_setThisSession && !WallpaperSetter.IsCurrent(Config.LastSetPath))
        {
            Pause("检测到壁纸被其它程序改动");
            return;
        }

        ApplyNext(force: false);
    }

    private void OnDayTick()
    {
        if (!string.Equals(Config.LastPoolDate, Today, StringComparison.Ordinal) && !_busy)
        {
            Log.Info($"跨天检测：{Config.LastPoolDate} → {Today}，开始换池");
            _ = RefreshAsync(manual: false);
        }
    }

    private void OnExternalTick()
    {
        if (Config.Paused || !_setThisSession) return;
        if (string.IsNullOrEmpty(Config.LastSetPath)) return;

        if (!WallpaperSetter.IsCurrent(Config.LastSetPath))
        {
            Pause("检测到壁纸被其它程序改动");
        }
    }

    public async Task RefreshAsync(bool manual)
    {
        if (_busy) return;

        _busy = true;
        SetStatus("正在拉取今日壁纸…");
        Changed?.Invoke();

        try
        {
            var theme = ResolveTheme();
            if (theme is null)
            {
                HandleFailure("没有可用的主题词（主题池为空）");
                return;
            }

            var date = Today;
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(6));
            var progress = new Progress<string>(s => SetStatus(s));

            var result = await _builder.BuildAsync(Config, theme, date, progress, cts.Token);

            if (result.Pool.Items.Count == 0)
            {
                HandleFailure("所有来源都没有拿到图" + (result.Notes.Count > 0 ? "（" + string.Join("; ", result.Notes) + "）" : ""));
                return;
            }

            Shuffle(result.Pool.Items);
            ConfigStore.SavePool(result.Pool);

            Pool = result.Pool;
            Config.LastPoolDate = date;
            Config.LastThemeDisplay = theme.Display;
            Config.CurrentIndex = -1;
            Config.LastRefreshAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Config.LastRefreshOk = true;
            Config.LastError = result.Notes.Count > 0 ? string.Join("; ", result.Notes) : null;
            RememberIds(date, result.Pool.Items);
            ConfigStore.Save(Config);

            _appliedThisRefresh = ApplyNext(force: true);
            StorageService.Cleanup(Config);

            var source = string.IsNullOrEmpty(result.FinalStage) ? "未知" : result.FinalStage;
            if (_appliedThisRefresh)
            {
                SetStatus($"今日主题：{theme.Display}（{Pool.Items.Count} 张 · {source}）");
                Notify?.Invoke("今日壁纸已就绪", $"{theme.Display} · {Pool.Items.Count} 张 · 来源 {source}");
            }
            else
            {
                Notify?.Invoke("壁纸已下载，但没能设为桌面", $"今日主题：{theme.Display} · {Pool.Items.Count} 张已存到 data\\pool");
            }

            if (_retryTimer is not null) { _retryTimer.Stop(); _retryTimer = null; }
        }
        catch (OperationCanceledException)
        {
            HandleFailure("拉取超时（6 分钟）");
        }
        catch (Exception ex)
        {
            Log.Error("刷新壁纸池失败", ex);
            HandleFailure(ex.Message);
        }
        finally
        {
            _busy = false;
            Changed?.Invoke();
        }
    }

    private void HandleFailure(string message)
    {
        Log.Warn("[刷新] 失败: " + message);
        Config.LastRefreshOk = false;
        Config.LastError = message;
        Config.LastRefreshAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        ConfigStore.Save(Config);

        SetStatus("拉取失败：" + message);
        Notify?.Invoke("拉取壁纸失败", message + "；30 分钟后自动重试");

        ScheduleRetry();
    }

    private void ScheduleRetry()
    {
        if (_retryTimer is not null) _retryTimer.Stop();
        _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _retryTimer.Tick += async (_, _) =>
        {
            _retryTimer?.Stop();
            _retryTimer = null;
            await RefreshAsync(manual: false);
        };
        _retryTimer.Start();
    }

    private ThemeWord? ResolveTheme()
    {
        // 优先级：今日主题 → 固定主题 → 每日随机
        var todayTheme = ThemeCatalog.ResolveToday(Config, Today);
        if (todayTheme is not null)
        {
            Log.Info($"[主题] 使用今日主题：{todayTheme.Display}（{todayTheme.Query}）");
            return todayTheme;
        }

        if (Config.ThemeMode == ThemeSource.Fixed)
        {
            var fixedWord = ThemeCatalog.ResolveFixed(Config);
            if (fixedWord is not null) return fixedWord;
            Log.Warn("固定主题解析失败，退回每日随机");
        }
        return ThemeCatalog.PickRandom(Config, _rng);
    }

    // ---------- 主题管理 ----------

    public bool IsTodayThemeActive => ThemeCatalog.ResolveToday(Config, Today) is not null;

    public string TodayThemeLabel
    {
        get
        {
            var t = ThemeCatalog.ResolveToday(Config, Today);
            return t is null ? "（未指定，按模式自动选择）" : $"{t.Display}（{t.Query}）";
        }
    }

    /// <summary>当前今日主题的显示名；没指定时返回空串（供设置窗回填输入框）。</summary>
    public string ThemeLabelForToday() => ThemeCatalog.ResolveToday(Config, Today)?.Display ?? "";

    public void SetTodayTheme(string display, string query)
    {
        Config.TodayThemeDate = Today;
        Config.TodayThemeDisplay = display;
        Config.TodayThemeQuery = query;
        ConfigStore.Save(Config);
        Log.Info($"[主题] 今日主题设为 {display}（{query}）");
        Changed?.Invoke();
    }

    public void ClearTodayTheme()
    {
        Config.TodayThemeDate = "";
        Config.TodayThemeDisplay = "";
        Config.TodayThemeQuery = "";
        ConfigStore.Save(Config);
        Log.Info("[主题] 已清除今日主题");
        Changed?.Invoke();
    }

    /// <summary>设为今日主题并立刻重抓今天的十张。</summary>
    public async Task ApplyTodayThemeAsync(string display, string query)
    {
        SetTodayTheme(display, query);
        await RefreshAsync(manual: true);
    }

    /// <summary>用目前的 HTTP 配置试搜一个标签：null=连不上，0=没图，&gt;0=可用。</summary>
    public async Task<int?> ProbeThemeAsync(string query, CancellationToken ct)
    {
        using var http = HttpFactory.Create(Config, TimeSpan.FromSeconds(25));
        return await new WallhavenSource().ProbeTotalAsync(http, query, ct);
    }

    public async Task<Translator.Result> TranslateAsync(string chinese, CancellationToken ct)
    {
        using var http = HttpFactory.Create(Config, TimeSpan.FromSeconds(20));
        return await Translator.TranslateAsync(chinese, http, ct);
    }

    /// <summary>新增自定义主题；同名或同标签已存在则返回 null。</summary>
    public ThemeWord? AddCustomTheme(string display, string query, bool verified)
    {
        display = (display ?? "").Trim();
        query = (query ?? "").Trim();
        if (display.Length == 0 || query.Length == 0) return null;
        if (ThemeCatalog.All(Config).Any(w => string.Equals(w.Query, query, StringComparison.OrdinalIgnoreCase))) return null;

        var word = new ThemeWord { Display = display, Query = query, Direction = ThemeCatalog.CustomGroup, BuiltIn = false, Verified = verified };
        Config.CustomThemes.Add(word);
        ConfigStore.Save(Config);
        Log.Info($"[主题] 新增自定义主题 {display}（{query}），已验证={verified}");
        Changed?.Invoke();
        return word;
    }

    /// <summary>修改一个主题：内置 → 写覆盖；自定义 → 直接改。</summary>
    public bool UpdateTheme(ThemeWord word, string display, string query, bool verified)
    {
        display = (display ?? "").Trim();
        query = (query ?? "").Trim();
        if (display.Length == 0 || query.Length == 0) return false;

        if (word.BuiltIn)
        {
            var original = word.OriginalQuery ?? word.Query;
            var existing = Config.ThemeOverrides.FirstOrDefault(o => string.Equals(o.OriginalQuery, original, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                Config.ThemeOverrides.Add(new ThemeOverride { OriginalQuery = original, Display = display, Query = query });
            }
            else
            {
                existing.Display = display;
                existing.Query = query;
            }
            Log.Info($"[主题] 覆盖内置词 {original} → {display}（{query}）");
        }
        else
        {
            var target = Config.CustomThemes.FirstOrDefault(t => string.Equals(t.Query, word.Query, StringComparison.OrdinalIgnoreCase));
            if (target is null) return false;
            target.Display = display;
            target.Query = query;
            target.Verified = verified;
            Log.Info($"[主题] 修改自定义主题 → {display}（{query}）");
        }

        ConfigStore.Save(Config);
        Changed?.Invoke();
        return true;
    }

    /// <summary>恢复内置词的默认显示名与标签。</summary>
    public void RestoreBuiltIn(ThemeWord word)
    {
        if (!word.BuiltIn) return;
        var original = word.OriginalQuery ?? word.Query;
        Config.ThemeOverrides.RemoveAll(o => string.Equals(o.OriginalQuery, original, StringComparison.OrdinalIgnoreCase));
        ConfigStore.Save(Config);
        Log.Info($"[主题] 已恢复内置词 {original} 的默认值");
        Changed?.Invoke();
    }

    /// <summary>删除自定义主题；内置词不可删（返回 false）。</summary>
    public bool DeleteTheme(ThemeWord word)
    {
        if (word.BuiltIn) return false;
        var removed = Config.CustomThemes.RemoveAll(t => string.Equals(t.Query, word.Query, StringComparison.OrdinalIgnoreCase));
        if (removed == 0) return false;

        Config.DisabledWords.RemoveAll(w => string.Equals(w, word.Query, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(Config.FixedThemeQuery, word.Query, StringComparison.OrdinalIgnoreCase))
        {
            Config.FixedThemeDisplay = "";
            Config.FixedThemeQuery = "";
        }
        if (string.Equals(Config.TodayThemeQuery, word.Query, StringComparison.OrdinalIgnoreCase))
            ClearTodayTheme();

        ConfigStore.Save(Config);
        Log.Info($"[主题] 已删除自定义主题 {word.Display}（{word.Query}）");
        Changed?.Invoke();
        return true;
    }

    /// <summary>启用/停用一个主题（内置按原始标签记，自定义按标签记）。</summary>
    public void SetThemeEnabled(ThemeWord word, bool enabled)
    {
        var key = word.BuiltIn ? (word.OriginalQuery ?? word.Query) : word.Query;
        Config.DisabledWords.RemoveAll(w => string.Equals(w, key, StringComparison.OrdinalIgnoreCase));
        if (!enabled) Config.DisabledWords.Add(key);
        ConfigStore.Save(Config);
        Log.Info($"[主题] {(enabled ? "启用" : "停用")} {word.Display}（{key}）");
        Changed?.Invoke();
    }

    /// <summary>把某个主题设为固定主题（并切到固定模式）。</summary>
    public void SetFixedTheme(ThemeWord word)
    {
        Config.ThemeMode = ThemeSource.Fixed;
        Config.FixedThemeDisplay = word.Display;
        Config.FixedThemeQuery = word.Query;
        ConfigStore.Save(Config);
        Log.Info($"[主题] 固定主题设为 {word.Display}（{word.Query}）");
        Changed?.Invoke();
    }

    private void Shuffle(List<WallpaperItem> items)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = _rng.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    private void RememberIds(string date, List<WallpaperItem> items)
    {
        foreach (var item in items)
        {
            var entry = PoolBuilder.MakeRecentEntry(date, item.Id);
            if (!Config.RecentIds.Contains(entry)) Config.RecentIds.Add(entry);
        }

        // 只保留最近 7 天的去重记录（条目格式 yyyy-MM-dd|id）
        Config.RecentIds = PoolBuilder.PruneRecent(Config.RecentIds, DateTime.Today, 7);
    }

    private bool ApplyNext(bool force)
    {
        if (Pool is null || Pool.Items.Count == 0) return false;
        if (Config.Paused && !force) return false;
        if (Config.Paused && force) Resume();

        var total = Pool.Items.Count;
        var start = Config.CurrentIndex;

        // 逐张尝试，失败就换下一张；整池都失败才报错退出（绝不递归，否则会无限打日志）
        for (var attempt = 0; attempt < total; attempt++)
        {
            var index = (start + 1 + attempt) % total;
            var item = Pool.Items[index];
            var full = Path.Combine(Paths.PoolDir(Pool.Date), item.File);

            if (!WallpaperSetter.Set(full))
            {
                Log.Warn($"[轮播] 第 {index + 1} 张设置失败，换下一张");
                continue;
            }

            Current = item;
            Config.CurrentIndex = index;
            Config.LastSetPath = full;
            _setThisSession = true;
            _nextSwitchAt = DateTime.Now.AddMinutes(Config.RotationMinutes);
            ConfigStore.Save(Config);

            SetStatus($"正在显示：{Pool.ThemeDisplay} · {index + 1}/{total}");
            Log.Info($"[轮播] 已设置 {Paths.Pretty(full)}");
            Changed?.Invoke();
            return true;
        }

        // 整池都写不进桌面：多半是被更高完整性级别的进程/安全软件挡住了
        Config.LastSetPath = null;
        var detail = WallpaperSetter.LastFailureDetail;
        SetStatus($"设置桌面壁纸失败：{total} 张都未能生效（{detail}）");
        Log.Warn("[轮播] 整池设置失败，停止本次切换，等下一次间隔再试");
        Blocked?.Invoke(detail);
        Changed?.Invoke();
        return false;
    }

    public void Pause(string reason)
    {
        if (Config.Paused) return;
        Config.Paused = true;
        Config.PausedReason = reason;
        _nextSwitchAt = DateTime.MinValue;
        ConfigStore.Save(Config);
        SetStatus("轮播已暂停：" + reason);
        Log.Info("[轮播] 暂停: " + reason);
        Notify?.Invoke("轮播已暂停", reason + "。点托盘「恢复轮播」继续。");
        Changed?.Invoke();
    }

    public void Resume()
    {
        if (!Config.Paused) return;
        Config.Paused = false;
        Config.PausedReason = null;
        _nextSwitchAt = DateTime.Now.AddMinutes(Config.RotationMinutes);
        ConfigStore.Save(Config);
        SetStatus("轮播已恢复");
        Log.Info("[轮播] 恢复");
        Changed?.Invoke();
    }

    public void TogglePause()
    {
        if (Config.Paused) Resume();
        else Pause("手动暂停");
    }

    /// <summary>手动换下一张：暂停状态下先恢复，再切。</summary>
    public void NextNow()
    {
        if (Config.Paused) Resume();
        ApplyNext(force: false);
    }

    public void ApplyRotationInterval()
    {
        if (_rotationTimer is null) return;
        _rotationTimer.Interval = TimeSpan.FromMinutes(Config.RotationMinutes);
        _nextSwitchAt = DateTime.Now.AddMinutes(Config.RotationMinutes);
        Changed?.Invoke();
    }

    public void ExcludeCurrent()
    {
        if (Current is null || Pool is null)
        {
            Notify?.Invoke("没有可排除的图", "当前没有正在显示壁纸池里的图");
            return;
        }

        var id = Current.Id;
        if (!Config.Excluded.Contains(id, StringComparer.OrdinalIgnoreCase))
            Config.Excluded.Add(id);

        var removed = Pool.Items.RemoveAll(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        ConfigStore.SavePool(Pool);
        ConfigStore.Save(Config);

        SetStatus($"已排除：{Current.File}（{removed} 张移出今日池）");
        Log.Info($"[排除] {id}，今日池剩余 {Pool.Items.Count} 张");

        if (Pool.Items.Count == 0)
        {
            Pool = null;
            Current = null;
            Config.CurrentIndex = -1;
            ConfigStore.Save(Config);
            _ = RefreshAsync(manual: true);
            return;
        }

        Config.CurrentIndex = -1;
        ApplyNext(force: true);
    }

    public void FavoriteCurrent()
    {
        if (Current is null || Pool is null)
        {
            Notify?.Invoke("没有可收藏的图", "当前没有正在显示壁纸池里的图");
            return;
        }

        try
        {
            var src = Path.Combine(Paths.PoolDir(Pool.Date), Current.File);
            if (!File.Exists(src)) return;

            Directory.CreateDirectory(Paths.FavoritesDir);
            var ext = Path.GetExtension(src);
            var safe = Current.Id.Replace(':', '-');
            var destName = safe + ext;
            var dest = Path.Combine(Paths.FavoritesDir, destName);

            if (!File.Exists(dest)) File.Copy(src, dest);

            if (!Config.Favorites.Any(f => string.Equals(f.Id, Current.Id, StringComparison.OrdinalIgnoreCase)))
            {
                Config.Favorites.Add(new FavoriteEntry
                {
                    Id = Current.Id,
                    File = destName,
                    Theme = Pool.ThemeDisplay,
                    AddedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                    PageUrl = Current.PageUrl,
                });
                ConfigStore.Save(Config);
            }

            Notify?.Invoke("已收藏", Current.File + " 已复制到 data\\favorites");
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("[收藏] 失败", ex);
            Notify?.Invoke("收藏失败", ex.Message);
        }
    }

    public void RemoveFavorite(FavoriteEntry entry)
    {
        try
        {
            var file = Path.Combine(Paths.FavoritesDir, entry.File);
            if (File.Exists(file)) File.Delete(file);
            Config.Favorites.RemoveAll(f => string.Equals(f.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
            ConfigStore.Save(Config);
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("[收藏] 删除失败", ex);
        }
    }

    public void RestoreExcluded(string id)
    {
        Config.Excluded.RemoveAll(e => string.Equals(e, id, StringComparison.OrdinalIgnoreCase));
        ConfigStore.Save(Config);
        Changed?.Invoke();
    }

    public void SetAutostart(bool enabled)
    {
        Config.Autostart = enabled;
        ConfigStore.Save(Config);
        ApplyAutostart();
    }

    /// <summary>开机自启：提权实例用计划任务（/RL HIGHEST），普通实例用 HKCU Run。</summary>
    public void ApplyAutostart()
    {
        if (Elevation.IsElevated())
        {
            var exe = Environment.ProcessPath ?? "";
            if (Config.Autostart) ScheduledTask.Ensure(exe, atLogon: true);
            else if (ScheduledTask.Exists()) ScheduledTask.Delete();
        }
        else
        {
            StartupService.Sync(Config.Autostart);
        }
    }

    public bool IsAutostartOn()
        => Elevation.IsElevated() ? ScheduledTask.IsAtLogon() : StartupService.IsEnabled();

    public void SetProxy(ProxyMode mode, string address)
    {
        Config.ProxyMode = mode;
        Config.ProxyAddress = address;
        ConfigStore.Save(Config);
    }

    public void SetRotationMinutes(int minutes)
    {
        Config.RotationMinutes = Math.Clamp(minutes, 1, 1440);
        ConfigStore.Save(Config);
        ApplyRotationInterval();
    }

    public void SetNotifications(bool enabled)
    {
        Config.Notifications = enabled;
        ConfigStore.Save(Config);
    }

    public void SaveConfig() => ConfigStore.Save(Config);

    public void OpenDataFolder()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Paths.Root + "\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("打开 data 目录失败: " + ex.Message); }
    }

    public void OpenFavoritesFolder()
    {
        try
        {
            Directory.CreateDirectory(Paths.FavoritesDir);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Paths.FavoritesDir + "\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Warn("打开收藏目录失败: " + ex.Message); }
    }

    public void OpenCurrentLocation()
    {
        var path = CurrentPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            OpenDataFolder();
            return;
        }
        try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("打开图片位置失败: " + ex.Message); }
    }

    public void OpenLog()
    {
        try
        {
            Directory.CreateDirectory(Paths.LogsDir);
            if (File.Exists(Log.CurrentFile))
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + Log.CurrentFile + "\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Paths.LogsDir + "\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Warn("打开日志失败: " + ex.Message); }
    }

    public void CleanupNow() => StorageService.Cleanup(Config);

    /// <summary>把今日池子里已有的图重新裁一遍黑边（给旧池子做补救）。返回改动的张数。</summary>
    public int RetrimCurrentPool()
    {
        var pool = Pool ?? ConfigStore.LoadPool(Today);
        if (pool is null) return 0;

        var (screenW, screenH) = ImageTools.TargetScreenSize();
        Log.Info($"[图片] 开始重裁今日池子，目标 {screenW}x{screenH}");

        var changed = 0;
        foreach (var item in pool.Items)
        {
            var full = Path.Combine(Paths.PoolDir(pool.Date), item.File);
            var result = ImageTools.Process(full, screenW, screenH);
            if (!result.Modified) continue;

            changed++;
            try
            {
                var bytes = File.ReadAllBytes(full);
                var (w, h) = PoolBuilder.ProbeSize(bytes);
                item.Width = w;
                item.Height = h;
                item.Bytes = bytes.Length;
            }
            catch (Exception ex)
            {
                Log.Warn($"[图片] 重读尺寸失败 {item.File}: {ex.Message}");
            }
        }

        if (changed > 0) ConfigStore.SavePool(pool);
        Log.Info($"[图片] 重裁完成，改动 {changed} 张");
        return changed;
    }

    /// <summary>把当前池子里正在显示的那张重新设成桌面壁纸。</summary>
    public bool ReapplyCurrent()
    {
        var path = CurrentPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
        return WallpaperSetter.Set(path);
    }

    private void SetStatus(string text)
    {
        if (string.Equals(_status, text, StringComparison.Ordinal)) return;
        _status = text;
        Log.Info("[状态] " + text);
    }
}
