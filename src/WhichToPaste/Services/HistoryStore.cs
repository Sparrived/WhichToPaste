using System.Text.Json;
using WhichToPaste.Models;

namespace WhichToPaste.Services;

/// <summary>内存里的剪贴板历史（新→旧），并落盘到 history.json。</summary>
public sealed class HistoryStore
{
    private readonly List<ClipItem> _items = [];
    private readonly Lock _gate = new();
    private readonly Lock _writeGate = new();
    private readonly Settings _settings;
    private long _version;

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

    /// <summary>记录一次复制。内容与已有条目相同时只把它提到最前，不产生重复项。</summary>
    public bool Add(ClipItem item)
    {
        if (!item.IsUsable) return false;

        lock (_gate)
        {
            int existing = _items.FindIndex(i => i.Key == item.Key);
            if (existing == 0)
            {
                _items[0].CopiedAt = item.CopiedAt = DateTimeOffset.Now;
            }
            else
            {
                if (existing > 0) _items.RemoveAt(existing);
                item.CopiedAt = DateTimeOffset.Now;
                _items.Insert(0, item);
                Trim();
            }
        }

        // 入了历史，这个图片文件才算"有人引用"，之前它随时可能被清理掉。
        if (item.ImageFile is not null) ImageStore.Claim(item.ImageFile);

        Persist();
        return true;
    }

    public void Clear()
    {
        lock (_gate) _items.Clear();
        Persist();
        ImageStore.Clear();
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
                _items.AddRange(loaded.Where(i => i.IsUsable));
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

    /// <summary>当前历史里被引用的图片文件名。</summary>
    private List<string> ImageFilesNow()
    {
        lock (_gate) return [.. _items.Select(i => i.ImageFile).OfType<string>()];
    }

    private void Persist()
    {
        List<ClipItem> copy = Snapshot();
        long version = Interlocked.Increment(ref _version);

        Task.Run(() =>
        {
            // 落盘是异步的，可能有多份快照排队。用版本号保证只有最新的那份真正写下去，
            // 否则先排队的旧快照后完成，会把新内容覆盖掉（图片清理同理，会误删新文件）。
            lock (_writeGate)
            {
                if (version != Interlocked.Read(ref _version)) return;

                try
                {
                    Directory.CreateDirectory(SettingsStore.DataDirectory);
                    File.WriteAllText(FilePath, JsonSerializer.Serialize(copy));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }

                // 顺手清掉已经被裁掉的图片文件。这里取"当前"历史而不是这份快照，
                // 否则先排队的旧快照会把刚存进来的新图片删掉。
                ImageStore.Prune(ImageFilesNow());
            }
        });
    }
}
