using WhichToPaste.Native;

namespace WhichToPaste.UI;

/// <summary>
/// 选择框的摆放计算。只用物理像素算矩形、不碰窗口，所以能脱离界面直接自检。
/// </summary>
internal static class PickerPlacement
{
    /// <summary>卡片与鼠标指针之间的空隙（物理像素）。</summary>
    private const int Gap = 6;

    /// <summary>
    /// 以鼠标指针为基准摆放：优先摆在指针下方，下方放不下就翻到上方，再夹进工作区保证整框可见。
    /// 贴着屏幕边缘时"翻到上方"可能又被夹回来、正好把指针框在里面，这时改摆到指针的
    /// 下 / 上 / 右 / 左空侧。指针始终落在卡片之外，所以弹窗刚显示时鼠标不会指着它，
    /// 一动或一点也不会误选了别的条目。
    /// </summary>
    /// <param name="cursor">当前鼠标位置，物理像素。</param>
    /// <param name="width">窗口宽度，含四周阴影留白，物理像素。</param>
    /// <param name="height">窗口高度，含四周阴影留白，物理像素。</param>
    /// <param name="margin">阴影留白，物理像素。</param>
    /// <param name="work">指针所在显示器的工作区，物理像素。</param>
    public static (int X, int Y) Choose(Win32.POINT cursor, int width, int height, int margin, Win32.RECT work)
    {
        int maxX = Math.Max(work.Left, work.Right - width);
        int maxY = Math.Max(work.Top, work.Bottom - height);

        int y = cursor.Y + Gap - margin;
        if (y + height > work.Bottom) y = cursor.Y - Gap - height + margin;

        (int X, int Y) preferred =
            (Math.Clamp(cursor.X - margin, work.Left, maxX), Math.Clamp(y, work.Top, maxY));

        if (!CursorOnCard(cursor, preferred, width, height, margin)) return preferred;

        // 指针当道：依次试指针的下、上、右、左，各夹进工作区后看还压不压指针。
        // 这四个方向是"躲开指针"的全部可能（框要么在指针上/下，要么在指针左/右），
        // 所以只要存在既完整可见、又不压指针的摆法，这里就一定找得到。
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

        return preferred; // 框比工作区的一半还大这类极端情况：四个方向都躲不开，维持原位总比乱跳好
    }

    /// <summary>鼠标是否落在框的可见部分上（窗口矩形去掉四周的阴影留白）。</summary>
    private static bool CursorOnCard(Win32.POINT cursor, (int X, int Y) position, int width, int height, int margin) =>
        cursor.X >= position.X + margin && cursor.X < position.X + width - margin
        && cursor.Y >= position.Y + margin && cursor.Y < position.Y + height - margin;
}
