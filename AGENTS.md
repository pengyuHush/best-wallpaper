# 每日壁纸 · 给 agent 的作业须知

个人自用的 Windows 桌面壁纸工具：WPF 单文件 exe，每天按主题取十张壁纸并轮播。
改代码前读完本文；遇到"壁纸不换 / 双击没反应"，直接读 [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md)。

## 铁律

1. **交付前校验 exe 的完整性标签是 Medium**。跑
   `powershell -File scripts\set-integrity.ps1 -Path dist\BestWallpaper.exe`，读到的应是 `S-1-16-8192`。
   标签是 Low 时程序以低完整性运行，写不了 HKCU、改不了壁纸，而唯一症状是 `SPI_SET` 返回 `win32=5`。
   `build.ps1` 会自动修；绕过它手工 publish 时，自己补这一步。
2. **网络结论用 Python 或 .NET 取**。本机 PowerShell 的 `curl.exe` / `Invoke-WebRequest` 因
   `SEC_E_NO_CREDENTIALS` 全部失败，那是沙箱令牌，不是网络故障。
3. **子进程输出走文件重定向**：`cmd /c foo.exe args > out.txt`，再读 `out.txt`。
   `RedirectStandardOutput` 在本沙箱会拿到空输出。
4. **失败必须大声**：任何"拿不到 / 设不上"的分支都要写日志，并让 `--diagnose` 能复现。
   静默失败会让用户以为程序没启动，代价是一整轮沟通。
5. **先自检再改代码**：`dist\BestWallpaper.exe --diagnose` → `data\diagnostics.txt`
   （进程权限级别、SPI 两种模式结果、注册表可写性、COM 激活、DACL）。

## 本机事实（2026-11 实测，会过期）

- wallhaven 直连被 DNS 污染（30 秒超时），必须经系统代理 `127.0.0.1:7892`；Bing 每日图直连可用；
  Pexels 的 API 条款明文禁止壁纸应用（见 `docs/adr/0001-wallpaper-source.md`）。
- .NET SDK 在 `%LOCALAPPDATA%\Microsoft\dotnet`（10.0.403），不在 PATH；本机无 git、无 node。
- 构建一条命令：`powershell -File build.ps1`（编译 → 94 条自测 → 发布 → 修标签）。
  测试项目零 NuGet 依赖是刻意的，别引测试框架。

## 改动边界

- 设置窗、托盘菜单、日志、`data\config.json` 字段都是用户可见契约；先看 README 的「行为规格」表。
- 加壁纸源要同时接入兜底链与 `PoolBuilder`，只实现 `IImageSource` 不会生效。
- 主题的一切合并规则只在 `ThemeCatalog.All/Pool` 里；新增主题字段时同步 `ConfigStore.Normalize`，
  并按 [docs/adr/0004-theme-model.md](docs/adr/0004-theme-model.md) 的优先级（今日 → 固定 → 随机）处理。
- 术语以 [GLOSSARY.md](GLOSSARY.md) 为准：壁纸池、轮播、主题、今日主题、固定主题、停用、兜底链、轮播暂停。
