using System.IO;
using System.Text;

namespace BestWallpaper.Services;

/// <summary>单文件滚动日志：按天分文件，保留 7 天，单文件超过 10MB 就换名。</summary>
public static class Log
{
    private const long MaxBytes = 10L * 1024 * 1024;
    private const int KeepDays = 7;
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);
    public static void Error(string message, Exception ex) => Write("ERROR", $"{message} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Paths.LogsDir);
                var file = Path.Combine(Paths.LogsDir, $"app-{DateTime.Now:yyyyMMdd}.log");

                var info = new FileInfo(file);
                if (info.Exists && info.Length > MaxBytes)
                {
                    var rotated = Path.Combine(Paths.LogsDir, $"app-{DateTime.Now:yyyyMMdd}-{DateTime.Now:HHmmss}.log");
                    try { File.Move(file, rotated); } catch { /* 忽略：换名失败也继续写 */ }
                }

                var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(file, line, Encoding.UTF8);
                PruneOld();
            }
        }
        catch
        {
            // 日志本身失败不能影响主流程
        }
    }

    private static void PruneOld()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-KeepDays);
            foreach (var f in Directory.EnumerateFiles(Paths.LogsDir, "app-*.log"))
            {
                if (File.GetLastWriteTime(f) < cutoff)
                {
                    try { File.Delete(f); } catch { }
                }
            }
        }
        catch { }
    }

    public static string CurrentFile => Path.Combine(Paths.LogsDir, $"app-{DateTime.Now:yyyyMMdd}.log");
}
