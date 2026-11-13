using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace BestWallpaper.Services;

/// <summary>
/// 环境自检：壁纸改不动时用来定位是"权限级别"、"注册表标签"还是"安全软件拦截"。
/// 结果写到 data\diagnostics.txt，同时开记事本给人看。
/// </summary>
public static class Diagnostics
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int TokenIntegrityLevel = 25;

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int cls, IntPtr buffer, int length, out int returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfoW(uint action, uint param, string value, uint winIni);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfoW(uint action, uint param, StringBuilder value, uint winIni);

    public static string IntegrityOf(int processId)
    {
        try
        {
            var handle = processId == Environment.ProcessId
                ? GetCurrentProcess()
                : OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (handle == IntPtr.Zero) return "无法打开进程(错误 " + Marshal.GetLastWin32Error() + ")";

            if (!OpenProcessToken(handle, 0x0008, out var token))
                return "无法打开令牌(错误 " + Marshal.GetLastWin32Error() + ")";

            GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out var size);
            var buffer = Marshal.AllocHGlobal(size);
            string result;
            if (GetTokenInformation(token, TokenIntegrityLevel, buffer, size, out _))
            {
                var sidPtr = Marshal.ReadIntPtr(buffer);
                var sid = new byte[12];
                Marshal.Copy(sidPtr, sid, 0, 12);
                var rid = BitConverter.ToInt32(sid, 8);
                result = rid switch
                {
                    4096 => "Low(4096) ← 低完整性，写不了注册表也改不了壁纸",
                    8192 => "Medium(8192) 普通用户",
                    12288 => "High(12288) 已提权",
                    16384 => "System(16384)",
                    20480 => "Protected(20480)",
                    _ => "RID=" + rid,
                };
            }
            else
            {
                result = "读取失败(错误 " + Marshal.GetLastWin32Error() + ")";
            }

            Marshal.FreeHGlobal(buffer);
            CloseHandle(token);
            if (processId != Environment.ProcessId) CloseHandle(handle);
            return result;
        }
        catch (Exception ex)
        {
            return "异常: " + ex.Message;
        }
    }

    public static string Collect()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== 每日壁纸 · 环境自检 ===");
        sb.AppendLine($"时间   : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"exe    : {Environment.ProcessPath}");
        sb.AppendLine($"系统   : {Environment.OSVersion.VersionString}");
        sb.AppendLine($"用户   : {Environment.UserDomainName}\\{Environment.UserName}");
        sb.AppendLine();

        sb.AppendLine("--- 权限级别 ---");
        sb.AppendLine($"本进程 : {IntegrityOf(Environment.ProcessId)}");
        foreach (var p in Process.GetProcessesByName("explorer"))
        {
            sb.AppendLine($"explorer(pid {p.Id}) : {IntegrityOf(p.Id)}");
        }
        sb.AppendLine();

        sb.AppendLine("--- 壁纸 API ---");
        var current = WallpaperSetter.Get();
        sb.AppendLine($"当前壁纸(SPI_GET) : {current}");

        var probe = FindProbeImage();
        if (probe is null)
        {
            sb.AppendLine("没有可用于测试的图片（今日池子为空）");
        }
        else
        {
            sb.AppendLine($"测试图片 : {probe}");
            var ok1 = SystemParametersInfoW(0x0014, 0, probe, 0x01 | 0x02);
            var err1 = Marshal.GetLastWin32Error();
            sb.AppendLine($"SPI_SET 持久模式(0x03) : {(ok1 ? "成功" : "失败")} 错误={err1}");

            var ok2 = SystemParametersInfoW(0x0014, 0, probe, 0);
            var err2 = Marshal.GetLastWin32Error();
            sb.AppendLine($"SPI_SET 会话模式(0x00) : {(ok2 ? "成功" : "失败")} 错误={err2}");
        }
        sb.AppendLine();

        sb.AppendLine("--- 注册表写入 ---");
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true);
            if (key is null)
            {
                sb.AppendLine("无法打开 HKCU\\Control Panel\\Desktop");
            }
            else
            {
                try
                {
                    key.GetValue("WallpaperStyle");
                    key.SetValue("WallpaperStyle", key.GetValue("WallpaperStyle") ?? "10", RegistryValueKind.String);
                    sb.AppendLine("写入 HKCU\\Control Panel\\Desktop : 成功");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"写入 HKCU\\Control Panel\\Desktop : 失败 {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"打开注册表失败: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            var acl = new System.Security.AccessControl.RegistrySecurity();
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            if (key is not null) acl = key.GetAccessControl();
            sb.AppendLine("DACL(本用户) : " + string.Join(" | ", acl.GetAccessRules(true, true, typeof(System.Security.Principal.NTAccount))
                .Cast<System.Security.AccessControl.RegistryAccessRule>()
                .Where(r => r.IdentityReference.Value.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase))
                .Select(r => $"{r.AccessControlType}:{r.RegistryRights}")));
        }
        catch (Exception ex)
        {
            sb.AppendLine("读取 DACL 失败: " + ex.Message);
        }
        sb.AppendLine();

        sb.AppendLine("--- 桌面壁纸 COM (IDesktopWallpaper) ---");
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"));
            var instance = type is null ? null : Activator.CreateInstance(type);
            sb.AppendLine(instance is null ? "CLSID 未注册" : "激活成功");
        }
        catch (Exception ex)
        {
            sb.AppendLine("激活失败: " + ex.GetType().Name + ": " + ex.Message);
        }
        sb.AppendLine();

        sb.AppendLine("--- 结论提示 ---");
        sb.AppendLine("若本进程是 Medium 而 SPI_SET 失败(错误=5)，说明壁纸被更高完整性级别的进程占着：");
        sb.AppendLine("  1) 右键 exe → 以管理员身份运行，若恢复正常即可确认；");
        sb.AppendLine("  2) 检查安全软件（本机装了火绒）的拦截日志，看是否有「修改桌面壁纸 / 注册表」被拦；");
        sb.AppendLine("  3) 检查 explorer.exe 是否以管理员身份运行（正常应为 Medium）。");
        return sb.ToString();
    }

    private static string? FindProbeImage()
    {
        try
        {
            var today = Path.Combine(Paths.PoolsDir, DateTime.Today.ToString("yyyy-MM-dd"));
            if (Directory.Exists(today))
            {
                var file = Directory.EnumerateFiles(today, "*.*")
                    .FirstOrDefault(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                                      || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
                if (file is not null) return file;
            }

            var any = Directory.Exists(Paths.PoolsDir)
                ? Directory.EnumerateDirectories(Paths.PoolsDir)
                    .OrderByDescending(d => d)
                    .SelectMany(d => Directory.EnumerateFiles(d, "*.*"))
                    .FirstOrDefault(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                : null;

            return any ?? @"C:\Windows\Web\Wallpaper\ThemeB\img26.jpg";
        }
        catch
        {
            return null;
        }
    }

    public static string WriteReport()
    {
        var text = Collect();
        try
        {
            Directory.CreateDirectory(Paths.Root);
            var file = Path.Combine(Paths.Root, "diagnostics.txt");
            File.WriteAllText(file, text, new UTF8Encoding(false));
            return file;
        }
        catch (Exception ex)
        {
            Log.Error("[自检] 写报告失败", ex);
            return "";
        }
    }
}
