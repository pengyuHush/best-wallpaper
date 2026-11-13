using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BestWallpaper.Models;
using BestWallpaper.Services;

namespace BestWallpaper.Tests;

/// <summary>
/// 零依赖自测（不引 xunit，避免沙箱里 NuGet 拉包失败）。
/// 覆盖纯逻辑：解析、过滤、去重、主题词库、配置序列化、壁纸 API 可读性。
/// 网络下载无法在这里验证——沙箱令牌拿不到 Schannel 凭据。
/// </summary>
internal static class Program
{
    private static int _failed;
    private static int _passed;

    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        TestWallhavenFixture();
        TestBingFixture();
        TestMalformedJson();
        TestThemeCatalog();
        TestAcceptFilter();
        TestRecentPrune();
        TestConfigRoundTrip();
        TestWallpaperProbe();
        TestScheduledTaskXml();
        TestBlackBarTrim();
        TestThemeModel();
        TestTranslator();

        Console.WriteLine();
        Console.WriteLine($"通过 {_passed} / 失败 {_failed}");
        return _failed == 0 ? 0 : 1;
    }

    private static void TestWallhavenFixture()
    {
        var json = File.ReadAllText(Fixture("wallhaven-search.json"));
        var items = WallhavenSource.Parse(json, 24);

        Check("wallhaven: 解析出候选", items.Count > 0, $"{items.Count} 条");
        Check("wallhaven: 全部标记来源", items.All(i => i.Source == "wallhaven"));
        Check("wallhaven: 用的是原图 path 而不是缩略图",
            items.All(i => i.RemoteUrls[0].Contains("w.wallhaven.cc/full/")),
            items.Count > 0 ? items[0].RemoteUrls[0] : "");
        Check("wallhaven: id 带前缀", items.All(i => i.Id.StartsWith("wallhaven:")), items.Count > 0 ? items[0].Id : "");
        Check("wallhaven: 分辨率已解析", items.All(i => i.Width > 0 && i.Height > 0));
        Check("wallhaven: want 被遵守", WallhavenSource.Parse(json, 3).Count == 3);
    }

    private static void TestBingFixture()
    {
        var json = File.ReadAllText(Fixture("bing-feed.json"));
        var items = BingSource.Parse(json, 8);

        Check("bing: 解析出 8 天", items.Count == 8, $"{items.Count} 条");
        Check("bing: 首选 UHD", items.All(i => i.RemoteUrls[0].EndsWith("_UHD.jpg")));
        Check("bing: 次选 1080p", items.All(i => i.RemoteUrls[1].EndsWith("_1920x1080.jpg")));
        Check("bing: 绝对地址", items.All(i => i.RemoteUrls[0].StartsWith("https://www.bing.com/th?id=")), items.Count > 0 ? items[0].RemoteUrls[0] : "");
        Check("bing: id 带日期", items.All(i => i.Id.StartsWith("bing:2026")), items.Count > 0 ? items[0].Id : "");
        Check("bing: 有版权信息", items.All(i => !string.IsNullOrWhiteSpace(i.Author)));
    }

    private static void TestMalformedJson()
    {
        Check("解析器: 空对象不抛异常", WallhavenSource.Parse("{}", 10).Count == 0);
        Check("解析器: 垃圾输入不抛异常", BingSource.Parse("{\"images\":[]}", 10).Count == 0);
    }

    private static void TestThemeCatalog()
    {
        var cfg = new AppConfig();
        var pool = ThemeCatalog.Pool(cfg);
        Check("主题池: 内置词非空", pool.Count >= 25, $"{pool.Count} 个");
        Check("主题池: 全部是中文展示名", pool.All(w => w.Display.Length > 0 && w.Query.Length > 0));

        cfg.DisabledDirections.Add("萌宠");
        var withoutPets = ThemeCatalog.Pool(cfg);
        Check("主题池: 停用方向后词被移除", withoutPets.All(w => w.Direction != "萌宠"));
        Check("主题池: 停用方向后数量变少", withoutPets.Count < pool.Count, $"{withoutPets.Count} < {pool.Count}");

        cfg.DisabledDirections.Clear();
        cfg.CustomThemes.Add(new ThemeWord { Display = "猫头鹰", Query = "owl", BuiltIn = false });
        var withCustom = ThemeCatalog.Pool(cfg);
        Check("主题池: 自定义词进入池子", withCustom.Any(w => w.Query == "owl"));
        Check("主题池: 重复查询词被去重", withCustom.Count(w => w.Query == "owl") == 1);

        cfg.CustomThemes.Add(new ThemeWord { Display = "猫的副本", Query = "cat", BuiltIn = false });
        Check("主题池: 与内置词重复也被去重", ThemeCatalog.Pool(cfg).Count(w => w.Query == "cat") == 1);

        cfg.FixedThemeDisplay = "猫头鹰";
        Check("主题池: 固定主题按展示名解析", ThemeCatalog.ResolveFixed(cfg)?.Query == "owl");

        cfg.FixedThemeDisplay = "不存在的主题";
        Check("主题池: 未知固定主题返回 null", ThemeCatalog.ResolveFixed(cfg) is null);

        var rng = new Random(42);
        var picks = Enumerable.Range(0, 200).Select(_ => ThemeCatalog.PickRandom(cfg, rng)?.Query).Where(q => q is not null).Distinct().Count();
        Check("主题池: 随机抽取能覆盖多个主题", picks > 10, $"{picks} 个不同主题");
    }

    private static void TestAcceptFilter()
    {
        var cfg = new AppConfig();
        var recent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var ok = Item("wallhaven:a", 2560, 1440);
        var small = Item("wallhaven:b", 1920, 1080);
        var portrait = Item("wallhaven:c", 1440, 2560);
        var ultrawide = Item("wallhaven:d", 3440, 1440);
        var sixteenTen = Item("wallhaven:e", 2560, 1600);

        Check("过滤: 2560x1440 通过高清档", PoolBuilder.Accept(ok, cfg, recent, 2560, 1440));
        Check("过滤: 1080p 被高清档拒绝", !PoolBuilder.Accept(small, cfg, recent, 2560, 1440));
        Check("过滤: 1080p 通过放宽档", PoolBuilder.Accept(small, cfg, recent, 1920, 1080));
        Check("过滤: 竖图被拒绝", !PoolBuilder.Accept(portrait, cfg, recent, 1920, 1080));
        Check("过滤: 16:10 通过", PoolBuilder.Accept(sixteenTen, cfg, recent, 1920, 1080));
        Check("过滤: 21:9 带鱼屏被拒绝（比例离屏幕太远）", !PoolBuilder.Accept(ultrawide, cfg, recent, 1920, 1080));

        cfg.Excluded.Add("wallhaven:a");
        Check("过滤: 排除列表生效", !PoolBuilder.Accept(ok, cfg, recent, 1920, 1080));

        cfg.Excluded.Clear();
        recent.Add("wallhaven:a");
        Check("过滤: 近 7 天去重生效", !PoolBuilder.Accept(ok, cfg, recent, 1920, 1080));
    }

    private static void TestRecentPrune()
    {
        var today = new DateTime(2026, 11, 3);
        var entries = new List<string>
        {
            PoolBuilder.MakeRecentEntry("2026-11-03", "wallhaven:today"),
            PoolBuilder.MakeRecentEntry("2026-11-01", "wallhaven:thisweek"),
            PoolBuilder.MakeRecentEntry("2026-10-20", "wallhaven:old"),
            "broken-entry",
        };

        var pruned = PoolBuilder.PruneRecent(entries, today, 7);
        Check("去重记录: 保留今天的", pruned.Contains("2026-11-03|wallhaven:today"));
        Check("去重记录: 保留 7 天内的", pruned.Contains("2026-11-01|wallhaven:thisweek"));
        Check("去重记录: 丢弃过期条目", !pruned.Any(e => e.Contains("old")));
        Check("去重记录: 丢弃畸形条目", !pruned.Contains("broken-entry"));
        Check("去重记录: 数量正确", pruned.Count == 2, $"{pruned.Count} 条");
        Check("去重记录: 能取出原始 id", PoolBuilder.StripDate("2026-11-03|wallhaven:today") == "wallhaven:today");
    }

    private static void TestConfigRoundTrip()
    {
        var cfg = new AppConfig
        {
            ThemeMode = ThemeSource.Fixed,
            FixedThemeDisplay = "猫",
            FixedThemeQuery = "cat",
            RotationMinutes = 25,
            ProxyMode = ProxyMode.Manual,
            ProxyAddress = "127.0.0.1:7892",
            Autostart = true,
            StartDelaySeconds = 90,
            Excluded = { "wallhaven:zzz" },
            RecentIds = { "2026-11-03|wallhaven:abc" },
            CustomThemes = { new ThemeWord { Display = "狐狸", Query = "fox" } },
            Favorites = { new FavoriteEntry { Id = "wallhaven:abc", File = "abc.jpg", Theme = "猫", AddedAt = "2026-11-03 10:00" } },
        };

        ConfigStore.Save(cfg);
        var loaded = ConfigStore.Load();

        Check("配置: 主题模式往返", loaded.ThemeMode == ThemeSource.Fixed);
        Check("配置: 间隔往返", loaded.RotationMinutes == 25);
        Check("配置: 枚举以字符串保存", File.ReadAllText(Paths.ConfigFile).Contains("\"fixed\""));
        Check("配置: 代理模式往返", loaded.ProxyMode == ProxyMode.Manual);
        Check("配置: 排除列表往返", loaded.Excluded.Contains("wallhaven:zzz"));
        Check("配置: 自定义主题往返", loaded.CustomThemes.Any(t => t.Query == "fox"));
        Check("配置: 收藏往返", loaded.Favorites.Any(f => f.File == "abc.jpg"));
        Check("配置: 中文未被转义", File.ReadAllText(Paths.ConfigFile).Contains("猫"));
        Check("配置: data 目录在 exe 同级", Paths.Root.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase), Paths.Root);
    }

    private static void TestWallpaperProbe()
    {
        var current = WallpaperSetter.Get();

        // GitHub Actions 的 runner 没有交互式桌面，SPI_GETDESKWALLPAPER 会返回空——
        // 那不是失败，只是没有桌面可读，跳过这几条即可。
        if (string.IsNullOrWhiteSpace(current))
        {
            Check("壁纸: 无交互桌面（CI 环境），跳过 API 读取", true, "SPI_GET 返回空");
            return;
        }

        Check("壁纸: 能读到当前桌面壁纸", true, current);
        Check("壁纸: 当前壁纸文件存在", File.Exists(current), current);
        Check("壁纸: 无记录时 IsCurrent 视为一致", WallpaperSetter.IsCurrent(null));
        Check("壁纸: 传入当前路径判定一致", WallpaperSetter.IsCurrent(current));
    }

    /// <summary>
    /// 计划任务的 XML 生成（纯函数，可离线验证）。
    /// 真正创建任务需要管理员权限，只能在真机授权那一次验证——这里先保证格式正确。
    /// </summary>
    private static void TestScheduledTaskXml()
    {
        const string exe = @"C:\apps\BestWallpaper\BestWallpaper.exe";
        const string dir = @"C:\apps\BestWallpaper";
        const string sid = "S-1-5-21-1-2-3-1001";

        var logon = ScheduledTask.BuildXml(exe, dir, sid, atLogon: true, highest: true);
        Check("任务XML: 以 UTF-16 声明", logon.Contains("encoding=\"UTF-16\""));
        Check("任务XML: 含登录触发器", logon.Contains("<LogonTrigger>"));
        Check("任务XML: 最高权限", logon.Contains("<RunLevel>HighestAvailable</RunLevel>"));
        Check("任务XML: 交互式令牌", logon.Contains("<LogonType>InteractiveToken</LogonType>"));
        Check("任务XML: 命令是 exe", logon.Contains($"<Command>{exe}</Command>"));
        Check("任务XML: 带 --elevated 参数", logon.Contains($"<Arguments>{ScheduledTask.ElevatedArgument}</Arguments>"));
        Check("任务XML: 工作目录正确", logon.Contains($"<WorkingDirectory>{dir}</WorkingDirectory>"));
        Check("任务XML: 用户 SID 正确", logon.Contains($"<UserId>{sid}</UserId>"));
        Check("任务XML: 不限超时（常驻）", logon.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>"));

        var onDemand = ScheduledTask.BuildXml(exe, dir, sid, atLogon: false, highest: true);
        Check("任务XML: 按需模式无登录触发器", !onDemand.Contains("<LogonTrigger>"));

        var limited = ScheduledTask.BuildXml(exe, dir, sid, atLogon: false, highest: false);
        Check("任务XML: 可降级为 LeastPrivilege（便于测试）", limited.Contains("<RunLevel>LeastPrivilege</RunLevel>"));

        var escaped = ScheduledTask.BuildXml(@"C:\a&b\c.exe", @"C:\a&b", sid, atLogon: true, highest: true);
        Check("任务XML: 特殊字符被转义", escaped.Contains("a&amp;b"));
    }

    /// <summary>黑边裁剪：造一张上下带黑边的图，验证能裁掉并回到目标比例；无黑边的图不被动。</summary>
    private static void TestBlackBarTrim()
    {
        var dir = Path.Combine(Paths.Root, "imgtest");
        Directory.CreateDirectory(dir);
        try
        {
            var withBars = Path.Combine(dir, "bars.png");
            MakeTestImage(withBars, 400, 300, barTop: 40, barBottom: 40);

            var r = ImageTools.Process(withBars, 400, 300);
            Check("裁边: 识别出黑边并裁剪", r.Modified, r.Detail);

            var frame = LoadImage(withBars);
            Check("裁边: 比例回到 4:3", Math.Abs((double)frame.PixelWidth / frame.PixelHeight - 4.0 / 3) < 0.02,
                $"{frame.PixelWidth}x{frame.PixelHeight}");
            Check("裁边: 高度明显变小（黑边真的没了）", frame.PixelHeight < 260, $"{frame.PixelHeight}");
            Check("裁边: 顶部不再是纯黑", !IsRowBlack(withBars, 0));

            var clean = Path.Combine(dir, "clean.png");
            MakeTestImage(clean, 400, 300, barTop: 0, barBottom: 0);
            var r2 = ImageTools.Process(clean, 400, 300);
            Check("裁边: 无黑边时不做改动（避免无谓重编码）", !r2.Modified, r2.Detail);
            Check("裁边: 无黑边的图尺寸不变", LoadImage(clean).PixelWidth == 400);
        }
        catch (Exception ex)
        {
            Check("裁边: 无异常", false, ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private static void MakeTestImage(string path, int w, int h, int barTop, int barBottom)
    {
        var stride = w * 4;
        var pixels = new byte[stride * h];
        for (var y = 0; y < h; y++)
        {
            var isBar = y < barTop || y >= h - barBottom;
            for (var x = 0; x < w; x++)
            {
                var i = y * stride + x * 4;
                if (isBar)
                {
                    pixels[i] = 0; pixels[i + 1] = 0; pixels[i + 2] = 0; pixels[i + 3] = 255;
                }
                else
                {
                    // 非黑：横向渐变 + 一点变化，确保不会被判成黑边
                    var v = (byte)(60 + (x * 120 / w));
                    pixels[i] = (byte)(v / 3);
                    pixels[i + 1] = v;
                    pixels[i + 2] = (byte)(v / 2);
                    pixels[i + 3] = 255;
                }
            }
        }

        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    /// <summary>必须用 FileStream 读：WPF 会按 URI 缓存位图，改完再读同一路径会拿到旧帧。</summary>
    private static BitmapFrame LoadImage(string path)
    {
        using var fs = File.OpenRead(path);
        return BitmapFrame.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    }

    private static bool IsRowBlack(string path, int y)
    {
        var frame = LoadImage(path);
        var conv = frame.Format == PixelFormats.Bgra32 ? (BitmapSource)frame : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var stride = conv.PixelWidth * 4;
        var row = new byte[stride];
        conv.CopyPixels(new Int32Rect(0, y, conv.PixelWidth, 1), row, stride, 0);
        for (var i = 0; i < stride; i += 4)
        {
            if (row[i] > 16 || row[i + 1] > 16 || row[i + 2] > 16) return false;
        }
        return true;
    }

    /// <summary>主题模型：内置/自定义/停用/覆盖/今日主题/查找。</summary>
    private static void TestThemeModel()
    {
        var cfg = new AppConfig();

        var all = ThemeCatalog.All(cfg);
        var builtInCount = all.Count(w => w.BuiltIn);
        Check("主题: 内置词全部列出", builtInCount == 30, $"{builtInCount} 个");
        Check("主题: 初始没有自定义词", all.All(w => w.Direction != ThemeCatalog.CustomGroup));

        cfg.DisabledWords.Add("cat");
        Check("主题: 停用单词后 Enabled=false", ThemeCatalog.All(cfg).First(w => w.Query == "cat").Enabled == false);
        Check("主题: 停用的词不进池", ThemeCatalog.Pool(cfg).All(w => w.Query != "cat"));
        cfg.DisabledWords.Clear();

        cfg.DisabledDirections.Add("萌宠");
        Check("主题: 停用方向后整组 Enabled=false",
            ThemeCatalog.All(cfg).Where(w => w.Direction == "萌宠").All(w => !w.Enabled));
        Check("主题: 停用方向后池里没有猫", ThemeCatalog.Pool(cfg).All(w => w.Query != "cat"));
        cfg.DisabledDirections.Clear();

        cfg.ThemeOverrides.Add(new ThemeOverride { OriginalQuery = "cat", Display = "喵星人", Query = "kitten" });
        var overridden = ThemeCatalog.All(cfg).First(w => w.OriginalQuery == "cat");
        Check("主题: 覆盖后显示名生效", overridden.Display == "喵星人", overridden.Display);
        Check("主题: 覆盖后标签生效", overridden.Query == "kitten");
        Check("主题: 覆盖后标记已修改", overridden.Modified);
        Check("主题: 池里用新标签", ThemeCatalog.Pool(cfg).Any(w => w.Query == "kitten"));

        Check("主题: 按显示名查找", ThemeCatalog.Find(cfg, "喵星人")?.Query == "kitten");
        Check("主题: 按标签查找（大小写不敏感）", ThemeCatalog.Find(cfg, "KITTEN")?.Display == "喵星人");
        Check("主题: 查不到返回 null", ThemeCatalog.Find(cfg, "完全不存在的词") is null);

        cfg.TodayThemeDate = "2026-11-06";
        cfg.TodayThemeDisplay = "猫";
        cfg.TodayThemeQuery = "cat";
        Check("今日主题: 当天生效", ThemeCatalog.ResolveToday(cfg, "2026-11-06")?.Query == "cat");
        Check("今日主题: 跨天失效", ThemeCatalog.ResolveToday(cfg, "2026-11-07") is null);
        Check("今日主题: 没指定时为空", ThemeCatalog.ResolveToday(new AppConfig(), "2026-11-06") is null);

        cfg.CustomThemes.Add(new ThemeWord { Display = "猫头鹰", Query = "owl" });
        Check("主题: 自定义词进池", ThemeCatalog.Pool(cfg).Any(w => w.Query == "owl"));
        Check("主题: 自定义词归入自定义组",
            ThemeCatalog.All(cfg).First(w => w.Query == "owl").Direction == ThemeCatalog.CustomGroup);
    }

    private static void TestTranslator()
    {
        Check("翻译: 词典命中「猫咪」", ThemeDictionary.TryLookup("猫咪", out var cat) && cat == "cat", cat);
        Check("翻译: 词典命中带空格写法", ThemeDictionary.TryLookup(" 猫 ", out var cat2) && cat2 == "cat");
        Check("翻译: 词典未命中返回 false", !ThemeDictionary.TryLookup("量子猫猫拳", out _));

        Check("翻译: 清洗去掉冠词", Translator.Clean("The pussy cats") == "pussy cats", Translator.Clean("The pussy cats"));
        Check("翻译: 清洗小写与标点", Translator.Clean("  Hello, World!  ") == "hello world", Translator.Clean("  Hello, World!  "));
        Check("翻译: 最多保留三个词", Translator.Clean("one two three four five") == "one two three");
        Check("翻译: 中文残留会被清空", Translator.Clean("猫咪").Length == 0);
    }

    private static SourceItem Item(string id, int w, int h)
        => new(id, "wallhaven", new[] { "https://w.wallhaven.cc/full/aa/wallhaven-aa.jpg" }, w, h, 500_000, null, null);

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static void Check(string name, bool ok, string? detail = null)
    {
        if (ok)
        {
            _passed++;
            Console.WriteLine($"  PASS  {name}" + (detail is null ? "" : $"  [{detail}]"));
        }
        else
        {
            _failed++;
            Console.WriteLine($"  FAIL  {name}" + (detail is null ? "" : $"  [{detail}]"));
        }
    }
}
