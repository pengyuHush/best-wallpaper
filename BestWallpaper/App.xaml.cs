using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using BestWallpaper.Services;
using BestWallpaper.Tray;
using BestWallpaper.UI;

namespace BestWallpaper;

public partial class App : Application
{
    private Engine? _engine;
    private TrayIcon? _tray;
    private SettingsWindow? _settings;

    private Mutex? _mutex;
    private bool _ownsMutex;
    private EventWaitHandle? _showSignal;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var args = Environment.GetCommandLineArgs();
        var autostart = args.Any(a => string.Equals(a, StartupService.AutostartArgument, StringComparison.OrdinalIgnoreCase));

        // --diagnose：只做环境自检、写报告，然后退出（用于排查"改了壁纸没反应"）
        if (args.Any(a => string.Equals(a, "--diagnose", StringComparison.OrdinalIgnoreCase)))
        {
            var file = Diagnostics.WriteReport();
            try
            {
                if (!string.IsNullOrEmpty(file))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", "\"" + file + "\"") { UseShellExecute = true });
            }
            catch { }
            Shutdown();
            return;
        }

        // --install-autostart / --uninstall-autostart：需要管理员权限的开关，由设置窗用 runas 调起
        if (args.Any(a => string.Equals(a, "--install-autostart", StringComparison.OrdinalIgnoreCase)))
        {
            var ok = ScheduledTask.Ensure(Environment.ProcessPath ?? "", atLogon: true);
            Log.Info("[任务] 安装登录自启: " + (ok ? "成功" : "失败"));
            Shutdown();
            return;
        }

        if (args.Any(a => string.Equals(a, "--uninstall-autostart", StringComparison.OrdinalIgnoreCase)))
        {
            var ok = ScheduledTask.Delete();
            Log.Info("[任务] 卸载登录自启: " + (ok ? "成功" : "失败"));
            Shutdown();
            return;
        }

        // --retrim：把今日池子已有的图重新裁一遍黑边（旧池子补救），然后退出
        if (args.Any(a => string.Equals(a, "--retrim", StringComparison.OrdinalIgnoreCase)))
        {
            var trimEngine = new Engine(Dispatcher);
            trimEngine.Initialize();
            var changed = trimEngine.RetrimCurrentPool();
            var reapplied = trimEngine.ReapplyCurrent();
            Log.Info($"[图片] --retrim 完成：改动 {changed} 张，重新设置壁纸={reapplied}");
            Shutdown();
            return;
        }

        // --themes：直接打开主题管理窗口（排查/快捷入口用）
        if (args.Any(a => string.Equals(a, "--themes", StringComparison.OrdinalIgnoreCase)))
        {
            var themeEngine = new Engine(Dispatcher);
            themeEngine.Initialize();
            try
            {
                new ThemeManagerWindow(themeEngine).ShowDialog();
            }
            catch (Exception ex)
            {
                Log.Error("[UI] 主题管理窗口加载失败", ex);
            }
            Shutdown();
            return;
        }

        // 静默提权：本机登录会话是提权的（explorer 为 High），双击得到的 Medium 实例改不了壁纸。
        // 只要之前授权过一次、计划任务已存在，就直接交给它以最高权限拉起，全程无 UAC、无弹窗。
        // fromTask 用于防止任务本身没能提权时无限自旋。
        var elevated = Elevation.IsElevated();
        var fromTask = args.Any(a => string.Equals(a, ScheduledTask.ElevatedArgument, StringComparison.OrdinalIgnoreCase));
        if (!elevated && !fromTask && ScheduledTask.Exists())
        {
            Log.Info("[启动] 检测到提权任务，静默交给它启动（不弹任何窗口）");
            ScheduledTask.RunNow();
            Shutdown();
            return;
        }

        _mutex = new Mutex(true, "BestWallpaper.SingleInstance", out var createdNew);
        _ownsMutex = createdNew;
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "BestWallpaper.ShowSettings");

        if (!createdNew)
        {
            // 已经在跑：把那个实例的设置窗叫出来，自己退出
            _showSignal.Set();
            Shutdown();
            return;
        }

        Log.Info("=== 启动 ===");
        Log.Info($"exe: {Environment.ProcessPath}");
        Log.Info($"args: {string.Join(' ', args)}");

        SetupExceptionHandling();

        _engine = new Engine(Dispatcher);
        _engine.Initialize();
        _engine.Notify += (title, text) => _tray?.ShowBalloon(title, text);
        _engine.Blocked += OnWallpaperBlocked;
        _engine.Changed += () => _tray?.UpdateTooltip();

        _tray = new TrayIcon(_engine, ShowSettings, ShowThemeManager, ExitApp);

        // 提权实例顺手把任务指向当前 exe：之后普通双击就会静默提权，不再弹 UAC / SmartScreen
        if (elevated)
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe)) ScheduledTask.Ensure(exe, _engine.Config.Autostart);
        }

        StartShowSignalWatcher();

        _ = StartEngineAsync(autostart);
    }

    private async Task StartEngineAsync(bool autostart)
    {
        try
        {
            await _engine!.StartAsync(autostart);
            _tray?.UpdateTooltip();
        }
        catch (Exception ex)
        {
            Log.Error("启动流程失败", ex);
            _tray?.ShowBalloon("启动异常", ex.Message);
        }
    }

    private void StartShowSignalWatcher()
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    if (_showSignal!.WaitOne())
                    {
                        Dispatcher.Invoke(ShowSettings);
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Log.Warn("[单实例] 等待信号失败: " + ex.Message);
                    return;
                }
            }
        })
        { IsBackground = true, Name = "show-settings-watcher" };
        thread.Start();
    }

    public void ShowSettings()
    {
        if (_engine is null) return;
        if (_settings is null)
        {
            _settings = new SettingsWindow(_engine);
            _settings.Closed += (_, _) => _settings = null;
        }

        if (!_settings.IsVisible) _settings.Show();
        if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
        _settings.Activate();
        _settings.Topmost = true;
        _settings.Topmost = false;
        _settings.Focus();
    }

    public void ShowThemeManager()
    {
        if (_engine is null) return;
        var window = new ThemeManagerWindow(_engine)
        {
            Owner = _settings is { IsVisible: true } ? _settings : null,
        };
        window.ShowDialog();
    }

    private void ExitApp()
    {
        Log.Info("=== 退出（用户操作托盘） ===");
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_engine is not null)
            {
                _engine.SaveConfig();
                Log.Info("=== 退出 ===");
            }

            _tray?.Dispose();
            _settings?.ForceClose();
            _showSignal?.Dispose();

            // 只有创建它的那个实例才持有互斥量；第二个实例释放会抛异常
            if (_ownsMutex)
            {
                try { _mutex?.ReleaseMutex(); } catch { }
                _ownsMutex = false;
            }
            _mutex?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("[退出] 清理异常: " + ex.Message);
        }

        base.OnExit(e);
    }

    private bool _blockedDialogShown;

    /// <summary>壁纸被系统拒绝时，别静默——明确告诉人发生了什么、以及怎么继续。</summary>
    private void OnWallpaperBlocked(string detail)
    {
        if (_blockedDialogShown) return;
        _blockedDialogShown = true;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            var choice = MessageBox.Show(
                "壁纸已经下载好了，但系统拒绝让本程序修改桌面壁纸。\n\n" +
                "错误：" + detail + "\n\n" +
                "最常见原因：资源管理器 explorer.exe 是以管理员身份运行的（High 完整性），\n" +
                "而双击启动的普通程序只有 Medium 权限，Windows 的 UIPI 会挡住它改壁纸。\n" +
                "（本项目实测：DACL 给了用户 FullControl、键上也没有完整性标签，纯粹是级别差。）\n\n" +
                "两个办法：\n" +
                "· 让资源管理器回到普通权限（任务管理器里结束 explorer.exe，它会自动重启）；\n" +
                "· 或者点「是」，本程序以管理员身份重启，直接和资源管理器同级。\n\n" +
                "要以管理员身份重启本程序吗？（会弹一次 UAC）\n\n" +
                "只需授权一次：重启后程序会自动登记一个最高权限计划任务，\n" +
                "以后双击就不会再弹任何窗口，也不需要再点 UAC。\n\n" +
                "选「否」则保持现状并打开日志。",
                "每日壁纸 · 无法修改桌面壁纸",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (choice == MessageBoxResult.Yes) RelaunchElevated();
            else _engine?.OpenLog();
        }), DispatcherPriority.Normal);
    }

    private void RelaunchElevated()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;

            // 先让出单实例锁，否则提权后的新进程会以为已有实例在跑、直接退出
            try
            {
                _mutex?.ReleaseMutex();
                _mutex?.Dispose();
            }
            catch { }
            _mutex = null;
            _ownsMutex = false;

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas",
            });

            Log.Info("[提权] 已请求以管理员身份重启");
            Shutdown();
        }
        catch (Exception ex)
        {
            Log.Error("[提权] 重启失败（多半是 UAC 被取消）", ex);
            MessageBox.Show("以管理员身份重启失败：" + ex.Message, "每日壁纸", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void SetupExceptionHandling()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("UI 线程未处理异常", args.Exception);
            args.Handled = true;
            try
            {
                _tray?.ShowBalloon("出现未处理异常", args.Exception.Message);
            }
            catch { }
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) Log.Error("应用域未处理异常", ex);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("未观察的任务异常", args.Exception);
            args.SetObserved();
        };
    }
}
