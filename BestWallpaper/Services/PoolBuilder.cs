using System.IO;
using System.Windows.Media.Imaging;
using BestWallpaper.Models;

namespace BestWallpaper.Services;

/// <summary>
/// 拼装当日壁纸池。降级顺序（兜底链）：wallhaven 高分辨率 → wallhaven 1080p → Bing 每日图 → 本地历史库。
/// 一条都没拿到时返回空池，由调用方决定“保持原壁纸 + 重试”。
/// </summary>
public sealed class PoolBuilder
{
    public const int Target = 10;

    private readonly WallhavenSource _wallhaven = new();
    private readonly BingSource _bing = new();

    public sealed class BuildResult
    {
        public DailyPool Pool { get; init; } = new();
        public List<string> Notes { get; } = new();
        public string FinalStage { get; set; } = "none";
    }

    public async Task<BuildResult> BuildAsync(AppConfig cfg, ThemeWord theme, string date, IProgress<string>? progress, CancellationToken ct)
    {
        var result = new BuildResult
        {
            Pool = new DailyPool
            {
                Date = date,
                ThemeDisplay = theme.Display,
                ThemeQuery = theme.Query,
                BuiltAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            },
        };

        var poolDir = Paths.PoolDir(date);
        Directory.CreateDirectory(poolDir);

        var recent = new HashSet<string>(cfg.RecentIds.Select(StripDate), StringComparer.OrdinalIgnoreCase);
        using var http = HttpFactory.Create(cfg);

        // 第一层：wallhaven 高分辨率（≥2560×1440）
        var items = await TryWallhavenAsync(http, theme.Query, result, recent, cfg, highRes: true, poolDir, progress, ct);

        // 第二层：放宽到 1920×1080
        if (items.Count < Target)
        {
            progress?.Report($"高分辨率只拿到 {items.Count} 张，放宽到 1920×1080 再试…");
            var more = await TryWallhavenAsync(http, theme.Query, result, recent, cfg, highRes: false, poolDir, progress, ct,
                skipIds: new HashSet<string>(items.Select(i => i.Id), StringComparer.OrdinalIgnoreCase));
            items.AddRange(more);
        }

        // 第三层：Bing 最近 8 天每日图
        if (items.Count == 0)
        {
            progress?.Report("wallhaven 全部失败，改用 Bing 每日图兜底…");
            var bing = await TryBingAsync(http, result, recent, cfg, poolDir, progress, ct);
            items.AddRange(bing);
        }

        // 第四层：本地历史库
        if (items.Count == 0)
        {
            progress?.Report("Bing 也失败，从本地历史库补…");
            var history = FromHistory(cfg, date, Target);
            items.AddRange(history);
        }

        result.Pool.Items = items;
        return result;
    }

    private async Task<List<WallpaperItem>> TryWallhavenAsync(
        HttpClient http,
        string query,
        BuildResult result,
        HashSet<string> recent,
        AppConfig cfg,
        bool highRes,
        string poolDir,
        IProgress<string>? progress,
        CancellationToken ct,
        HashSet<string>? skipIds = null)
    {
        var minW = highRes ? 2560 : 1920;
        var minH = highRes ? 1440 : 1080;
        var stage = highRes ? "wallhaven-高清" : "wallhaven-1080p";

        try
        {
            progress?.Report(highRes ? $"正在搜索「{result.Pool.ThemeDisplay}」的高分辨率壁纸…" : "正在放宽条件再搜一次…");
            var found = await _wallhaven.SearchAsync(http, query, 24, highRes, ct);
            var candidates = found
                .Where(s => skipIds is null || !skipIds.Contains(s.Id))
                .Where(s => Accept(s, cfg, recent, minW, minH))
                .ToList();

            if (candidates.Count == 0)
            {
                result.Notes.Add($"{stage}: 没有通过过滤的候选");
                return new List<WallpaperItem>();
            }

            var downloaded = await DownloadMany(http, candidates, 0, Target, poolDir, progress, ct);
            if (downloaded.Count > 0) result.FinalStage = stage;
            result.Notes.Add($"{stage}: {downloaded.Count} 张");
            return downloaded;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error($"[{stage}] 失败", ex);
            result.Notes.Add($"{stage} 失败: {ex.Message}");
            return new List<WallpaperItem>();
        }
    }

