param(
    [Parameter(Mandatory = $true)][string]$Path,
    [switch]$Recurse
)

# 把文件/目录的强制完整性标签重置为 Medium（S-1-16-4096）。
#
# 为什么需要：沙箱工具创建的文件会被打上 Low（S-1-16-4096 with NO_WRITE_UP）标签。
# Windows 启动带 Low 标签的 exe 时，进程也是 Low 完整性——Low 进程写不了 HKCU，
# 于是 SystemParametersInfoW(SPI_SETDESKWALLPAPER) 返回 win32=5、IDesktopWallpaper 返回
# E_ACCESSDENIED、注册表写入被拒。把标签改回 Medium 就一切正常。
#
# 用法：
#   .\set-integrity.ps1 -Path dist\BestWallpaper.exe
#   .\set-integrity.ps1 -Path dist -Recurse

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

public static class IntegrityLabel
{
    const int SE_FILE_OBJECT = 1;
    const int SE_REGISTRY_KEY = 4;
    const int LABEL_SECURITY_INFORMATION = 0x10;
    const byte SYSTEM_MANDATORY_LABEL_ACE_TYPE = 0x11;
    const byte OBJECT_INHERIT = 0x01;
    const byte CONTAINER_INHERIT = 0x02;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern int GetNamedSecurityInfo(string name, int type, int info,
        out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr sd);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern int SetNamedSecurityInfo(string name, int type, int info,
        IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl);

    public static string Describe(string sid)
    {
        switch (sid)
        {
            case "S-1-16-4096": return "Low";
            case "S-1-16-8192": return "Medium";
            case "S-1-16-12288": return "High";
            case "S-1-16-16384": return "System";
            default: return "其它";
        }
    }

    public static string Read(string path, int objectType)
    {
        IntPtr o, g, d, s, sd;
        int rc = GetNamedSecurityInfo(path, objectType, LABEL_SECURITY_INFORMATION, out o, out g, out d, out s, out sd);
        if (rc != 0) return "读取失败 rc=" + rc;
        if (s == IntPtr.Zero) return "无标签";

        int count = Marshal.ReadInt16(s, 4);
        IntPtr p = (IntPtr)((long)s + 8);
        var sb = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            byte type = Marshal.ReadByte(p, 0);
            int size = Marshal.ReadInt16(p, 2);
            uint mask = (uint)Marshal.ReadInt32(p, 4);
            if (size <= 0) break;
            var sid = new SecurityIdentifier((IntPtr)((long)p + 8));
            if (type == SYSTEM_MANDATORY_LABEL_ACE_TYPE)
                sb.Append(sid.Value + " (" + Describe(sid.Value) + ", mask=0x" + mask.ToString("X") + ")");
            p = (IntPtr)((long)p + size);
        }
        return sb.Length == 0 ? "无标签" : sb.ToString();
    }

    public static string SetMedium(string path, int objectType, bool inherit)
    {
        byte flags = inherit ? (byte)(OBJECT_INHERIT | CONTAINER_INHERIT) : (byte)0;
        return Set(path, objectType, "S-1-16-8192", flags);
    }

    static string Set(string path, int objectType, string sidString, byte aceFlags)
    {
        var sid = new SecurityIdentifier(sidString);
        byte[] sidBytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(sidBytes, 0);

        int aceSize = 4 + 4 + sidBytes.Length;
        int aclSize = 8 + aceSize;
        IntPtr acl = Marshal.AllocHGlobal(aclSize);
        try
        {
            Marshal.WriteByte(acl, 0, 2);       // ACL_REVISION
            Marshal.WriteByte(acl, 1, 0);
            Marshal.WriteInt16(acl, 2, (short)aclSize);
            Marshal.WriteInt16(acl, 4, 1);
            Marshal.WriteInt16(acl, 6, 0);

            IntPtr ace = (IntPtr)((long)acl + 8);
            Marshal.WriteByte(ace, 0, SYSTEM_MANDATORY_LABEL_ACE_TYPE);
            Marshal.WriteByte(ace, 1, aceFlags);
            Marshal.WriteInt16(ace, 2, (short)aceSize);
            Marshal.WriteInt32(ace, 4, 0);       // 不附加额外限制
            Marshal.Copy(sidBytes, 0, (IntPtr)((long)ace + 8), sidBytes.Length);

            int rc = SetNamedSecurityInfo(path, objectType, LABEL_SECURITY_INFORMATION,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, acl);
            return rc == 0 ? "ok" : ("err=" + rc);
        }
        finally
        {
            Marshal.FreeHGlobal(acl);
        }
    }
}
'@

function Fix-One([string]$target, [bool]$isDir) {
    $type = 1   # SE_FILE_OBJECT，文件和目录相同
    $before = [IntegrityLabel]::Read($target, $type)
    $ok = [IntegrityLabel]::SetMedium($target, $type, $isDir)
    $after = [IntegrityLabel]::Read($target, $type)
    "{0,-60} {1,-34} -> {2,-34} {3}" -f $target, $before, $after, $ok
}

$full = (Resolve-Path -LiteralPath $Path).Path
if (Test-Path -LiteralPath $full -PathType Container) {
    Fix-One $full $true
    if ($Recurse) {
        Get-ChildItem -LiteralPath $full -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {
            $isDir = $_.PSIsContainer
            try { Fix-One $_.FullName $isDir } catch { "跳过: $($_.FullName) ($($_.Exception.Message))" }
        }
    }
} else {
    Fix-One $full $false
}
