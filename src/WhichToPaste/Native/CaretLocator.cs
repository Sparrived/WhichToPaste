using System.Runtime.InteropServices;
using System.Windows.Automation;
using Point = System.Windows.Point;

namespace WhichToPaste.Native;

/// <summary>
/// 判断右键落点是不是可编辑控件。UIA 调用可能被目标进程拖住，一律带超时，
/// 超时就退回 Win32 的插入符信息。
/// </summary>
internal static class CaretLocator
{
    public static bool IsEditableAt(Point screenPoint)
    {
        bool? editable = RunWithTimeout(() => UiaEditable(screenPoint), TimeSpan.FromMilliseconds(400), (bool?)null);
        return editable ?? HasCaret();
    }

    /// <summary>前台窗口此刻有没有插入符——有就说明焦点在可编辑控件里。</summary>
    private static bool HasCaret()
    {
        IntPtr foreground = Win32.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;

        uint threadId = Win32.GetWindowThreadProcessId(foreground, out _);
        var info = new Win32.GUITHREADINFO { cbSize = Marshal.SizeOf<Win32.GUITHREADINFO>() };
        return Win32.GetGUIThreadInfo(threadId, ref info) && info.hwndCaret != IntPtr.Zero;
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