    private async Task<List<WallpaperItem>> TryBingAsync(
        HttpClient http, BuildResult result, HashSet<string> recent, AppConfig cfg, string poolDir, IProgress<string>? progress, CancellationToken ct)
    {
        try
        {
            var found = await _bing.SearchAsync(http, "", 8, false, ct);
            var candidates = found.Where(s => Accept(s, cfg, recent, 1024, 0)).ToList();
            if (candidates.Count == 0) return new List<WallpaperItem>();

            var downloaded = await DownloadMany(http, candidates, 0, Target, poolDir, progress, ct);
            if (downloaded.Count > 0) result.FinalStage = "bing";
            result.Notes.Add($"bing: {downloaded.Count} 张");
            return downloaded;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error("[bing] 失败", ex);
            result.Notes.Add($"bing 失败: {ex.Message}");
            return new List<WallpaperItem>();
        }
    }

    /// <summary>按 4 张一波并发下载，凑够目标数量就停。</summary>
    private async Task<List<WallpaperItem>> DownloadMany(
        HttpClient http, IReadOnlyList<SourceItem> candidates, int already, int target,
        string poolDir, IProgress<string>? progress, CancellationToken ct)
    {
        var got = new List<WallpaperItem>();
        var queue = new Queue<SourceItem>(candidates);

        while (already + got.Count < target && queue.Count > 0)
        {
            // 批次大小不超过还缺的张数，避免多下用不上的孤儿文件
            var batchSize = Math.Min(4, target - (already + got.Count));
            var batch = new List<SourceItem>();
            while (batch.Count < batchSize && queue.Count > 0) batch.Add(queue.Dequeue());

            var tasks = batch.Select(c => DownloadOneAsync(http, c, poolDir, ct)).ToArray();
            var done = await Task.WhenAll(tasks);
            foreach (var item in done)
            {
                if (item is null) continue;
                if (got.Count + already >= target) break;
                got.Add(item);
            }

            progress?.Report($"已下载 {already + got.Count}/{target} 张…");
        }

        return got;
    }

