using System.Windows.Interop;

namespace WhichToPaste.Native;

/// <summary>
/// 用 AddClipboardFormatListener 监听剪贴板变化，从而统一覆盖 Ctrl+C 与右键"复制"
/// （两者最终都会写入剪贴板）。
/// </summary>
internal sealed class ClipboardWatcher : IDisposable
{
    private readonly HwndSource _source;

    public event Action? Changed;

    public ClipboardWatcher()
    {
        _source = new HwndSource(0, 0, 0, 0, 0, "WhichToPaste.ClipboardListener", IntPtr.Zero);
        _source.AddHook(WndProc);
        Win32.AddClipboardFormatListener(_source.Handle);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_CLIPBOARDUPDATE) Changed?.Invoke();
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Win32.RemoveClipboardFormatListener(_source.Handle);
        _source.Dispose();
    }
}