using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Drawing.Color;
using DrawingIcon = System.Drawing.Icon;
using DrawingPoint = System.Drawing.Point;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace WhichToPaste.UI;

/// <summary>运行时画出来的应用图标：紫底 + 白色剪贴板，避免往仓库塞二进制资源。</summary>
internal static class AppIcon
{
    private const int Size = 64;
    private static readonly Color Primary = Color.FromArgb(0x67, 0x50, 0xA4);

    private static DrawingIcon? _icon;
    private static IntPtr _handle;
    private static ImageSource? _source;

    public static DrawingIcon Tray => _icon ??= Build();

    public static ImageSource Mark => _source ??= BuildImageSource();

    private static DrawingIcon Build()
    {
        using var bitmap = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var background = new SolidBrush(Primary);
            using var shell = Rounded(new RectangleF(1, 1, Size - 2, Size - 2), 17);
            g.FillPath(background, shell);

            using var white = new SolidBrush(Color.White);
            using var clip = Rounded(new RectangleF(25, 10, 14, 10), 4);
            g.FillPath(white, clip);
            using var body = Rounded(new RectangleF(15, 15, 34, 38), 7);
            g.FillPath(white, body);

            using var lines = new SolidBrush(Primary);
            g.FillRectangle(lines, 23, 28, 18, 3);
            g.FillRectangle(lines, 23, 35, 18, 3);
            g.FillRectangle(lines, 23, 42, 11, 3);
        }

        // HICON 由本进程持有到退出，交给 Icon 包装即可。
        _handle = bitmap.GetHicon();
        return DrawingIcon.FromHandle(_handle);
    }

    private static ImageSource BuildImageSource()
    {
        ImageSource source = Imaging.CreateBitmapSourceFromHIcon(
            Tray.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }

    private static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}