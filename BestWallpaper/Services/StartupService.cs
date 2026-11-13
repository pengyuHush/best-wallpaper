using Microsoft.Win32;

namespace BestWallpaper.Services;

/// <summary>开机自启：HKCU 的 Run 键，无需管理员权限、可在设置里随时开关。</summary>
public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BestWallpaper";
    public const string AutostartArgument = "--autostart";

    public static string ExePath
    {
        get
        {
            var path = Environment.ProcessPath ?? "";
            return path;
        }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch (Exception ex)
        {
            Log.Warn("[自启] 读取失败: " + ex.Message);
            return false;
        }
    }

    public static bool Enable()
    {
        try
        {
            var exe = ExePath;
            if (string.IsNullOrEmpty(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                Log.Warn($"[自启] 当前进程不是 exe（{exe}），跳过");
                return false;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null) return false;

            key.SetValue(ValueName, $"\"{exe}\" {AutostartArgument}", RegistryValueKind.String);
            Log.Info($"[自启] 已写入 Run 键: {exe}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("[自启] 写入失败", ex);
            return false;
        }
    }

    public static bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null) return true;
            if (key.GetValue(ValueName) is null) return true;
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            Log.Info("[自启] 已删除 Run 键");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("[自启] 删除失败", ex);
            return false;
        }
    }

    /// <summary>让注册表与配置一致（配置说开就写、说关就删）。</summary>
    public static void Sync(bool enabled)
    {
        if (enabled && !IsEnabled()) Enable();
        else if (!enabled && IsEnabled()) Disable();
    }
}
