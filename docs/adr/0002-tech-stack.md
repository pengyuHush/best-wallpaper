# 技术栈：.NET 10 + WPF，框架依赖单文件发布，SDK 装在用户目录

本机没有 .NET SDK、MSBuild、Visual Studio、Windows SDK，也没有 Node/Rust/Go/Java/Git；只有 .NET 10 桌面运行时和 in-box 的 .NET Framework 4.8 编译器。选择下载 .NET SDK（用 dotnet-install 之外的直取 zip 方式装到 `%LOCALAPPDATA%\Microsoft\dotnet`，不弹 UAC）并用 WPF 实现：WPF 提供原生托盘宿主、per-monitor DPI 感知和像样的中文界面，而发布物只需几 MB，因为桌面运行时已在本机。

**Considered Options**: .NET Framework 4.8 + WinForms（零下载、实测可离线编译，但受限于 C# 5 语法与老旧视觉）、Python 3.15 + tkinter（托盘要 pip 装 pystray/Pillow，且产出是脚本而非绿色 exe）、自包含单文件发布（约 150MB，换机零依赖但体积大）。

**Consequences**: 换机器运行时需要先装 .NET 10 桌面运行时。沙箱令牌取不到 Schannel 凭据，本会话内所有 HTTPS 请求都会 `SEC_E_NO_CREDENTIALS`，因此下载链路无法由 agent 端到端验证，必须人工在真机跑一次并看日志。
