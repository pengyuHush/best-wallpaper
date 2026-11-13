# 每日壁纸（BestWallpaper）

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![build](https://github.com/pengyuHush/best-wallpaper/actions/workflows/build.yml/badge.svg)](https://github.com/pengyuHush/best-wallpaper/actions/workflows/build.yml)

个人自用的 Windows 桌面壁纸软件：每天按主题从免费图源抓十张壁纸，在本机按固定间隔轮播，支持开机自启、离线兜底与中文字典式主题。

![icon](BestWallpaper/assets/app.png)

> 一个"够用就好"的小工具：约 4.4k 行 C#，无第三方 NuGet 依赖，只要求 .NET 10 桌面运行时。
> 设计取舍与踩过的坑都写进了 [`docs/adr/`](docs/adr/)（4 篇 ADR）和 [`GLOSSARY.md`](GLOSSARY.md)。

## 快速开始

**方式一：下载预编译 exe** —— 到 [Releases](../../releases) 下载后解压，双击 `BestWallpaper.exe`。
（仓库不提交二进制；如果你要自己出包，见下方「从源码构建」。）

**方式二：自己构建** —— `powershell -File build.ps1`，产物在 `dist\`。

程序是托盘程序，**启动时不会弹窗口**：图标在任务栏右下角的 `^` 折叠区。
首次运行会在 exe 旁边生成 `data\`（配置、壁纸池、日志），所以请放在可写目录（不要放 `Program Files`）。

## 行为规格

| 项目 | 行为 |
| --- | --- |
| 每日换池 | 启动时拉一次；运行中跨过本地日期再拉一次（不依赖定时器，也不会因为不关机而永不更新） |
| 每池张数 | 目标 10 张；实测不足 10 张有几张用几张，一张都没有才算失败 |
| 轮播 | 池内打乱顺序播完再从头；间隔默认 10 分钟，可设 1–1440 分钟 |
| 外部改动 | 每 15 秒比对当前壁纸，不是程序设的那张就暂停轮播并提示；托盘可恢复，重启程序自动恢复 |
| 兜底链 | wallhaven 高分辨率（≥2560×1440）→ wallhaven 1080p → Bing 最近 8 天每日图 → 本地历史库 → 保持原壁纸 + 30 分钟后重试 |
| 排除 | 按壁纸 id 永久生效，不再出现在任何池子里 |
| 收藏 | 复制到 `data\favorites\`，不参与清理 |
| 存储 | 保留最近 7 天池子；总占用超过 2GB 时从最旧的删 |
| 多显示器 | 所有屏幕同一张图，只挑能覆盖最大屏幕分辨率的图，Fill 裁切 |
| 黑边 | 下载后自动检测并裁掉**源图自带**的黑边（电影截图常见），再裁到屏幕比例；旧池子用 `BestWallpaper.exe --retrim` 补裁 |
| 通知 | 只在拉取失败 / 新池就绪时弹一次托盘气泡 |
| 今日主题 | 指定后只对当天生效、优先于固定主题，并立刻重抓；跨天自动失效 |
| 主题管理 | 按方向分组 + 搜索 + 行内编辑；内置词可停用/可覆盖修改（可恢复默认），自定义词可增删改 |

## 目录结构

```
BestWallpaper.exe
data\
  config.json              设置、排除列表、收藏索引（明文）
  pool\YYYY-MM-DD\         当日壁纸池 + meta.json（主题、来源、分辨率）
  favorites\               收藏副本（永久）
  logs\app-YYYYMMDD.log    滚动日志，保留 7 天、单文件超 10MB 换名
```

## 主题

「主题」是一个可搜索的题材：**中文显示名 + 英文搜索标签**（如「猫」= `cat`）。中文本身搜不到，必须映射成 wallhaven 认的标签。

**选择顺序（优先级从高到低）**：

1. **今日主题** —— 你在设置窗顶部或主题管理里指定，只影响当天，并立刻重抓十张；跨天自动失效。
2. **固定主题** —— 模式选「固定」时天天用它。
3. **每天随机** —— 从主题池里抽一个。

**内置 7 个方向、30 个词**：

| 方向 | 词（中文 → 标签） |
| --- | --- |
| 自然风景 | 山 mountains · 海 ocean · 森林 forest · 星空 starry sky · 云 clouds |
| 萌宠 | 猫 cat · 狗 dog · 兔 rabbit · 狐狸 fox · 小动物 animal |
| 游戏 | 游戏 video game · 游戏原画 game art · 像素风 pixel art · 奇幻 fantasy |
| 极简抽象 | 极简 minimalism · 抽象 abstract · 几何 geometry · 渐变 gradient |
| 城市赛博朋克 | 城市 cityscape · 城市夜景 night city · 赛博朋克 cyberpunk · 霓虹 neon |
| 太空科幻 | 太空 space · 星云 nebula · 科幻 sci fi · 星球 planet |
| 汽车机械 | 汽车 car · 摩托 motorcycle · 机械 mechanical · 飞机 aircraft |

### 主题管理（托盘「主题管理…」或设置窗按钮）

- 按方向分组 + 顶部搜索（匹配中文名或标签）
- 行内编辑中文名与标签；**内置词的修改以覆盖形式存在配置里**，可「恢复默认」
- 每行可启用/停用；停用只是标记，不删除
- 内置词不可删（可停用）；自定义词可删，删除正在使用的会提示并自动退回随机
- 新增：填中文名 → 自动查词典/翻译得到标签（可改）→ 试搜验证 → 保存
- 设置「今日主题」两处都行：设置窗顶部输入框，或管理窗口每行的「设为今日」

### 中文怎么变成英文标签

三级兜底：**内置词典**（约 150 条，直接映射到 wallhaven 真实标签）→ **在线翻译**（MyMemory，免 key；结果会清洗掉冠词/标点并最多保留三个词）→ **手填**。机器翻译对中文词质量不稳定（曾把「猫咪」翻成 `The pussy cats`），所以结果永远放进可编辑框让人确认。

保存前会**试搜一次**验证标签有没有图：0 结果会提醒你；网络不通则按「未验证」保存，联网后补验。


## 从源码构建

需要一个 .NET 10 SDK。本机已装在 `%LOCALAPPDATA%\Microsoft\dotnet`（解压即用，不需管理员权限、没动系统 PATH）。

```powershell
.\build.ps1              # 编译 + 自测 + 发布到 dist\
.\build.ps1 -SkipTests   # 只编译和发布
.\build.ps1 -SelfContained   # 出一份自带运行时的大 exe（约 150MB，换机零依赖）
```

项目结构：

```
BestWallpaper\        主程序（WPF + WinForms 托盘）
  Models\             配置与壁纸池的数据结构
  Services\           源站、下载、拼池、壁纸 API、自启、存储、引擎
  Tray\               托盘图标与菜单
  UI\                 单页滚动设置窗
tests\                零依赖自测（不引 NuGet，故意避开沙箱网络问题）
docs\adr\             关键决策记录（ADR，4 篇）
```

## 自测

```powershell
dotnet run --project tests\BestWallpaper.Tests\BestWallpaper.Tests.csproj -c Release
```

94 条断言，覆盖：wallhaven / Bing 真实响应的解析、分辨率与比例过滤、7 天去重、主题模型（内置/自定义/停用/覆盖/今日主题/查找）、中文→英文词典与清洗、配置往返序列化、黑边裁剪、计划任务 XML、壁纸 API 可读性。用的样本是实测抓回来的 JSON（`tests\fixtures\`）。

## 故障排查：双击了但壁纸不换 / 好像"没反应"

程序是托盘程序，**启动时刻意不弹窗**，所以先看这两处：

1. `data\logs\app-YYYYMMDD.log` —— 每次启动、搜索、下载、设置壁纸都有记录。
2. 运行 `BestWallpaper.exe --diagnose`，会生成 `data\diagnostics.txt` 并自动打开，里面写明：本进程权限级别、SPI 两种模式的结果、注册表可写性、COM 激活结果、DACL。

**本机已实测并修好过一个真实故障**（2026-11-03）：

```
本进程 : Low(4096)                       ← 根因：exe 文件带 Low 完整性标签
SPI_SET 持久模式(0x03) : 失败 错误=5
SPI_SET 会话模式(0x00) : 失败 错误=5
HKCU\Control Panel\Desktop 写入 : 拒绝访问
DACL(本用户) : Allow:FullControl         ← 权限没问题
注册表键完整性标签 : 没有 SACL            ← 注册表也没问题
IDesktopWallpaper : E_ACCESSDENIED
```

根因不在权限、不在 UAC、不在组策略：**workspace 里的文件被打了 Low 强制完整性标签**（`S-1-16-4096`）。
Windows 启动带 Low 标签的可执行文件时，进程也以 Low 完整性运行，而 Low 进程写不了 HKCU、改不了桌面壁纸。
（排查过程中容易把 RID 记错一级：4096=Low、8192=Medium、12288=High。分界线是 Low vs Medium。）

**修法**（已固化进构建流程）：

```powershell
# 单看标签 / 单修一个文件
.\scripts\set-integrity.ps1 -Path dist\BestWallpaper.exe
# 修整个发布目录（build.ps1 发布后会自动执行这一条）
.\scripts\set-integrity.ps1 -Path dist -Recurse
```

修好后同一个 exe 的 `--diagnose` 会变成 `本进程 : Medium(8192)`、`SPI_SET ... 成功`。

## 已知限制与免责声明

- **wallhaven 在多数大陆网络需要代理**：直连会被 DNS 污染（30 秒超时），走系统代理约 1.5 秒。程序默认「跟随系统代理」；没有代理时会退到 Bing 每日图（直连可用）+ 本地历史库。
- **没有使用 Pexels**：其 API 条款明文禁止「把 Pexels 内容作为壁纸应用提供」。详见 [`docs/adr/0001-wallpaper-source.md`](docs/adr/0001-wallpaper-source.md)。
- **图片版权归原作者**：本程序只在运行时下载并显示，不重新分发任何图片；请仅用于个人桌面。
- 图片存原图，PNG 多的日子单池可能到 60MB 上下；2GB 上限与 7 天保留足以控制磁盘。
- exe 未签名，第一次运行可能被 SmartScreen 拦一次（更多信息 → 仍要运行）。
- 勾选开机自启后，值写在 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`（写失败会在设置窗明确报错）。

## 参与

欢迎提 [issue](../../issues) 与 PR。这个项目刻意保持**零第三方依赖**（连测试框架都没引），新增依赖前请先开 issue 讨论；
改代码前建议先读 [`docs/TROUBLESHOOTING.md`](docs/TROUBLESHOOTING.md) 与 [`docs/adr/`](docs/adr/)。

## 许可

[MIT](LICENSE)
