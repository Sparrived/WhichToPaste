using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WhichToPaste.Models;
using WhichToPaste.Native;
using WhichToPaste.Services;
using WhichToPaste.UI;
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;
using DataFormats = System.Windows.DataFormats;
using DataObject = System.Windows.DataObject;
using Point = System.Windows.Point;
using WindowState = System.Windows.WindowState;

namespace WhichToPaste;

/// <summary>
/// 把钩子、剪贴板、选择框、托盘串起来。所有回调都回到 UI 线程执行。
/// </summary>
internal sealed class AppController : IDisposable
{
    private const string ActivateEventName = @"Local\WhichToPaste.Activate";

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly HookThread _hooks = new();
    private readonly ClipboardWatcher _clipboard;

    private PickerWindow? _picker;
    private SettingsWindow? _settingsWindow;
    private TrayIcon? _tray;
    private EventWaitHandle? _activateEvent;
    private bool _disposed;

    public Settings Settings { get; }

    public HistoryStore History { get; }

    public AppController()
    {
        Settings = SettingsStore.Load();
        History = new HistoryStore(Settings);
        History.Load();
        _clipboard = new ClipboardWatcher();
    }

    public void Start()
    {
        ThemeManager.Apply(Settings.ResolveDark());

        _hooks.KeyAction += OnKeyAction;
        _hooks.RightButtonDown += OnRightButtonDown;
        _hooks.RightButtonUp += OnRightButtonUp;
        _hooks.LeftButtonDown += OnLeftButtonDown;
        _hooks.Start();

        _clipboard.Changed += OnClipboardChanged;

        _tray = new TrayIcon(Settings.Paused);
        _tray.SettingsRequested += ShowSettings;
        _tray.PauseToggled += TogglePause;
        _tray.QuitRequested += Quit;

        ListenForActivation();
        SyncHookState();

        // 首次运行直接亮出设置页，否则用户会以为没启动。
        if (!SettingsStore.HasSettings) ShowSettings();
    }

    // ---------- 复制：一律经剪贴板监听，Ctrl+C 与右键"复制"都覆盖 ----------

    private void OnClipboardChanged() => ReadClipboard(attempt: 0);

    private void ReadClipboard(int attempt)
    {
        ClipItem? item = null;
        BitmapSource? bitmap = null;
        bool locked = false;

        try
        {
            // 优先级：文件 > 文本 > 图片。Excel 这类同时给文本和位图的，取文本更有用；
            // 截图工具只给位图，于是自然落到图片分支。
            if (Clipboard.GetDataObject() is not { } data) return;

            if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
                item = new ClipItem { Kind = ClipKind.Files, Files = files };
            else if (data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string text && !string.IsNullOrWhiteSpace(text))
                item = new ClipItem { Kind = ClipKind.Text, Text = text.Trim() };
            else if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource source)
                bitmap = source;
        }
        catch (COMException)
        {
            locked = true; // 别的进程正占着剪贴板
        }

        if (locked && attempt < 3)
        {
            RetryIn(80, () => ReadClipboard(attempt + 1));
            return;
        }

        if (bitmap is not null)
        {
            SaveImageInBackground(bitmap);
            return;
        }

