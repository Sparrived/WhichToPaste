using System.Text.Json.Serialization;

namespace WhichToPaste.Models;

/// <summary>一条剪贴板历史。只收文本，图片/文件暂不记录。</summary>
public sealed class ClipItem
{
    public string Text { get; set; } = string.Empty;

    public DateTimeOffset CopiedAt { get; set; }

    /// <summary>0 表示最新一条，由界面在展开时写入，兼顾"新到旧"的视觉强弱。</summary>
    [JsonIgnore]
    public int Rank { get; set; }

    [JsonIgnore]
    public double RankOpacity => Math.Max(0.18, 1.0 - Rank * 0.16);

    [JsonIgnore]
    public bool IsMultiLine => Text.Contains('\n') || Text.Contains('\r');

    [JsonIgnore]
    public string Preview
    {
        get
        {
            string flattened = string.Join(' ', Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            return flattened.Length > 300 ? flattened[..300] : flattened;
        }
    }

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
}