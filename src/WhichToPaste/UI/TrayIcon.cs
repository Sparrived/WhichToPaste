using System.Drawing;
using System.Windows.Forms;

namespace WhichToPaste.UI;

/// <summary>托盘图标：左键打开设置，右键给菜单。用 WinForms 的 NotifyIcon，省掉一个第三方包。</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _pauseItem;

    public event Action? SettingsRequested;
    public event Action? PauseToggled;
    public event Action? QuitRequested;

    public TrayIcon(bool paused)
    {
        var settingsItem = new ToolStripMenuItem("设置…")
        {
            Font = new Font(SystemFonts.MenuFont ?? SystemFonts.DefaultFont, FontStyle.Bold),
        };
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke();

        _pauseItem = new ToolStripMenuItem("暂停监听") { Checked = paused, CheckOnClick = false };
        _pauseItem.Click += (_, _) => PauseToggled?.Invoke();

        var quitItem = new ToolStripMenuItem("退出");
        quitItem.Click += (_, _) => QuitRequested?.Invoke();

        var menu = new ContextMenuStrip();
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(quitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = AppIcon.Tray,
            Text = Tooltip(paused),
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) SettingsRequested?.Invoke();
        };
    }

    public void SetPaused(bool paused)
    {
        _pauseItem.Checked = paused;
        _notifyIcon.Text = Tooltip(paused);
    }

    private static string Tooltip(bool paused) =>
        paused ? "WhichToPaste · 已暂停" : "WhichToPaste · 复制即记录";

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}