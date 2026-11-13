using System.IO;

namespace BestWallpaper.Services;

/// <summary>绿色便携：所有数据都在 exe 同级的 data\ 下，整个文件夹可拷走。</summary>
public static class Paths
{
    public static string Root => Path.Combine(AppContext.BaseDirectory, "data");

    public static string ConfigFile => Path.Combine(Root, "config.json");
    public static string PoolsDir => Path.Combine(Root, "pool");
    public static string FavoritesDir => Path.Combine(Root, "favorites");
    public static string LogsDir => Path.Combine(Root, "logs");

    public static string PoolDir(string date) => Path.Combine(PoolsDir, date);
    public static string MetaFile(string date) => Path.Combine(PoolDir(date), "meta.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(PoolsDir);
        Directory.CreateDirectory(FavoritesDir);
        Directory.CreateDirectory(LogsDir);
    }

    /// <summary>把可能位于 data\ 下的路径转成相对 data\ 的写法，便于日志与配置可读。</summary>
    public static string Pretty(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        try
        {
            var full = Path.GetFullPath(path);
            if (full.StartsWith(Root, StringComparison.OrdinalIgnoreCase))
                return "data\\" + full.Substring(Root.Length).TrimStart('\\');
            return full;
        }
        catch
        {
            return path;
        }
    }
}
