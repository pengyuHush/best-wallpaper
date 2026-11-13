# 排查树：壁纸不换 / 双击没反应 / 拉不到图

按顺序走到第一条能解释现象的地方就停。每步都给出**命令**与**判据**，不要凭印象跳过。

## 0. 先用一条命令收全信息

```powershell
cd <仓库根目录>
Get-Process BestWallpaper -ErrorAction SilentlyContinue | Select-Object Id, StartTime
Get-ChildItem dist\data\logs\*.log | Sort-Object LastWriteTime -Descending | Select-Object -First 1 |
  ForEach-Object { Get-Content $_.FullName -Encoding UTF8 -Tail 20 }
dist\BestWallpaper.exe --diagnose      # 会写 data\diagnostics.txt 并打开记事本
```

判据：日志里出现 `[轮播] 已设置 …` 说明壁纸设上了；出现 `设置桌面壁纸失败` 就往下走。

## 1. 进程在跑吗

```powershell
Get-Process BestWallpaper | Select-Object Id, StartTime
```

- 没进程：看日志末尾有没有 `=== 启动 ===`。有 → 启动后崩了，看 `[ERROR]`；没有 → 根本没启动（杀软拦截 / 路径错误）。
- 有进程但反复起落：大概率单实例互斥在起作用（另一个实例已存在），此时双击只会唤出已有实例的设置窗。

## 2. 进程是什么完整性级别（**最常见的坑**）

```powershell
# 1=Low 2=Medium 3=High；直接看标签更直观
powershell -File scripts\set-integrity.ps1 -Path dist\BestWallpaper.exe
```

- 文件标签是 `S-1-16-4096 (Low)` → **这就是根因**。修：`-Path dist -Recurse`，然后重启程序。
  低完整性进程写不了 HKCU、改不了壁纸，症状固定是 `win32=5`。
- 文件标签是 `S-1-16-8192 (Medium)`，进程也是 Medium → 继续往下。

## 3. 壁纸 API 到底卡在哪

看 `data\diagnostics.txt`：

| 字段 | 通过判据 | 不通过说明 |
| --- | --- | --- |
| `本进程` | `Medium(8192)` | `Low(4096)` → 回第 2 步 |
| `SPI_SET 持久模式(0x03)` | 成功 | `错误=5` = 拒绝访问 |
| `SPI_SET 会话模式(0x00)` | 成功 | 两种都失败 → 基本是权限级别问题 |
| `写入 HKCU\Control Panel\Desktop` | 成功 | 失败说明连用户注册表都写不了 → 回第 2 步 |
| `IDesktopWallpaper` | 激活成功 | `E_ACCESSDENIED` 与上面同源 |

## 4. 拉不到图（网络）

**只信 Python 或 .NET 的结论**——本机 PowerShell 的 `curl` / `Invoke-WebRequest` 会因沙箱凭据报假失败。

```powershell
python -c "import urllib.request,sys; r=urllib.request.urlopen('https://wallhaven.cc/api/v1/search?q=cat',timeout=15); print(r.status, len(r.read()))"
```

- wallhaven 需要代理：确认 `127.0.0.1:7892` 在跑，设置窗里代理选「跟随系统代理」。
- 想看兜底链走到第几层：日志里搜 `[wallhaven-高清]`、`[wallhaven-1080p]`、`[bing]`、`[历史库]`。

## 5. 每天不换池

```powershell
Get-ChildItem dist\data\pool -Directory | Select-Object Name, LastWriteTime
```

判据：应当存在以今天日期命名的目录。跨天检测每分钟跑一次，判据是 `config.json` 的 `lastPoolDate` 与今天不同。

## 6. 开机自启没生效

```powershell
Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name BestWallpaper
schtasks /query /tn BestWallpaper 2>$null    # 提权模式才用计划任务
```

判据：二者至少有其一，且路径指向当前 `dist\BestWallpaper.exe`（搬过文件夹要重设）。

## 附：会骗人的"假故障"清单

| 现象 | 其实不是故障 |
| --- | --- |
| PowerShell 里所有 HTTPS 都失败 | 沙箱令牌没有 Schannel 凭据；Python / .NET 正常 |
| `RedirectStandardOutput` 拿到空串 | 沙箱禁用管道；改用文件重定向 |
| `%TEMP%` 写文件被拒 | 用 `data\` 目录 |
| `schtasks /create /rl HIGHEST` 被拒 | 需要管理员，不是命令写错 |
| 双击后"没反应" | 托盘程序刻意不弹窗，先看日志与托盘折叠区 |