    private static async Task<WallpaperItem?> DownloadOneAsync(HttpClient http, SourceItem src, string poolDir, CancellationToken ct)
    {
        foreach (var url in src.RemoteUrls)
        {
            try
            {
                using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    Log.Warn($"[下载] {(int)resp.StatusCode} {Short(url)}");
                    continue;
                }

                var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length < 20_000)
                {
                    Log.Warn($"[下载] 内容过小 {bytes.Length}B，跳过 {Short(url)}");
                    continue;
                }

                var (w, h) = ProbeSize(bytes);
                if (w < 1600 || h < 900)
                {
                    Log.Warn($"[下载] 实际尺寸 {w}x{h} 太小，跳过 {Short(url)}");
                    continue;
                }

                var ext = GuessExtension(url, resp.Content.Headers.ContentType?.MediaType);
                // 用 id 命名（而不是序号）：重复下载会覆盖同一文件，不会留下孤儿副本
                var file = SafeFileName(src.Id) + ext;
                var full = Path.Combine(poolDir, file);
                await File.WriteAllBytesAsync(full, bytes, ct);

                // 裁掉源图自带的黑边（电影截图常见），并裁到屏幕比例
                var (screenW, screenH) = ImageTools.TargetScreenSize();
                var trim = ImageTools.Process(full, screenW, screenH);
                if (trim.Modified)
                {
                    bytes = await File.ReadAllBytesAsync(full, ct);
                    (w, h) = ProbeSize(bytes);
                }

                Log.Info($"[下载] OK {file} {w}x{h} {bytes.Length / 1024}KB");
                return new WallpaperItem
                {
                    Id = src.Id,
                    Source = src.Source,
                    File = file,
                    Width = w,
                    Height = h,
                    Bytes = bytes.Length,
                    PageUrl = src.PageUrl,
                    Author = src.Author,
                    RemoteUrl = url,
                };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warn($"[下载] 异常 {Short(url)} :: {ex.GetType().Name}: {ex.Message}");
            }
        }
        return null;
    }

    /// <summary>读文件头拿真实像素，别看接口声明的分辨率。</summary>
    internal static (int W, int H) ProbeSize(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            var frame = BitmapFrame.Create(ms, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch
        {
            return (0, 0);
        }
    }

    internal static bool Accept(SourceItem s, AppConfig cfg, HashSet<string> recent, int minW, int minH)
    {
        if (cfg.Excluded.Any(e => string.Equals(e, s.Id, StringComparison.OrdinalIgnoreCase))) return false;
        if (recent.Contains(s.Id)) return false;
        if (s.Width < minW || s.Height < minH) return false;
        var ratio = (double)s.Width / s.Height;
        return ratio is >= 1.45 and <= 2.25;
    }

    private static List<WallpaperItem> FromHistory(AppConfig cfg, string today, int want)
    {
        var result = new List<WallpaperItem>();
        try
        {
            if (!Directory.Exists(Paths.PoolsDir)) return result;
            var poolDir = Paths.PoolDir(today);
            Directory.CreateDirectory(poolDir);

            var candidates = new List<(string Path, WallpaperItem Item, string Date)>();
            foreach (var dir in Directory.EnumerateDirectories(Paths.PoolsDir))
            {
                var date = Path.GetFileName(dir);
                if (string.Equals(date, today, StringComparison.Ordinal)) continue;
                var meta = ConfigStore.LoadPool(date);
                if (meta is null) continue;
                foreach (var item in meta.Items)
                {
                    if (cfg.Excluded.Any(e => string.Equals(e, item.Id, StringComparison.OrdinalIgnoreCase))) continue;
                    var full = Path.Combine(dir, item.File);
                    if (File.Exists(full)) candidates.Add((full, item, date));
                }
            }

            if (candidates.Count == 0) return result;
            var rng = new Random();
            foreach (var c in candidates.OrderBy(_ => rng.Next()).Take(want))
            {
                var file = $"h-{SafeFileName(c.Item.Id)}{Path.GetExtension(c.Path)}";
                var dest = Path.Combine(poolDir, file);
                try
                {
                    File.Copy(c.Path, dest, overwrite: true);
                    result.Add(new WallpaperItem
                    {
                        Id = c.Item.Id,
                        Source = "history",
                        File = file,
                        Width = c.Item.Width,
                        Height = c.Item.Height,
                        Bytes = new FileInfo(dest).Length,
                        PageUrl = c.Item.PageUrl,
                        Author = c.Item.Author,
                    });
                }
                catch (Exception ex)
                {
                    Log.Warn($"[历史库] 复制失败 {c.Path} :: {ex.Message}");
                }
            }

            Log.Info($"[历史库] 补入 {result.Count} 张");
        }
        catch (Exception ex)
        {
            Log.Error("[历史库] 失败", ex);
        }
        return result;
    }

    public static string StripDate(string recentEntry)
    {
        var idx = recentEntry.IndexOf('|');
        return idx >= 0 ? recentEntry[(idx + 1)..] : recentEntry;
    }

    public static string MakeRecentEntry(string date, string id) => date + "|" + id;

    /// <summary>只保留最近 keepDays 天的去重记录（条目格式 yyyy-MM-dd|id）。畸形条目直接丢掉。</summary>
    internal static List<string> PruneRecent(IEnumerable<string> entries, DateTime today, int keepDays)
    {
        var cutoff = today.Date.AddDays(-keepDays);
        var kept = new List<string>();
        foreach (var e in entries)
        {
            if (e.Length <= 11) continue;
            if (!DateTime.TryParseExact(e[..10], "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var date)) continue;
            if (date.Date < cutoff) continue;
            kept.Add(e);
        }
        return kept;
    }

    private static string SafeFileName(string id)
    {
        var s = id.Replace(':', '-').Replace('/', '-').Replace('\\', '-');
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '-');
        return s.Length > 60 ? s[..60] : s;
    }

    private static string GuessExtension(string url, string? mediaType)
    {
        if (mediaType?.Contains("png", StringComparison.OrdinalIgnoreCase) == true) return ".png";
        if (url.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return ".png";
        return ".jpg";
    }

    private static string Short(string url) => url.Length <= 80 ? url : url[..77] + "...";
}
