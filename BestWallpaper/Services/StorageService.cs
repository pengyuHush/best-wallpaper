using System.IO;
using BestWallpaper.Models;

namespace BestWallpaper.Services;

/// <summary>磁盘生命周期：保留最近 N 天池子，总占用超过上限时从最旧的开始删；收藏永不删。</summary>
public static class StorageService
{
    public sealed record Usage(long PoolBytes, int PoolDays, long FavoritesBytes, int FavoriteCount)
    {
        public long TotalBytes => PoolBytes + FavoritesBytes;
        public string Text => $"池子 {Mb(PoolBytes)}（{PoolDays} 天） · 收藏 {FavoriteCount} 张 {Mb(FavoritesBytes)} · 合计 {Mb(TotalBytes)}";
        private static string Mb(long bytes) => bytes >= 1024L * 1024 * 1024
            ? $"{bytes / 1024.0 / 1024 / 1024:F2} GB"
            : $"{bytes / 1024.0 / 1024:F1} MB";
    }

    public static Usage Measure()
    {
        long pool = 0;
        var days = 0;
        try
        {
            if (Directory.Exists(Paths.PoolsDir))
            {
                foreach (var dir in Directory.EnumerateDirectories(Paths.PoolsDir))
                {
                    days++;
                    pool += DirSize(dir);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("[存储] 统计池子失败: " + ex.Message);
        }

        long fav = 0;
        var favCount = 0;
        try
        {
            if (Directory.Exists(Paths.FavoritesDir))
            {
                foreach (var f in Directory.EnumerateFiles(Paths.FavoritesDir))
                {
                    favCount++;
                    fav += new FileInfo(f).Length;
                }
            }
        }
        catch { }

        return new Usage(pool, days, fav, favCount);
    }

    public static void Cleanup(AppConfig cfg)
    {
        try
        {
            if (!Directory.Exists(Paths.PoolsDir)) return;

            var dirs = Directory.EnumerateDirectories(Paths.PoolsDir)
                .Select(d => new { Path = d, Name = Path.GetFileName(d), Time = ParseDate(Path.GetFileName(d)) })
                .Where(d => d.Time.HasValue)
                .OrderBy(d => d.Time!.Value)
                .ToList();

            var cutoff = DateTime.Today.AddDays(-cfg.MaxPoolDays);
            foreach (var d in dirs.Where(d => d.Time!.Value < cutoff))
            {
                DeleteDir(d.Path);
            }

            // 超出体积上限：仍从最旧的删，但不删今天的池子
            var remaining = Directory.EnumerateDirectories(Paths.PoolsDir)
                .Select(d => new { Path = d, Time = ParseDate(Path.GetFileName(d)) })
                .Where(d => d.Time.HasValue)
                .OrderBy(d => d.Time!.Value)
                .ToList();

            var total = remaining.Sum(d => DirSize(d.Path));
            foreach (var d in remaining)
            {
                if (total <= cfg.MaxCacheBytes) break;
                if (d.Time!.Value.Date == DateTime.Today) continue;
                total -= DirSize(d.Path);
                DeleteDir(d.Path);
            }

            Log.Info($"[存储] 清理完成，当前 {Measure().Text}");
        }
        catch (Exception ex)
        {
            Log.Error("[存储] 清理失败", ex);
        }
    }

    public static void DeleteDir(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
            Log.Info($"[存储] 已删除旧池子 {Path.GetFileName(dir)}");
        }
        catch (Exception ex)
        {
            Log.Warn($"[存储] 删除失败 {dir} :: {ex.Message}");
        }
    }

    private static DateTime? ParseDate(string name)
        => DateTime.TryParseExact(name, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var dt) ? dt : null;

    private static long DirSize(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f =>
            {
                try { return new FileInfo(f).Length; } catch { return 0L; }
            });
        }
        catch
        {
            return 0;
        }
    }
}
