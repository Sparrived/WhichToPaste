using System.Windows;
using WhichToPaste.Native;
using Point = System.Windows.Point;

namespace WhichToPaste.UI;

/// <summary>
/// 选择框的摆放计算。只用物理像素算矩形、不碰窗口，所以能脱离界面直接自检。
/// </summary>
internal static class PickerPlacement
{
    /// <summary>卡片与插入符 / 鼠标之间的空隙（物理像素）。</summary>
    private const int Gap = 6;

    /// <summary>
    /// 先贴插入符：默认摆在下方，下方放不下就翻到上方，再夹进工作区保证整框可见。
    /// 若这样摆会让鼠标落在框上——插入符离鼠标较远、或贴到屏幕边缘被夹回来时都会发生——
    /// 就改摆到鼠标的下方 / 上方 / 右侧 / 左侧：宁可离插入符远一点，也不让鼠标指着框，
    /// 免得刚弹出来就被鼠标指到，一动或一点就误选了别的条目。
    /// </summary>
    /// <param name="anchor">插入符（右键时是落点）位置，物理像素。</param>
    /// <param name="cursor">当前鼠标位置，物理像素。</param>
    /// <param name="width">窗口宽度，含四周阴影留白，物理像素。</param>
    /// <param name="height">窗口高度，含四周阴影留白，物理像素。</param>
    /// <param name="margin">阴影留白，物理像素。</param>
    /// <param name="work">所在显示器的工作区，物理像素。</param>
    public static (int X, int Y) Choose(
        Point anchor, Win32.POINT cursor, int width, int height, int margin, Win32.RECT work)
    {
        int maxX = Math.Max(work.Left, work.Right - width);
        int maxY = Math.Max(work.Top, work.Bottom - height);

        int y = (int)anchor.Y + Gap - margin;
        if (y + height > work.Bottom) y = (int)anchor.Y - Gap - height + margin;

        (int X, int Y) preferred =
            (Math.Clamp((int)anchor.X - margin, work.Left, maxX), Math.Clamp(y, work.Top, maxY));

        if (!CursorOnCard(cursor, preferred, width, height, margin)) return preferred;

        // 鼠标当道：依次试鼠标的下、上、右、左，各夹进工作区后看还压不压鼠标。
        // 这四个方向是"躲开鼠标"的全部可能（框要么在鼠标上/下，要么在鼠标左/右），
        // 所以只要存在既完整可见、又不压鼠标的摆法，这里就一定找得到。
        (int X, int Y)[] escapes =
        [
            (preferred.X, cursor.Y + Gap - margin),
            (preferred.X, cursor.Y - Gap - height + margin),
            (cursor.X + Gap - margin, preferred.Y),
            (cursor.X - Gap - width + margin, preferred.Y),
        ];

        foreach ((int X, int Y) escape in escapes)
        {
            (int X, int Y) spot = (Math.Clamp(escape.X, work.Left, maxX), Math.Clamp(escape.Y, work.Top, maxY));
            if (!CursorOnCard(cursor, spot, width, height, margin)) return spot;
        }

        return preferred; // 框比工作区还大这类极端情况：四个方向都躲不开，维持原位总比乱跳好
    }

    /// <summary>鼠标是否落在框的可见部分上（窗口矩形去掉四周的阴影留白）。</summary>
    private static bool CursorOnCard(Win32.POINT cursor, (int X, int Y) position, int width, int height, int margin) =>
        cursor.X >= position.X + margin && cursor.X < position.X + width - margin
        && cursor.Y >= position.Y + margin && cursor.Y < position.Y + height - margin;
}
