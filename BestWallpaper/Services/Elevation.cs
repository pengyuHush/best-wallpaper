using System.Runtime.InteropServices;

namespace BestWallpaper.Services;

/// <summary>判断当前进程是不是以管理员身份（完整令牌）在跑。UIPI 那条坑就靠它识别。</summary>
public static class Elevation
{
    private const int TokenElevation = 20;

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int cls, ref int value, int length, out int returned);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    public static bool IsElevated()
    {
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), 0x0008, out var token)) return false;
            try
            {
                var value = 0;
                return GetTokenInformation(token, TokenElevation, ref value, sizeof(int), out _) && value == 1;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        catch
        {
            return false;
        }
    }
}
