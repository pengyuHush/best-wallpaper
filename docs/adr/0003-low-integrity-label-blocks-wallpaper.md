# 壁纸改不动：工作区文件带 Low 完整性标签，导致程序以低完整性运行

2026-11-03 双击运行后壁纸不变、托盘无窗口，看起来像"程序没启动"。真正的根因是：**workspace 里的文件被打了 Low 强制完整性标签**（`S-1-16-4096`），而 Windows 启动带 Low 标签的可执行文件时，进程也以 Low 完整性运行。Low 进程写不了 HKCU、改不了桌面壁纸：

```
SPI_SETDESKWALLPAPER(持久模式) : 失败 错误=5      (ERROR_ACCESS_DENIED)
SPI_SETDESKWALLPAPER(会话模式) : 失败 错误=5
HKCU\Control Panel\Desktop 写入 : 拒绝访问
IDesktopWallpaper 激活          : E_ACCESSDENIED
```

**为什么难查**：DACL 里当前用户是 `FullControl`，键上也没有完整性标签，组策略也没锁壁纸。加上排查时把 RID 记错了一级（4096 当成 Medium、8192 当成 High），把问题误判成"整个会话提权、UIPI 挡住 Medium 进程"。真正的分界线是 **Low vs Medium**，而不是 Medium vs High。

复现与判定：

```powershell
# 文件标签：dist\BestWallpaper.exe -> S-1-16-4096 (Low)
.\scripts\set-integrity.ps1 -Path dist\BestWallpaper.exe     # 读取（并顺手打印修复结果）
.\scripts\set-integrity.ps1 -Path dist -Recurse              # 修复：改成 S-1-16-8192 (Medium)
```

修复后同一个 exe 的 `--diagnose` 直接变成：

```
本进程 : Medium(8192) 普通用户
SPI_SET 持久模式(0x03) : 成功
HKCU\Control Panel\Desktop 写入 : 成功
```

**决定**：
1. `scripts\set-integrity.ps1` 负责把文件/目录标签重置为 Medium，`build.ps1` 在发布后自动调用它（否则每次 publish 出来的 exe 又会继承 Low 标签）。
2. 程序内置 `--diagnose`，把权限级别（含 Low 的醒目标注）、两种 SPI 调用结果、注册表可写性、COM 激活结果写进 `data\diagnostics.txt`。
3. 壁纸真的设置不了时仍然弹一次说明窗口（而不是静默待在托盘），并提供「以管理员身份重启」；另有一套"计划任务静默提权"作为兜底，但正常环境用不到。

**Consequences**: 这个坑只影响由带 Low 标签的工具链产出的文件。换一台机器、或把 dist 拷到普通目录，就不会复现；但如果重新在被打标签的目录里构建，必须再跑一次标签修复（已自动化）。
