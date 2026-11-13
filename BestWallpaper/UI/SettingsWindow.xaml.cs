using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BestWallpaper.Models;
using BestWallpaper.Services;

namespace BestWallpaper.UI;

public sealed class FavoriteVm
{
    public FavoriteEntry Entry { get; init; } = new();
    public BitmapImage? Image { get; init; }
    public string Title { get; init; } = "";
    public string Sub { get; init; } = "";
}

public partial class SettingsWindow : Window
{
    private readonly Engine _engine;
    private readonly DispatcherTimer _tick;
    private readonly ObservableCollection<FavoriteVm> _favorites = new();
    private bool _forceClose;
    private bool _suppress;
    private int _lastFavoriteCount = -1;

    public SettingsWindow(Engine engine)
    {
        _engine = engine;
        InitializeComponent();

        var version = typeof(SettingsWindow).Assembly.GetName().Version;
        VersionText.Text = version is null ? "" : $"v{version.Major}.{version.Minor}.{version.Build}";

        _suppress = true;
        BuildStaticLists();
        _suppress = false;

        LoadFromConfig();
        RefreshThemeCombo();
        FavoritesList.ItemsSource = _favorites;

        _engine.Changed += OnEngineChanged;
        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => UpdateStatus();
        _tick.Start();

        UpdateStatus();
        RefreshExcluded();
        RefreshFavorites();
        _ = RefreshUsageAsync();

        Closing += (_, e) =>
        {
            if (_forceClose) return;
            e.Cancel = true;
            Hide();
        };

        Closed += (_, _) =>
        {
            _tick.Stop();
            _engine.Changed -= OnEngineChanged;
        };
    }

    public void ForceClose()
    {
        _forceClose = true;
        try { Close(); } catch { }
    }

    private void BuildStaticLists()
    {
        ProxyCombo.Items.Clear();
        ProxyCombo.Items.Add("跟随系统代理");
        ProxyCombo.Items.Add("直连（不使用代理）");
        ProxyCombo.Items.Add("手动指定代理");

        RotationCombo.Items.Clear();
        foreach (var m in new[] { 1, 2, 5, 10, 15, 30, 60, 120, 240, 720, 1440 })
            RotationCombo.Items.Add(m);

        DirectionPanel.Children.Clear();
        foreach (var dir in ThemeCatalog.Directions)
        {
            var cb = new CheckBox
            {
                Content = dir.Name,
                Margin = new Thickness(0, 0, 14, 6),
                IsChecked = !_engine.Config.DisabledDirections.Contains(dir.Name),
                Tag = dir.Name,
            };
            cb.Click += Direction_Click;
            DirectionPanel.Children.Add(cb);
        }
    }

    private void LoadFromConfig()
    {
        var cfg = _engine.Config;

        DailyRadio.IsChecked = cfg.ThemeMode == ThemeSource.DailyRandom;
        FixedRadio.IsChecked = cfg.ThemeMode == ThemeSource.Fixed;

        ProxyCombo.SelectedIndex = (int)cfg.ProxyMode;
        ProxyAddressBox.Text = cfg.ProxyAddress;
        ProxyAddressBox.IsEnabled = cfg.ProxyMode == ProxyMode.Manual;
        ProxyHint.Text = cfg.ProxyMode switch
        {
            ProxyMode.System => "默认。.NET 会读系统的 HTTP(S)_PROXY 与 Windows 代理设置，Clash 开着就自动走。",
            ProxyMode.Direct => "不走代理。注意：wallhaven 在多数大陆网络下直连会被 DNS 污染，只能拿到 Bing 与本地历史图。",
            _ => "手动填写 host:port，例如 127.0.0.1:7892（会自动补 http://）。",
        };

        AutostartCheck.IsChecked = cfg.Autostart;
        DelayBox.Text = cfg.StartDelaySeconds.ToString();
        RotationCombo.Text = cfg.RotationMinutes.ToString();
    }

