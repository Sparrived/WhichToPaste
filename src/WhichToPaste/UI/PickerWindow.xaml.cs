using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WhichToPaste.Models;
using WhichToPaste.Native;
using WhichToPaste.Services;
using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace WhichToPaste.UI;

/// <summary>
/// 贴鼠标指针弹出的选择框。它永远不抢焦点（WS_EX_NOACTIVATE），
/// 键盘操作全靠全局钩子转发进来，这样原输入框的插入符不会丢。
/// </summary>
public partial class PickerWindow : Window
{
    private const double ShadowMargin = 14;
    private ScrollViewer? _scroll;

    public event Action<ClipItem>? ItemChosen;

    public PickerWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        ItemList.MouseMove += OnListMouseMove;
        ItemList.PreviewMouseLeftButtonUp += OnListMouseUp;
        PreviewMouseWheel += OnWheel;

        MaxHeight = Math.Max(280, SystemParameters.WorkArea.Height - 40);
    }

    public ClipItem? SelectedItem
    {
        get => ItemList.SelectedItem as ClipItem;
        set => ItemList.SelectedItem = value;
    }

    public void ShowItems(IReadOnlyList<ClipItem> items, Settings settings)
    {
        ItemList.ItemsSource = items;

        bool empty = items.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ItemList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        CountText.Text = empty ? string.Empty : $"共 {items.Count} 条";

        ItemList.SelectedIndex = empty ? -1 : 0;
        BuildHints(settings);

        Show();
        UpdateLayout();
        PositionAtCursor();
    }

    public void MoveSelection(int delta)
    {
        int count = ItemList.Items.Count;
        if (count == 0) return;

        int index = Math.Clamp(ItemList.SelectedIndex + delta, 0, count - 1);
        ItemList.SelectedIndex = index;
        if (ItemList.SelectedItem is not null) ItemList.ScrollIntoView(ItemList.SelectedItem);
    }

    internal bool ContainsScreenPoint(Win32.POINT point)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !Win32.GetWindowRect(handle, out Win32.RECT rect)) return false;
        return point.X >= rect.Left && point.X <= rect.Right && point.Y >= rect.Top && point.Y <= rect.Bottom;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        IntPtr exStyle = Win32.GetWindowLongPtrW(handle, Win32.GWL_EXSTYLE);
        Win32.SetWindowLongPtrW(handle, Win32.GWL_EXSTYLE,
            (IntPtr)(exStyle.ToInt64() | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW));
    }

    private void PositionAtCursor()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !Win32.GetCursorPos(out Win32.POINT cursor)) return;

        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int width = (int)Math.Ceiling(ActualWidth * scale);
        int height = (int)Math.Ceiling(ActualHeight * scale);
        int margin = (int)Math.Round(ShadowMargin * scale);

        (int x, int y) = PickerPlacement.Choose(cursor, width, height, margin, WorkAreaAt(cursor));

        Win32.SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0,
            Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    private static Win32.RECT WorkAreaAt(Win32.POINT point)
    {
        var monitor = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
        IntPtr handle = Win32.MonitorFromPoint(point, Win32.MONITOR_DEFAULTTONEAREST);

        if (handle != IntPtr.Zero && Win32.GetMonitorInfoW(handle, ref monitor)) return monitor.rcWork;

        return new Win32.RECT
        {
            Left = 0,
            Top = 0,
            Right = (int)SystemParameters.PrimaryScreenWidth,
            Bottom = (int)SystemParameters.PrimaryScreenHeight,
        };
    }

    private void OnListMouseMove(object sender, MouseEventArgs e)
    {
        if (ContainerAt(e.OriginalSource) is { } container && !ReferenceEquals(container, ItemList.SelectedItem))
            ItemList.SelectedItem = container;
    }

    private void OnListMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (ContainerAt(e.OriginalSource) is ClipItem item)
        {
            e.Handled = true;
            ItemChosen?.Invoke(item);
        }
    }

    private ClipItem? ContainerAt(object source)
    {
        if (source is not DependencyObject dependency) return null;
        return ItemsControl.ContainerFromElement(ItemList, dependency) is ListBoxItem row
            ? row.DataContext as ClipItem
            : null;
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        _scroll ??= FindScrollViewer(ItemList);
        if (_scroll is null) return;

        _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset - e.Delta / 3.0);
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer) return viewer;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        }
        return null;
    }

    private void BuildHints(Settings settings)
    {
        HintBar.Children.Clear();
        AddHint(settings.Up.ToString(), "上移");
        AddHint(settings.Down.ToString(), "下移");
        AddHint(settings.Paste.ToString(), "粘贴选中");
        AddHint(settings.Cancel.ToString(), "关闭");
    }

    private void AddHint(string key, string label)
    {
        HintBar.Children.Add(new Border
        {
            Background = (Brush)FindResource("SurfaceContainerHighBrush"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = key,
                FontFamily = (FontFamily)FindResource("MonoFont"),
                FontSize = 11,
                Foreground = (Brush)FindResource("OnSurfaceBrush"),
            },
        });

        HintBar.Children.Add(new TextBlock
        {
            Text = label,
            FontFamily = (FontFamily)FindResource("UiFont"),
            FontSize = 11.5,
            Margin = new Thickness(6, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("OnSurfaceVariantBrush"),
        });
    }
}