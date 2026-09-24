using System.Runtime.InteropServices;

namespace WhichToPaste.Native;

/// <summary>钩子线程读到的快捷键状态快照，整体替换以保证线程安全。</summary>
internal sealed record HotkeyState(
    bool Enabled,
    bool PickerVisible,
    bool TakeOverRightClick,
    bool HasHistory,
    Services.KeyCombo Paste,
    Services.KeyCombo Up,
    Services.KeyCombo Down,
    Services.KeyCombo Cancel,
    Services.KeyCombo CancelAlt);

internal enum HookKeyAction
{
    PasteRequested,
    MoveUp,
    MoveDown,
    Cancel,
    DismissAndPassThrough,
}

/// <summary>
/// WH_KEYBOARD_LL / WH_MOUSE_LL 钩子宿主。低级钩子要求安装线程持续泵消息，
/// 因此独占一个线程；回调里只做判断与投递，绝不碰 UI。
/// </summary>
internal sealed class HookThread : IDisposable
{
    private readonly uint _ownerProcessId = Win32.GetCurrentProcessId();
    private readonly HashSet<uint> _swallowedKeys = [];

    private Win32.HookProc? _keyboardProc; // 保持存活，防止回调被 GC
    private Win32.HookProc? _mouseProc;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private Thread? _thread;
    private uint _threadId;
    private volatile bool _stopping;

    /// <summary>右键"按下"时目标是否可编辑，由 UI 线程异步探测后置位。</summary>
    private volatile bool _rightClickArmed;

    private volatile HotkeyState _state = new(
        Enabled: true,
        PickerVisible: false,
        TakeOverRightClick: true,
        HasHistory: false,
        Paste: new Services.KeyCombo(Services.HotkeyModifiers.Control, VK.KEY_V),
        Up: new Services.KeyCombo(Services.HotkeyModifiers.Control, VK.KEY_C),
        Down: new Services.KeyCombo(Services.HotkeyModifiers.Control, VK.KEY_B),
        Cancel: new Services.KeyCombo(Services.HotkeyModifiers.None, VK.ESCAPE),
        CancelAlt: new Services.KeyCombo(Services.HotkeyModifiers.Control, VK.KEY_Z));

    public event Action<HookKeyAction>? KeyAction;
    public event Action<Win32.POINT>? RightButtonDown;
    public event Action<Win32.POINT>? RightButtonUp;
    public event Action<Win32.POINT>? LeftButtonDown;

    public void Update(HotkeyState state) => _state = state;

    /// <summary>UI 线程完成"目标可编辑"探测后调用，决定右键是否被拦截。</summary>
    public void ArmRightClick(bool editable) => _rightClickArmed = editable;