    private void RefreshThemeCombo()
    {
        var words = ThemeCatalog.Pool(_engine.Config);
        ThemeCombo.ItemsSource = words;
        ThemeCombo.DisplayMemberPath = "Display";

        var selected = words.FirstOrDefault(w => string.Equals(w.Display, _engine.Config.FixedThemeDisplay, StringComparison.Ordinal));
        if (selected is not null) ThemeCombo.SelectedItem = selected;
        else if (words.Count > 0) ThemeCombo.SelectedIndex = 0;

        ThemeCombo.IsEnabled = _engine.Config.ThemeMode == ThemeSource.Fixed;
        RefreshTodayCombo();
        _suppress = true;
        LoadFromConfig();
        _suppress = false;
    }

    /// <summary>今日主题下拉：可选项是当前主题池，也能直接输入一个不存在的词。</summary>
    private void RefreshTodayCombo()
    {
        var words = ThemeCatalog.Pool(_engine.Config);
        TodayCombo.ItemsSource = words;
        TodayCombo.DisplayMemberPath = "Display";

        var today = _engine.ThemeLabelForToday();
        if (!string.IsNullOrEmpty(today))
        {
            var match = words.FirstOrDefault(w => string.Equals(w.Display, today, StringComparison.Ordinal));
            if (match is not null) TodayCombo.SelectedItem = match;
            else TodayCombo.Text = today;
        }

        TodayHint.Text = _engine.IsTodayThemeActive
            ? $"今日已指定：{_engine.TodayThemeLabel}（明天自动失效）。"
            : "未指定今日主题：当前按" + (_engine.Config.ThemeMode == ThemeSource.Fixed ? "固定主题" : "每天随机") + "选择。";
    }

    private void OnEngineChanged() => Dispatcher.Invoke(() =>
    {
        UpdateStatus();
        RefreshTodayCombo();
        RefreshFavorites();
        RefreshExcluded();
        _ = RefreshUsageAsync();
    });

    private void UpdateStatus()
    {
        StatusText.Text = _engine.Status;
        PauseBtn.Content = _engine.Config.Paused ? "恢复轮播" : "暂停轮播";

        var parts = new List<string>
        {
            $"主题：{_engine.ThemeLabel}",
            $"今日池：{_engine.PoolCount} 张",
        };

        if (_engine.Config.Paused)
            parts.Add("轮播已暂停" + (string.IsNullOrEmpty(_engine.Config.PausedReason) ? "" : "（" + _engine.Config.PausedReason + "）"));
        else if (_engine.NextSwitchIn > TimeSpan.Zero)
            parts.Add($"下次切换：{FormatSpan(_engine.NextSwitchIn)} 后");
        else
            parts.Add("尚未开始轮播");

        if (!string.IsNullOrEmpty(_engine.Config.LastRefreshAt))
            parts.Add("上次拉取：" + _engine.Config.LastRefreshAt + (_engine.Config.LastRefreshOk ? "" : "（失败）"));

        SubStatusText.Text = string.Join("　·　", parts);
        TodayHint.Text = _engine.IsTodayThemeActive
            ? $"今日已指定：{_engine.TodayThemeLabel}（明天自动失效）。"
            : "未指定今日主题：当前按" + (_engine.Config.ThemeMode == ThemeSource.Fixed ? "固定主题" : "每天随机") + "选择。";
    }

    private static string FormatSpan(TimeSpan span)
        => span.TotalSeconds < 60 ? $"{span.TotalSeconds:F0} 秒" : $"{(int)span.TotalMinutes} 分 {span.Seconds} 秒";

