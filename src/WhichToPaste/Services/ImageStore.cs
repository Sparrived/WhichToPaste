using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WhichToPaste.Services;

/// <summary>
/// 图片条目的落盘：位图编码成 PNG 存在 <c>%APPDATA%\WhichToPaste\images</c>，
/// 文件名取内容哈希，于是同一张图重复复制不会堆文件。
/// </summary>
internal static class ImageStore
{
    public static string Folder => Path.Combine(SettingsStore.DataDirectory, "images");

    public static string PathOf(string fileName) => Path.Combine(Folder, fileName);

    public static bool Exists(string fileName)
    {
        try
        {
            return File.Exists(PathOf(fileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>编码并存盘，返回文件名与像素尺寸；失败返回 null。</summary>
    public static (string File, int Width, int Height)? Save(BitmapSource source)
    {
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var buffer = new MemoryStream();
            encoder.Save(buffer);

            string name = Convert.ToHexString(SHA256.HashData(buffer.ToArray()))[..16] + ".png";
            Directory.CreateDirectory(Folder);
            string path = PathOf(name);
            if (!File.Exists(path)) File.WriteAllBytes(path, buffer.ToArray());

            return (name, source.PixelWidth, source.PixelHeight);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>读取 PNG。<paramref name="decodeWidth"/> 大于 0 时按缩略图尺寸解码。</summary>
    public static BitmapImage? Load(string fileName, int decodeWidth = 0)
    {
        try
        {
            string path = PathOf(fileName);
            if (!File.Exists(path)) return null;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>删掉不再被历史引用的图片文件。</summary>
    public static void Prune(IReadOnlyCollection<string> keep)
    {
        try
        {
            if (!Directory.Exists(Folder)) return;

            var alive = new HashSet<string>(keep, StringComparer.OrdinalIgnoreCase);
            foreach (string path in Directory.EnumerateFiles(Folder, "*.png"))
            {
                if (!alive.Contains(Path.GetFileName(path))) File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static void Clear()
    {
        try
        {
            if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>把剪贴板位图变成冻结副本，之后可以在后台线程编码。</summary>
    public static BitmapSource Freeze(BitmapSource source)
    {
        var copy = new WriteableBitmap(new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0));
        copy.Freeze();
        return copy;
    }
}