    public void Start()
    {
        if (_thread is not null) return;
        _stopping = false;
        _thread = new Thread(ThreadBody)
        {
            IsBackground = true,
            Name = "WhichToPaste.Hooks",
        };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    private void ThreadBody()
    {
        _threadId = Win32.GetCurrentThreadId();
        _keyboardProc = KeyboardProc;
        _mouseProc = MouseProc;

        IntPtr module = Win32.GetModuleHandleW(null);
        _keyboardHook = Win32.SetWindowsHookExW(Win32.WH_KEYBOARD_LL, _keyboardProc, module, 0);
        _mouseHook = Win32.SetWindowsHookExW(Win32.WH_MOUSE_LL, _mouseProc, module, 0);

        while (!_stopping && Win32.GetMessageW(out Win32.MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessageW(ref msg);
        }
        if (_keyboardHook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_mouseHook);
        _keyboardHook = _mouseHook = IntPtr.Zero;
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

        int message = (int)wParam;
        var data = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
        uint vk = data.vkCode;

        // 抬起：把键从"已吞下"集合里摘掉，这样下一次按下还能再触发一次。
        // 被我们吞过的抬起也一并吞掉，目标窗口不会收到落单的 keyup。
        if (message is Win32.WM_KEYUP or Win32.WM_SYSKEYUP)
        {
            return _swallowedKeys.Remove(vk)
                ? 1
                : Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        if (message is not (Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN))
            return Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

        if (VK.IsModifier(vk)) return Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        // 只放行自己注入的按键（避免自触发），第三方注入照常当普通按键处理。
        if (data.dwExtraInfo == InputSender.Signature) return Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        if (!_state.Enabled) return Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        if (IsOwnProcessForeground()) return Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

        var mods = CurrentModifiers();
        var st = _state;

        if (st.HasHistory && st.Paste.Matches(mods, vk))
        {
            if (Swallow(vk)) Emit(HookKeyAction.PasteRequested);
            return 1;
        }

        if (st.PickerVisible)
        {
            if (st.Up.Matches(mods, vk) || (mods == Services.HotkeyModifiers.None && vk == VK.UP))
            {
                if (Swallow(vk)) Emit(HookKeyAction.MoveUp);
                return 1;
            }
            if (st.Down.Matches(mods, vk) || (mods == Services.HotkeyModifiers.None && vk == VK.DOWN))
            {
                if (Swallow(vk)) Emit(HookKeyAction.MoveDown);
                return 1;
            }
            if (st.Cancel.Matches(mods, vk) || st.CancelAlt.Matches(mods, vk))
            {
                if (Swallow(vk)) Emit(HookKeyAction.Cancel);
                return 1;
            }

            // 任何其它按键都表示"放弃选历史、正常输入"：收起选择框，按键照常传达。
            Emit(HookKeyAction.DismissAndPassThrough);
        }

        return Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);

        var st = _state;
        if (!st.Enabled || !st.TakeOverRightClick || !st.HasHistory)
            return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);

        int msg = (int)wParam;
        if (msg is not (Win32.WM_RBUTTONDOWN or Win32.WM_RBUTTONUP or Win32.WM_LBUTTONDOWN))
            return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);

        if (IsOwnProcessForeground()) return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);

        var data = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);

        if (msg == Win32.WM_RBUTTONDOWN)
        {
            _rightClickArmed = false;
            RightButtonDown?.Invoke(data.pt);
        }
        else if (msg == Win32.WM_RBUTTONUP && _rightClickArmed)
        {
            // 目标是输入框：吞掉右键，不弹系统菜单，改由我们弹选择框。
            _rightClickArmed = false;
            RightButtonUp?.Invoke(data.pt);
            return 1;
        }
        else if (msg == Win32.WM_LBUTTONDOWN && st.PickerVisible)
        {
            // 点在选择框外面就收起它；点在里面交给窗口自己处理。
            LeftButtonDown?.Invoke(data.pt);
        }

        return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    /// <summary>长按产生的重复 keydown 只处理第一次。</summary>
    private bool Swallow(uint vk) => _swallowedKeys.Add(vk);

    private void Emit(HookKeyAction action) => KeyAction?.Invoke(action);

    private bool IsOwnProcessForeground()
    {
        IntPtr foreground = Win32.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        Win32.GetWindowThreadProcessId(foreground, out uint pid);
        return pid == _ownerProcessId;
    }

    private static Services.HotkeyModifiers CurrentModifiers()
    {
        var mods = Services.HotkeyModifiers.None;
        if (IsDown(VK.CONTROL)) mods |= Services.HotkeyModifiers.Control;
        if (IsDown(VK.SHIFT)) mods |= Services.HotkeyModifiers.Shift;
        if (IsDown(VK.MENU)) mods |= Services.HotkeyModifiers.Alt;
        if (IsDown(VK.LWIN) || IsDown(VK.RWIN)) mods |= Services.HotkeyModifiers.Win;
        return mods;
    }

    private static bool IsDown(uint vk) => (Win32.GetAsyncKeyState((int)vk) & 0x8000) != 0;

    public void Dispose()
    {
        if (_thread is null) return;
        _stopping = true;
        if (_threadId != 0) Win32.PostThreadMessageW(_threadId, Win32.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
    }
}