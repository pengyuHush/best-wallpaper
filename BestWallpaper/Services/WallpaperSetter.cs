using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace BestWallpaper.Services;

/// <summary>设置桌面壁纸：SPI_SETDESKWALLPAPER + Fill 填充模式，作用于所有显示器。</summary>
public static class WallpaperSetter
{
    private const uint SPI_SETDESKWALLPAPER = 0x0014;
    private const uint SPI_GETDESKWALLPAPER = 0x0073;
    private const uint SPIF_UPDATEINIFILE = 0x01;
    private const uint SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfoW(uint uiAction, uint uiParam, string pvParam, uint fWinIni);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfoW(uint uiAction, uint uiParam, StringBuilder pvParam, uint fWinIni);

    private static bool _styleWarned;
    private static bool _setWarned;
    private static bool _temporaryModeLogged;

    /// <summary>最近一次失败的细节，给人看。</summary>
    public static string LastFailureDetail { get; private set; } = "未知";

    public static bool Set(string imagePath)
    {
        try
        {
            if (!File.Exists(imagePath))
            {
                Log.Warn($"[壁纸] 文件不存在: {imagePath}");
                return false;
            }

            SetFillStyle();

            // 正常路径：写入用户配置（换壁纸后重启仍保留）。
            if (SystemParametersInfoW(SPI_SETDESKWALLPAPER, 0, imagePath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE))
                return true;

            var err = Marshal.GetLastWin32Error();

            // 回退路径：不带 SPIF_UPDATEINIFILE，只对当前会话生效、不碰注册表。
            // 适用于 HKCU\Control Panel\Desktop 被提权进程接管（owner=SYSTEM、Medium 进程只读）的机器：
            // explorer 以管理员身份运行时就会出现这种情况，此时带 UPDATEINIFILE 必然 ERROR_ACCESS_DENIED。
            if (SystemParametersInfoW(SPI_SETDESKWALLPAPER, 0, imagePath, 0))
            {
                if (!_temporaryModeLogged)
                {
                    _temporaryModeLogged = true;
                    Log.Warn($"[壁纸] 写注册表被拒(win32={err})，改用会话级设置成功。" +
                             "壁纸重启后不会自动保留，但本程序每天/每次启动都会重新设置。");
                }
                LastFailureDetail = $"持久模式被拒(win32={err})，已退回会话模式";
                return true;
            }

            var err2 = Marshal.GetLastWin32Error();
            LastFailureDetail = $"持久模式 win32={err}，会话模式 win32={err2}";
            if (!_setWarned)
            {
                _setWarned = true;
                Log.Warn($"[壁纸] SPI 设置失败：持久模式 win32={err}，会话模式 win32={err2}（后续同类失败不再重复记录）");
            }
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("[壁纸] 设置异常", ex);
            return false;
        }
    }

    public static string Get()
    {
        try
        {
            var sb = new StringBuilder(1024);
            return SystemParametersInfoW(SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0) ? sb.ToString() : "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>当前壁纸是否就是程序最后设置的那张（用于检测外部改动）。</summary>
    public static bool IsCurrent(string? expectedPath)
    {
        if (string.IsNullOrEmpty(expectedPath)) return true;
        var current = Get();
        if (string.IsNullOrEmpty(current)) return true;

        try
        {
            var a = Path.GetFullPath(current);
            var b = Path.GetFullPath(expectedPath);
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(current, expectedPath, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void SetFillStyle()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true);
            if (key is null) return;
            key.SetValue("WallpaperStyle", "10", RegistryValueKind.String); // 10 = Fill
            key.SetValue("TileWallpaper", "0", RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            if (_styleWarned) return;
            _styleWarned = true;
            Log.Warn("[壁纸] 写入填充模式失败（不影响设置壁纸）: " + ex.Message);
        }
    }
}
