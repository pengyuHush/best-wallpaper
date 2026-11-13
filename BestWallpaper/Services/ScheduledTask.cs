using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace BestWallpaper.Services;

/// <summary>
/// 用计划任务做"静默提权启动"。
///
/// 为什么需要：本机登录会话是提权的（explorer 为 High），双击启动的程序只有 Medium，
/// UIPI 会挡住它改壁纸。每次用 runas 提权又会弹 UAC + SmartScreen。
/// 做法：以管理员身份登记一次计划任务（RunLevel=HighestAvailable），之后普通权限双击 →
/// 检测到任务 → 直接触发它，由它以最高权限拉起程序：无 UAC、无弹窗。
///
/// 实现用「XML 文件 + schtasks /create /xml」而不是拼命令行参数，也不依赖 C# 的 COM 动态绑定。
/// </summary>
public static class ScheduledTask
{
    public const string TaskName = "BestWallpaper";
    public const string ElevatedArgument = "--elevated";

    private sealed record CmdResult(int ExitCode, string Output);

    /// <summary>
    /// 不用 RedirectStandardOutput：某些受限环境禁止用管道捕获子进程输出。
    /// 改为让 cmd 把输出重定向到文件再读，稳。
    /// </summary>
    private static CmdResult Exec(string arguments)
    {
        var outFile = Path.Combine(Paths.Root, "schtasks.out");
        try
        {
            Directory.CreateDirectory(Paths.Root);
            var psi = new ProcessStartInfo("cmd.exe", $"/c schtasks.exe {arguments} > \"{outFile}\" 2>&1")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return new CmdResult(-1, "无法启动 schtasks.exe");

            if (!p.WaitForExit(20000))
            {
                try { p.Kill(); } catch { }
                return new CmdResult(-2, "schtasks 超时");
            }

            var output = File.Exists(outFile) ? File.ReadAllText(outFile) : "";
            try { File.Delete(outFile); } catch { }
            return new CmdResult(p.ExitCode, output);
        }
        catch (Exception ex)
        {
            return new CmdResult(-3, ex.GetType().Name + ": " + ex.Message);
        }
    }

    public static bool Exists(string name = TaskName)
        => Exec($"/query /tn \"{name}\"").ExitCode == 0;

    /// <summary>创建或更新任务。highest=true 需要调用者已经是管理员。</summary>
    public static bool Ensure(string exePath, bool atLogon, bool highest = true, string name = TaskName)
    {
        try
        {
            if (string.IsNullOrEmpty(exePath) || !exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                Log.Warn($"[任务] 路径不像 exe，跳过: {exePath}");
                return false;
            }

            var dir = Path.GetDirectoryName(exePath) ?? "";
            var sid = WindowsIdentity.GetCurrent().User?.Value ?? "";
            var xml = BuildXml(exePath, dir, sid, atLogon, highest);

            var tmp = Path.Combine(Paths.Root, "task.xml");
            Directory.CreateDirectory(Paths.Root);
            File.WriteAllText(tmp, xml, new UnicodeEncoding(false, true));

            var r = Exec($"/create /tn \"{name}\" /xml \"{tmp}\" /f");

            if (r.ExitCode == 0)
            {
                try { File.Delete(tmp); } catch { }
                Log.Info($"[任务] 已创建（{(atLogon ? "登录自启" : "仅按需")}，highest={highest}，name={name}）");
                return true;
            }

            Log.Warn($"[任务] 创建失败 exit={r.ExitCode}: {r.Output.Trim()}（XML 保留在 {tmp} 便于排查）");
            return false;
        }
        catch (Exception ex)
        {
            Log.Warn($"[任务] 创建异常: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public static bool RunNow(string name = TaskName)
    {
        var r = Exec($"/run /tn \"{name}\"");
        if (r.ExitCode == 0)
        {
            Log.Info("[任务] 已触发，等待它以最高权限启动");
            return true;
        }
        Log.Warn($"[任务] 触发失败 exit={r.ExitCode}: {r.Output.Trim()}");
        return false;
    }

    public static bool Delete(string name = TaskName)
        => Exec($"/delete /tn \"{name}\" /f").ExitCode == 0;

    /// <summary>任务里是否带"登录时触发"。</summary>
    public static bool IsAtLogon(string name = TaskName)
    {
        var r = Exec($"/query /tn \"{name}\" /xml ONE");
        return r.ExitCode == 0 && r.Output.Contains("<LogonTrigger", StringComparison.OrdinalIgnoreCase);
    }

    internal static string BuildXml(string exePath, string workingDir, string userId, bool atLogon, bool highest)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n");
        sb.Append("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n");
        sb.Append("  <RegistrationInfo>\n");
        sb.Append("    <Description>每日壁纸：每天取十张壁纸并轮播</Description>\n");
        sb.Append("  </RegistrationInfo>\n");

        if (atLogon)
        {
            sb.Append("  <Triggers>\n");
            sb.Append("    <LogonTrigger>\n");
            sb.Append("      <Enabled>true</Enabled>\n");
            sb.Append("      <Delay>PT30S</Delay>\n");
            sb.Append("    </LogonTrigger>\n");
            sb.Append("  </Triggers>\n");
        }

        sb.Append("  <Principals>\n");
        sb.Append("    <Principal id=\"Author\">\n");
        sb.Append($"      <UserId>{Escape(userId)}</UserId>\n");
        sb.Append("      <LogonType>InteractiveToken</LogonType>\n");
        sb.Append($"      <RunLevel>{(highest ? "HighestAvailable" : "LeastPrivilege")}</RunLevel>\n");
        sb.Append("    </Principal>\n");
        sb.Append("  </Principals>\n");

        sb.Append("  <Settings>\n");
        sb.Append("    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\n");
        sb.Append("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\n");
        sb.Append("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\n");
        sb.Append("    <AllowHardTerminate>true</AllowHardTerminate>\n");
        sb.Append("    <StartWhenAvailable>true</StartWhenAvailable>\n");
        sb.Append("    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\n");
        sb.Append("    <AllowStartOnDemand>true</AllowStartOnDemand>\n");
        sb.Append("    <Enabled>true</Enabled>\n");
        sb.Append("    <Hidden>false</Hidden>\n");
        sb.Append("    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\n");
        sb.Append("    <Priority>6</Priority>\n");
        sb.Append("  </Settings>\n");

        sb.Append("  <Actions Context=\"Author\">\n");
        sb.Append("    <Exec>\n");
        sb.Append($"      <Command>{Escape(exePath)}</Command>\n");
        sb.Append($"      <Arguments>{Escape(ElevatedArgument)}</Arguments>\n");
        sb.Append($"      <WorkingDirectory>{Escape(workingDir)}</WorkingDirectory>\n");
        sb.Append("    </Exec>\n");
        sb.Append("  </Actions>\n");
        sb.Append("</Task>\n");
        return sb.ToString();
    }

    private static string Escape(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