    private void RefreshFavorites()
    {
        var cfg = _engine.Config;
        if (cfg.Favorites.Count == _lastFavoriteCount) return;
        _lastFavoriteCount = cfg.Favorites.Count;

        _favorites.Clear();
        foreach (var fav in cfg.Favorites)
        {
            var path = Path.Combine(Paths.FavoritesDir, fav.File);
            BitmapImage? image = null;
            try
            {
                if (File.Exists(path))
                {
                    image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = 200;
                    image.UriSource = new Uri(path, UriKind.Absolute);
                    image.EndInit();
                    image.Freeze();
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"[设置] 缩略图加载失败 {fav.File}: {ex.Message}");
            }

            _favorites.Add(new FavoriteVm
            {
                Entry = fav,
                Image = image,
                Title = fav.File,
                Sub = $"{fav.Theme}　{fav.AddedAt}",
            });
        }

        FavoritesEmptyText.Visibility = _favorites.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshExcluded()
    {
        var list = _engine.Config.Excluded;
        ExcludedList.ItemsSource = null;
        ExcludedList.ItemsSource = list.ToList();
        ExcludedEmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task RefreshUsageAsync()
    {
        try
        {
            var usage = await Task.Run(StorageService.Measure);
            UsageText.Text = usage.Text + " · 上限 2GB / 保留 7 天";
        }
        catch (Exception ex)
        {
            Log.Warn("[设置] 统计占用失败: " + ex.Message);
        }
    }

    // ---------- 事件处理 ----------

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        _engine.NextNow();
        UpdateStatus();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshBtn.IsEnabled = false;
        try
        {
            await _engine.RefreshAsync(manual: true);
        }
        finally
        {
            RefreshBtn.IsEnabled = true;
            UpdateStatus();
        }
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        _engine.TogglePause();
        UpdateStatus();
    }

    private void OpenCurrent_Click(object sender, RoutedEventArgs e) => _engine.OpenCurrentLocation();

    private void ThemeMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        var cfg = _engine.Config;
        cfg.ThemeMode = FixedRadio.IsChecked == true ? ThemeSource.Fixed : ThemeSource.DailyRandom;

        if (cfg.ThemeMode == ThemeSource.Fixed && string.IsNullOrEmpty(cfg.FixedThemeDisplay))
        {
            var first = ThemeCatalog.Pool(cfg).FirstOrDefault();
            if (first is not null)
            {
                cfg.FixedThemeDisplay = first.Display;
                cfg.FixedThemeQuery = first.Query;
            }
        }

        ThemeCombo.IsEnabled = cfg.ThemeMode == ThemeSource.Fixed;
        _engine.SaveConfig();
        UpdateStatus();
    }

