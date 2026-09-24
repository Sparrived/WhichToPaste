using System.Runtime.InteropServices;
using System.Windows.Automation;
using Point = System.Windows.Point;

namespace WhichToPaste.Native;

/// <summary>
/// 找到"插入符位置"用于贴近输入框弹窗，并判断右键落点是不是可编辑控件。
/// UIA 调用可能被目标进程拖住，一律带超时，超时就退回 Win32 插入符/光标位置。
/// </summary>
internal static class CaretLocator
{
    public static Point CaretPoint()
    {
        Point? caret = FromGuiThreadInfo();
        if (caret is not null) return caret.Value;

        caret = RunWithTimeout(FromUiaTextPattern, TimeSpan.FromMilliseconds(300), null);
        if (caret is not null) return caret.Value;

        Win32.GetCursorPos(out Win32.POINT cursor);
        return new Point(cursor.X, cursor.Y);
    }

    public static bool IsEditableAt(Point screenPoint)
    {
        bool? editable = RunWithTimeout(() => UiaEditable(screenPoint), TimeSpan.FromMilliseconds(400), (bool?)null);
        return editable ?? FromGuiThreadInfo() is not null;
    }

    private static Point? FromGuiThreadInfo()
    {
        IntPtr foreground = Win32.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return null;

        uint threadId = Win32.GetWindowThreadProcessId(foreground, out _);
        var info = new Win32.GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.GUITHREADINFO>() };
        if (!Win32.GetGUIThreadInfo(threadId, ref info) || info.hwndCaret == IntPtr.Zero) return null;

        var point = new Win32.POINT(info.rcCaret.Left, info.rcCaret.Bottom);
        if (!Win32.ClientToScreen(info.hwndCaret, ref point)) return null;
        return new Point(point.X, point.Y);
    }

    private static bool UiaEditable(Point screenPoint)
    {
        try
        {
            AutomationElement? element = AutomationElement.FromPoint(screenPoint);
            // 浏览器里点到的是文本子节点，向上找几层。
            for (int depth = 0; depth < 4 && element is not null; depth++)
            {
                if (IsEditable(element)) return true;
                element = TreeWalker.ControlViewWalker.GetParent(element);
            }
        }
        catch (ElementNotAvailableException) { }
        catch (COMException) { }
        return false;
    }

    private static bool IsEditable(AutomationElement element)
    {
        try
        {
            if (element.Current.ControlType == ControlType.Edit) return true;
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? pattern)
                && pattern is ValuePattern value && !value.Current.IsReadOnly)
            {
                return true;
            }
        }
        catch (ElementNotAvailableException) { }
        catch (COMException) { }
        return false;
    }

    private static Point? FromUiaTextPattern()
    {
        try
        {
            AutomationElement? focused = AutomationElement.FocusedElement;
            if (focused is null || !focused.TryGetCurrentPattern(TextPattern.Pattern, out object? pattern)) return null;

            var ranges = ((TextPattern)pattern).GetSelection();
            if (ranges.Length == 0) return null;

            var rects = ranges[^1].GetBoundingRectangles();
            if (rects.Length == 0) return null;
            return new Point(rects[^1].Left, rects[^1].Bottom);
        }
        catch (ElementNotAvailableException) { return null; }
        catch (COMException) { return null; }
    }

    private static T RunWithTimeout<T>(Func<T> work, TimeSpan timeout, T fallback)
    {
        try
        {
            Task<T> task = Task.Run(work);
            return task.Wait(timeout) ? task.Result : fallback;
        }
        catch (AggregateException)
        {
            return fallback;
        }
    }
}