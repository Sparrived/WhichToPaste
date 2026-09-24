using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WhichToPaste.Services;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using AppTheme = WhichToPaste.Services.AppTheme;

namespace WhichToPaste.UI;

/// <summary>托盘点开的设置页：快捷键、触发方式、历史、常规。</summary>
public partial class SettingsWindow : Window
{
    private readonly AppController _controller;
    private readonly Settings _settings;
    private Button? _capturing;

    internal SettingsWindow(AppController controller)
    {
        _controller = controller;
        _settings = controller.Settings;

        InitializeComponent();
        AppMark.Source = AppIcon.Mark;
        PreviewKeyDown += OnPreviewKeyDown;

        // 窗口高度吃满工作区（小屏就滚动），别让内容被无谓地截断。
        MaxHeight = Math.Max(420, SystemParameters.WorkArea.Height - 24);
        ContentScroller.MaxHeight = Math.Max(260, MaxHeight - 96);

        LoadState();
    }

    private void LoadState()
    {
        PasteKeyButton.Content = _settings.PasteKey;
        UpKeyButton.Content = _settings.UpKey;
        DownKeyButton.Content = _settings.DownKey;
        CancelKeyButton.Content = _settings.CancelKey;
        CancelAltKeyButton.Content = _settings.CancelAltKey;

        RightClickSwitch.IsChecked = _settings.TakeOverRightClick;
        PauseSwitch.IsChecked = _settings.Paused;
        AutoStartSwitch.IsChecked = _settings.AutoStart;
        LimitBox.Text = _settings.HistoryLimit.ToString();

        ThemeSystem.IsChecked = _settings.Theme == AppTheme.System;
        ThemeLight.IsChecked = _settings.Theme == AppTheme.Light;
        ThemeDark.IsChecked = _settings.Theme == AppTheme.Dark;

        HistorySummary.Text = $"当前 {_controller.History.Snapshot().Count} 条";
        FooterText.Text = $"版本 {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3)} · " +
                          $"历史与配置保存在 {SettingsStore.DataDirectory}";
    }

    /// <summary>托盘里改了暂停状态后，把开关拉回同步。</summary>
    public void SyncFromSettings()
    {
        PauseSwitch.IsChecked = _settings.Paused;
        HistorySummary.Text = $"当前 {_controller.History.Snapshot().Count} 条";
    }

    // ---------- 快捷键录入 ----------
    private void OnCapturePasteKey(object sender, RoutedEventArgs e) => BeginCapture(PasteKeyButton);
    private void OnCaptureUpKey(object sender, RoutedEventArgs e) => BeginCapture(UpKeyButton);
    private void OnCaptureDownKey(object sender, RoutedEventArgs e) => BeginCapture(DownKeyButton);
    private void OnCaptureCancelKey(object sender, RoutedEventArgs e) => BeginCapture(CancelKeyButton);
    private void OnCaptureCancelAltKey(object sender, RoutedEventArgs e) => BeginCapture(CancelAltKeyButton);

    private void BeginCapture(Button button)
    {
        CancelCapture();
        _capturing = button;
        button.Content = "按下新组合…";
        button.Focus();
    }

    private void CancelCapture()
    {
        if (_capturing is null) return;
        _capturing.Content = _capturing == PasteKeyButton ? _settings.PasteKey
            : _capturing == UpKeyButton ? _settings.UpKey
            : _capturing == DownKeyButton ? _settings.DownKey
            : _capturing == CancelKeyButton ? _settings.CancelKey
            : _settings.CancelAltKey;
        _capturing = null;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturing is null) return;
        e.Handled = true;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        HotkeyModifiers modifiers = ToModifiers(Keyboard.Modifiers);

        if (key == Key.Escape && modifiers == HotkeyModifiers.None)
        {
            CancelCapture();
            return;
        }

        // 只按住了修饰键：先给个预览，等主键落下再定案。
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System or Key.None)
        {
            _capturing.Content = Describe(modifiers) + "…";
            return;
        }

        var combo = new KeyCombo(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
        Button target = _capturing;
        _capturing = null;

        switch (target.Name)
        {
            case nameof(PasteKeyButton): _settings.PasteKey = combo.ToString(); break;
            case nameof(UpKeyButton): _settings.UpKey = combo.ToString(); break;
            case nameof(DownKeyButton): _settings.DownKey = combo.ToString(); break;
            case nameof(CancelKeyButton): _settings.CancelKey = combo.ToString(); break;
            default: _settings.CancelAltKey = combo.ToString(); break;
        }

        target.Content = combo.ToString();
        _controller.ApplySettings();
    }

    private void OnResetHotkeysClicked(object sender, RoutedEventArgs e)
    {
        var defaults = new Settings();
        _settings.PasteKey = defaults.PasteKey;
        _settings.UpKey = defaults.UpKey;
        _settings.DownKey = defaults.DownKey;
        _settings.CancelKey = defaults.CancelKey;
        _settings.CancelAltKey = defaults.CancelAltKey;

        LoadState();
        _controller.ApplySettings();
    }

    private static HotkeyModifiers ToModifiers(ModifierKeys keys)
    {
        var modifiers = HotkeyModifiers.None;
        if (keys.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Control;
        if (keys.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (keys.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (keys.HasFlag(ModifierKeys.Windows)) modifiers |= HotkeyModifiers.Win;
        return modifiers;
    }

    private static string Describe(HotkeyModifiers modifiers)
    {
        var parts = new List<string>(4);
        if (modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        return string.Join("+", parts);
    }

    // ---------- 开关 ----------

    private void OnRightClickToggled(object sender, RoutedEventArgs e)
    {
        _settings.TakeOverRightClick = RightClickSwitch.IsChecked == true;
        _controller.ApplySettings();
    }

    private void OnPauseToggled(object sender, RoutedEventArgs e)
    {
        _settings.Paused = PauseSwitch.IsChecked == true;
        _controller.ApplySettings();
    }

    private void OnAutoStartToggled(object sender, RoutedEventArgs e)
    {
        _settings.AutoStart = AutoStartSwitch.IsChecked == true;
        _controller.ApplySettings();
    }

    private void OnLimitChanged(object sender, TextChangedEventArgs e)
    {
        if (!int.TryParse(LimitBox.Text, out int limit) || limit is < 1 or > 200) return;
        _settings.HistoryLimit = limit;
        _controller.ApplySettings();
    }

    private void OnClearHistoryClicked(object sender, RoutedEventArgs e)
    {
        MessageBoxResult answer = MessageBox.Show(this,
            "清空后无法恢复，确定要删除全部剪贴板历史吗？", "清空历史",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);

        if (answer != MessageBoxResult.OK) return;

        _controller.ClearHistory();
        LoadState();
    }

    // ---------- 外观 ----------

    private void OnThemeSystemClicked(object sender, RoutedEventArgs e) => SetTheme(AppTheme.System);
    private void OnThemeLightClicked(object sender, RoutedEventArgs e) => SetTheme(AppTheme.Light);
    private void OnThemeDarkClicked(object sender, RoutedEventArgs e) => SetTheme(AppTheme.Dark);

    private void SetTheme(AppTheme mode)
    {
        _settings.Theme = mode;
        ThemeSystem.IsChecked = mode == AppTheme.System;
        ThemeLight.IsChecked = mode == AppTheme.Light;
        ThemeDark.IsChecked = mode == AppTheme.Dark;

        _controller.ApplySettings();
        _controller.ApplyTheme();
    }

    // ---------- 窗口 ----------

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnQuitClicked(object sender, RoutedEventArgs e) => _controller.Quit();
}