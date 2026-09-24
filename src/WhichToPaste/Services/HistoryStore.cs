using System.Text.Json;
using WhichToPaste.Models;

namespace WhichToPaste.Services;

/// <summary>内存里的剪贴板历史（新→旧），并落盘到 history.json。</summary>
public sealed class HistoryStore
{
    private readonly List<ClipItem> _items = [];
    private readonly Lock _gate = new();
    private readonly Settings _settings;

    public HistoryStore(Settings settings) => _settings = settings;

    public static string FilePath => Path.Combine(SettingsStore.DataDirectory, "history.json");

    public bool IsEmpty
    {
        get { lock (_gate) return _items.Count == 0; }
    }

    public List<ClipItem> Snapshot()
    {
        lock (_gate) return [.. _items];
    }

    /// <summary>记录一次复制。内容与最新一条相同时只刷新时间，不产生重复项。</summary>
    public bool Add(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();

        lock (_gate)
        {
            int existing = _items.FindIndex(i => i.Text == text);
            if (existing == 0)
            {
                _items[0].CopiedAt = DateTimeOffset.Now;
                return true;
            }
            if (existing > 0) _items.RemoveAt(existing);

            _items.Insert(0, new ClipItem { Text = text, CopiedAt = DateTimeOffset.Now });
            Trim();
        }

        Persist();
        return true;
    }

    public void Clear()
    {
        lock (_gate) _items.Clear();
        Persist();
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var loaded = JsonSerializer.Deserialize<List<ClipItem>>(File.ReadAllText(FilePath));
            if (loaded is null) return;

            lock (_gate)
            {
                _items.Clear();
                _items.AddRange(loaded.Where(i => !string.IsNullOrWhiteSpace(i.Text)));
                Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
    }

    private void Trim()
    {
        int limit = Math.Clamp(_settings.HistoryLimit, 1, 200);
        if (_items.Count > limit) _items.RemoveRange(limit, _items.Count - limit);
    }

    private void Persist()
    {
        List<ClipItem> copy = Snapshot();
        Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(SettingsStore.DataDirectory);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(copy));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        });
    }
}