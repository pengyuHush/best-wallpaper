using System.Drawing;
using System.Windows.Forms;
using BestWallpaper.Services;

namespace BestWallpaper.Tray;

/// <summary>托盘图标与右键菜单。用 WinForms 的 NotifyIcon（WPF 没有原生托盘）。</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Engine _engine;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _statusItem;
    private readonly Action _showSettings;
    private readonly Action _showThemes;
    private readonly Action _exit;

    public TrayIcon(Engine engine, Action showSettings, Action showThemes, Action exit)
    {
        _engine = engine;
        _showSettings = showSettings;
        _showThemes = showThemes;
        _exit = exit;

        _statusItem = new ToolStripMenuItem("—") { Enabled = false };
        var next = new ToolStripMenuItem("立即换下一张", null, (_, _) => engine.NextNow());
        var exclude = new ToolStripMenuItem("排除这张", null, (_, _) => engine.ExcludeCurrent());
        var favorite = new ToolStripMenuItem("收藏这张", null, (_, _) => engine.FavoriteCurrent());
        var openLocation = new ToolStripMenuItem("打开图片所在位置", null, (_, _) => engine.OpenCurrentLocation());
        var refresh = new ToolStripMenuItem("立即重新抓取今日 10 张", null, (_, _) => _ = engine.RefreshAsync(manual: true));
        _pauseItem = new ToolStripMenuItem("暂停轮播", null, (_, _) => engine.TogglePause());
        var themes = new ToolStripMenuItem("主题管理…", null, (_, _) => _showThemes());
        var settings = new ToolStripMenuItem("设置…", null, (_, _) => _showSettings());
        var quit = new ToolStripMenuItem("退出", null, (_, _) => _exit());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(next);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exclude);
        menu.Items.Add(favorite);
        menu.Items.Add(openLocation);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(refresh);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(themes);
        menu.Items.Add(settings);
        menu.Items.Add(quit);
        menu.Opening += (_, _) => RefreshMenuState();

        var icon = LoadIcon();
        _icon = new NotifyIcon
        {
            Icon = icon,
            Text = "每日壁纸",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => _showSettings();

        engine.Changed += UpdateTooltip;
        UpdateTooltip();
    }

    private static Icon LoadIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var icon = Icon.ExtractAssociatedIcon(exe);
                if (icon is not null) return icon;
            }
        }
        catch { }
        return SystemIcons.Application;
    }

    private void RefreshMenuState()
    {
        _statusItem.Text = Trim(_engine.Status, 60);
        _pauseItem.Text = _engine.Config.Paused ? "恢复轮播" : "暂停轮播";
    }

    public void UpdateTooltip()
    {
        try
        {
            _icon.Text = Trim(_engine.Status, 63);
        }
        catch { }
    }

    public void ShowBalloon(string title, string text)
    {
        if (!_engine.Config.Notifications) return;
        try
        {
            _icon.ShowBalloonTip(4000, title, Trim(text, 250), ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Log.Warn("[托盘] 气泡失败: " + ex.Message);
        }
    }

    private static string Trim(string s, int max) => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..(max - 1)] + "…");

    public void Dispose()
    {
        try
        {
            _engine.Changed -= UpdateTooltip;
            _icon.Visible = false;
            _icon.Dispose();
        }
        catch { }
    }
}
