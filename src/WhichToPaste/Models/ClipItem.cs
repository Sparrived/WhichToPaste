using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WhichToPaste.Services;

namespace WhichToPaste.Models;

public enum ClipKind
{
    Text = 0,
    Files = 1,
    Image = 2,
}

/// <summary>一条剪贴板历史：文本、文件列表或位图。</summary>
public sealed class ClipItem
{
    private ImageSource? _thumbnail;

    public ClipKind Kind { get; set; }

    /// <summary>Kind=Text 时为文本本身。</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Kind=Files 时为完整路径列表。</summary>
    public string[]? Files { get; set; }

    /// <summary>Kind=Image 时为 images 目录下的文件名（内容哈希）。</summary>
    public string? ImageFile { get; set; }

    public int PixelWidth { get; set; }

    public int PixelHeight { get; set; }

    public DateTimeOffset CopiedAt { get; set; }

    /// <summary>0 表示最新一条，由界面在展开时写入，兼顾"新到旧"的视觉强弱。</summary>
    [JsonIgnore]
    public int Rank { get; set; }

    [JsonIgnore]
    public double RankOpacity => Math.Max(0.18, 1.0 - Rank * 0.16);

    /// <summary>去重键：同样的内容只留最新一条。</summary>
    [JsonIgnore]
    public string Key => Kind switch
    {
        ClipKind.Files => "F:" + string.Join('\n', Files ?? []),
        ClipKind.Image => "I:" + ImageFile,
        _ => "T:" + Text,
    };

    [JsonIgnore]
    public bool IsFiles => Kind == ClipKind.Files;

    [JsonIgnore]
    public bool IsImage => Kind == ClipKind.Image;

    [JsonIgnore]
    public bool IsMultiLine => Kind == ClipKind.Text && (Text.Contains('\n') || Text.Contains('\r'));

    /// <summary>主行文字。</summary>
    [JsonIgnore]
    public string Title => Kind switch
    {
        ClipKind.Files => Files switch
        {
            { Length: > 1 } many => $"{Path.GetFileName(many[0])} 等 {many.Length} 个文件",
            { Length: 1 } one => Path.GetFileName(one[0]),
            _ => "文件",
        },
        ClipKind.Image => $"{PixelWidth} × {PixelHeight} 像素",
        _ => Preview,
    };

    /// <summary>副行文字：文件条目显示所在目录。</summary>
    [JsonIgnore]
    public string Subtitle => Kind == ClipKind.Files && Files is { Length: > 0 } files
        ? Path.GetDirectoryName(files[0]) ?? string.Empty
        : string.Empty;

    [JsonIgnore]
    public string Preview
    {
        get
        {
            string flattened = string.Join(' ', Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            return flattened.Length > 300 ? flattened[..300] : flattened;
        }
    }

    /// <summary>图片条目的列表缩略图，按需解码，失败返回 null（文件可能已被清理）。</summary>
    [JsonIgnore]
    public ImageSource? Thumbnail =>
        IsImage && ImageFile is not null ? _thumbnail ??= ImageStore.Load(ImageFile, 96) : null;

    [JsonIgnore]
    public string RelativeTime
    {
        get
        {
            TimeSpan age = DateTimeOffset.Now - CopiedAt;
            return age switch
            {
                { TotalSeconds: < 45 } => "刚刚",
                { TotalMinutes: < 60 } => $"{(int)age.TotalMinutes} 分钟前",
                { TotalHours: < 24 } => $"{(int)age.TotalHours} 小时前",
                { TotalDays: < 30 } => $"{(int)age.TotalDays} 天前",
                _ => CopiedAt.LocalDateTime.ToString("yyyy-MM-dd"),
            };
        }
    }

    /// <summary>内容还在不在：图片条目可能因为清理而失效。</summary>
    [JsonIgnore]
    public bool IsUsable => Kind switch
    {
        ClipKind.Files => Files is { Length: > 0 },
        ClipKind.Image => ImageFile is not null && ImageStore.Exists(ImageFile),
        _ => !string.IsNullOrWhiteSpace(Text),
    };
}