        if (item is not null && History.Add(item)) SyncHookState();
    }

    /// <summary>PNG 编码有点慢，放后台做，完成后回 UI 线程入历史。</summary>
    private void SaveImageInBackground(BitmapSource source)
    {
        BitmapSource frozen;
        try
        {
            frozen = ImageStore.Freeze(source); // 剪贴板位图不能跨线程访问，先拷一份冻结副本
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
            return;
        }

        Task.Run(() =>
        {
            if (ImageStore.Save(frozen) is not { } saved) return;

            _dispatcher.InvokeAsync(() =>
            {
                var item = new ClipItem
                {
                    Kind = ClipKind.Image,
                    ImageFile = saved.File,
                    PixelWidth = saved.Width,
                    PixelHeight = saved.Height,
                };
                if (History.Add(item)) SyncHookState();
            });
        });
    }

    private void RetryIn(int milliseconds, Action action)
    {
        DispatcherTimer? timer = null;
        timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(milliseconds),
            DispatcherPriority.Background,
            (_, _) =>
            {
                timer!.Stop();
                action();
            },
            _dispatcher);
        timer.Start();
    }

    // ---------- 粘贴：钩子把按键送到这里 ----------

    private void OnKeyAction(HookKeyAction action) => _dispatcher.InvokeAsync(() => HandleKeyAction(action));

    private void HandleKeyAction(HookKeyAction action)
    {
        switch (action)
        {
            case HookKeyAction.PasteRequested:
                if (_picker?.IsVisible == true) CommitPaste();
                else ShowPicker(CaretLocator.CaretPoint());
                break;

            case HookKeyAction.MoveUp:
                _picker?.MoveSelection(-1);
                break;

            case HookKeyAction.MoveDown:
                _picker?.MoveSelection(1);
                break;

            case HookKeyAction.Cancel:
            case HookKeyAction.DismissAndPassThrough:
                HidePicker();
                break;
        }
    }

    private void ShowPicker(Point anchor)
    {
        if (Settings.Paused) return;

        List<ClipItem> items = History.Snapshot();
        for (int i = 0; i < items.Count; i++) items[i].Rank = i;

        if (_picker is null)
        {
            _picker = new PickerWindow();
            _picker.ItemChosen += OnItemChosen;
            _picker.IsVisibleChanged += (_, _) => SyncHookState();
        }

        _picker.ShowItems(items, anchor, Settings);
        SyncHookState();
    }

    private void HidePicker()
    {
        if (_picker?.IsVisible != true) return;
        _picker.Hide();
        SyncHookState();
    }

    private void OnItemChosen(ClipItem item)
    {
        if (_picker is null) return;
        _picker.SelectedItem = item;
        CommitPaste();
    }

    private void CommitPaste()
    {
        ClipItem? item = _picker?.SelectedItem;
        HidePicker();
        if (item is null) return;

        if (!TrySetClipboard(item)) return;

        // 选中项此刻成了系统剪贴板内容，历史里提到最新。
        History.Add(item);
        InputSender.SendPaste();
        SyncHookState();
    }

    /// <summary>别的进程可能正占着剪贴板，短暂重试几次。</summary>
    private static bool TrySetClipboard(ClipItem item)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (BuildDataObject(item) is not { } data) return false;
                Clipboard.SetDataObject(data, copy: true);
                return true;
            }
            catch (Exception ex) when (ex is COMException or IOException or NotSupportedException)
            {
                Thread.Sleep(40);
            }
        }
        return false;
    }

    /// <summary>按条目类型还原成剪贴板数据；图片文件读不出来时返回 null。</summary>
    private static DataObject? BuildDataObject(ClipItem item)
    {
        var data = new DataObject();

        switch (item.Kind)
        {
            case ClipKind.Files:
                data.SetData(DataFormats.FileDrop, item.Files);
                // Explorer 靠这个值决定"粘贴"是复制还是移动，缺了它可能变成移动。
                data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1)));
                break;

            case ClipKind.Image:
                if (item.ImageFile is null || ImageStore.Load(item.ImageFile) is not { } bitmap) return null;
                data.SetImage(bitmap);
                data.SetData("PNG", new MemoryStream(File.ReadAllBytes(ImageStore.PathOf(item.ImageFile))));
                break;

            default:
                data.SetText(item.Text);
                break;
        }

        return data;
    }

    // ---------- 右键：可编辑控件上直接弹选择框 ----------

    private void OnRightButtonDown(Win32.POINT point)
    {
        if (Settings.Paused || !Settings.TakeOverRightClick || History.IsEmpty) return;

        var anchor = new Point(point.X, point.Y);
        _ = Task.Run(() => _hooks.ArmRightClick(CaretLocator.IsEditableAt(anchor)));
    }

    private void OnRightButtonUp(Win32.POINT point) => _dispatcher.InvokeAsync(() =>
    {
        if (Settings.Paused) return;
        if (_picker?.IsVisible == true)
        {
            HidePicker();
            return;
        }
        ShowPicker(new Point(point.X, point.Y));
    });

    private void OnLeftButtonDown(Win32.POINT point) => _dispatcher.InvokeAsync(() =>
    {
        if (_picker?.IsVisible != true) return;
        if (!_picker.ContainsScreenPoint(point)) HidePicker();
    });

    // ---------- 托盘与设置 ----------

    public void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) =>
            {
                _settingsWindow = null;
                SyncHookState();
            };
        }

        _settingsWindow.Show();
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;

        _settingsWindow.Activate();
        _settingsWindow.Topmost = true; // 从托盘唤起时压到最前
        _settingsWindow.Topmost = false;
        _settingsWindow.Focus();
    }

    private void TogglePause()
    {
        Settings.Paused = !Settings.Paused;
        ApplySettings();
        _settingsWindow?.SyncFromSettings();
    }

    public void ApplySettings()
    {
        SettingsStore.Save(Settings);

        if (Settings.AutoStart != AutoStart.IsEnabled()) AutoStart.Apply(Settings.AutoStart);

        _tray?.SetPaused(Settings.Paused);
        SyncHookState();
    }

    public void ApplyTheme() => ThemeManager.Apply(Settings.ResolveDark());

    public void ClearHistory()
    {
        History.Clear();
        SyncHookState();
    }

    public void Quit()
    {
        Dispose();
        Application.Current?.Shutdown();
    }

    private void SyncHookState() => _hooks.Update(new HotkeyState(
        Enabled: !Settings.Paused,
        PickerVisible: _picker?.IsVisible == true,
        TakeOverRightClick: Settings.TakeOverRightClick,
        HasHistory: !History.IsEmpty,
        Paste: Settings.Paste,
        Up: Settings.Up,
        Down: Settings.Down,
        Cancel: Settings.Cancel,
        CancelAlt: Settings.CancelAlt));

    // ---------- 单实例 ----------

    private void ListenForActivation()
    {
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);

        var thread = new Thread(() =>
        {
            while (_activateEvent.WaitOne())
            {
                if (_disposed) return;
                _dispatcher.InvokeAsync(ShowSettings);
            }
        })
        {
            IsBackground = true,
            Name = "WhichToPaste.Activate",
        };
        thread.Start();
    }

    /// <summary>第二次启动时唤醒已在跑的那个实例，让它把设置页亮出来。</summary>
    public static void SignalExistingInstance()
    {
        try
        {
            EventWaitHandle.OpenExisting(ActivateEventName).Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _clipboard.Changed -= OnClipboardChanged;
        _clipboard.Dispose();

        _hooks.Dispose();
        _activateEvent?.Dispose();
        _tray?.Dispose();
        _tray = null;
    }
}