    private void ThemeCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        if (ThemeCombo.SelectedItem is not ThemeWord word) return;
        _engine.Config.FixedThemeDisplay = word.Display;
        _engine.Config.FixedThemeQuery = word.Query;
        _engine.SaveConfig();
    }

    private void AddCustom_Click(object sender, RoutedEventArgs e)
    {
        // 主题的新增/修改/删除都搬到「主题管理」窗口了，这里只留入口。
        OpenThemeManager();
    }

    private void OpenThemeManager_Click(object sender, RoutedEventArgs e) => OpenThemeManager();

    private void OpenThemeManager()
    {
        var window = new ThemeManagerWindow(_engine) { Owner = IsVisible ? this : null };
        window.ShowDialog();
        RefreshThemeCombo();
        UpdateStatus();
    }

    /// <summary>应用今日主题：下拉里选已有的直接用；输入新词则先翻译+试搜，再存成自定义。</summary>
    private async void ApplyToday_Click(object sender, RoutedEventArgs e)
    {
        var text = (TodayCombo.Text ?? "").Trim();
        if (text.Length == 0)
        {
            MessageBox.Show(this, "先在输入框里填一个主题名，或从下拉里选一个。", "今日主题",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var existing = ThemeCatalog.Find(_engine.Config, text);
        if (existing is not null)
        {
            await _engine.ApplyTodayThemeAsync(existing.Display, existing.Query);
            RefreshTodayCombo();
            UpdateStatus();
            return;
        }

        // 主题池里没有：走"翻译 → 试搜 → 存为自定义 → 设为今日"
        var translated = await _engine.TranslateAsync(text, System.Threading.CancellationToken.None);
        if (translated.Tag is null)
        {
            MessageBox.Show(this,
                $"「{text}」不在主题池里，也没能翻出英文标签（{translated.Source}）。\n" +
                "请打开「主题管理…」手动填英文标签。",
                "无法自动添加", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var total = await _engine.ProbeThemeAsync(translated.Tag, System.Threading.CancellationToken.None);
        var verified = total is > 0;
        if (total == 0 &&
            MessageBox.Show(this, $"标签「{translated.Tag}」在 wallhaven 上搜不到图，仍要添加并应用吗？", "试搜无结果",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }
        if (total is null)
            MessageBox.Show(this, "网络不可用，没能验证标签，将按「未验证」保存。", "今日主题",
                MessageBoxButton.OK, MessageBoxImage.Information);

        var added = _engine.AddCustomTheme(text, translated.Tag, verified);
        if (added is null)
        {
            MessageBox.Show(this, "添加失败：标签可能已被占用。", "今日主题", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await _engine.ApplyTodayThemeAsync(added.Display, added.Query);
        RefreshTodayCombo();
        UpdateStatus();
    }

    private void Direction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb || cb.Tag is not string name) return;
        var cfg = _engine.Config;
        var disabled = cb.IsChecked != true;
        if (disabled && !cfg.DisabledDirections.Contains(name)) cfg.DisabledDirections.Add(name);
        if (!disabled) cfg.DisabledDirections.RemoveAll(d => d == name);
        _engine.SaveConfig();
        RefreshThemeCombo();
    }

    private void Rotation_Changed(object sender, SelectionChangedEventArgs e) => ApplyRotation();

    private void Rotation_LostFocus(object sender, RoutedEventArgs e) => ApplyRotation();

    private void ApplyRotation()
    {
        if (_suppress) return;
        if (!int.TryParse((RotationCombo.Text ?? "").Trim(), out var minutes)) return;
        if (minutes < 1 || minutes > 1440) return;
        _engine.SetRotationMinutes(minutes);
        UpdateStatus();
    }

    private void Proxy_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        var mode = ProxyCombo.SelectedIndex switch
        {
            1 => ProxyMode.Direct,
            2 => ProxyMode.Manual,
            _ => ProxyMode.System,
        };
        ProxyAddressBox.IsEnabled = mode == ProxyMode.Manual;
        _engine.SetProxy(mode, ProxyAddressBox.Text);
        ProxyHint.Text = mode switch
        {
            ProxyMode.System => "默认。.NET 会读系统的 HTTP(S)_PROXY 与 Windows 代理设置，Clash 开着就自动走。",
            ProxyMode.Direct => "不走代理。注意：wallhaven 在多数大陆网络下直连会被 DNS 污染，只能拿到 Bing 与本地历史图。",
            _ => "手动填写 host:port，例如 127.0.0.1:7892（会自动补 http://）。",
        };
    }

    private void Proxy_LostFocus(object sender, RoutedEventArgs e)
    {
        if (ProxyCombo.SelectedIndex != 2) return;
        _engine.SetProxy(ProxyMode.Manual, ProxyAddressBox.Text);
    }

    private void Autostart_Click(object sender, RoutedEventArgs e)
    {
        var want = AutostartCheck.IsChecked == true;
        _engine.SetAutostart(want);
        var actual = _engine.IsAutostartOn();
        if (want != actual)
        {
            AutostartCheck.IsChecked = actual;
            MessageBox.Show(this,
                "写入开机启动失败（详见日志）。\n\n" +
                "本机资源管理器以管理员身份运行，普通权限的开机自启无法换壁纸；" +
                "请先在弹窗里选择「以管理员身份重启」，再在这里勾选。",
                "自启动", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Delay_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse((DelayBox.Text ?? "").Trim(), out var seconds)) return;
        seconds = Math.Clamp(seconds, 0, 600);
        DelayBox.Text = seconds.ToString();
        _engine.Config.StartDelaySeconds = seconds;
        _engine.SaveConfig();
    }

    private void RemoveFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not FavoriteVm vm) return;
        _engine.RemoveFavorite(vm.Entry);
        _lastFavoriteCount = -1;
        RefreshFavorites();
        _ = RefreshUsageAsync();
    }

    private void RestoreExcluded_Click(object sender, RoutedEventArgs e)
    {
        if (ExcludedList.SelectedItem is not string id) return;
        _engine.RestoreExcluded(id);
        RefreshExcluded();
    }

    private void OpenData_Click(object sender, RoutedEventArgs e) => _engine.OpenDataFolder();

    private void OpenFavorites_Click(object sender, RoutedEventArgs e) => _engine.OpenFavoritesFolder();

    private void Cleanup_Click(object sender, RoutedEventArgs e)
    {
        _engine.CleanupNow();
        _ = RefreshUsageAsync();
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e) => _engine.OpenLog();

    private void OpenWallhaven_Click(object sender, RoutedEventArgs e) => OpenUrl("https://wallhaven.cc");

    private void OpenBing_Click(object sender, RoutedEventArgs e) => OpenUrl("https://www.bing.com");

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("打开链接失败: " + ex.Message); }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
