using System.Text.Json;

namespace Copeer.Core;

public enum EntryKind { Text, Image, Files }

public class Entry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public EntryKind Kind { get; set; }
    public string Text { get; set; } = "";
    public string? ImageFile { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    public DateTime Ts { get; set; } = DateTime.Now;
}

/// <summary>История буфера обмена: текст и изображения, с сохранением на диск.</summary>
public class ClipboardStore
{
    readonly Settings _s;
    readonly List<Entry> _items = new();
    readonly object _gate = new();

    public event Action? Changed;

    public ClipboardStore(Settings s)
    {
        _s = s;
        Load();
        CleanupOrphanImages();
    }

    public List<Entry> Items { get { lock (_gate) return _items.ToList(); } }

    public void AddText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.Length > _s.MaxTextLength)
        {
            Log.Info($"clipboard: текст {text.Length} симв. превышает лимит {_s.MaxTextLength} — не сохраняю");
            return;
        }
        lock (_gate)
        {
            if (_items.Count > 0 && _items[0].Kind == EntryKind.Text && _items[0].Text == text)
            {
                var top = _items[0];
                _items.RemoveAt(0);
                top.Ts = DateTime.Now;
                _items.Insert(0, top);
            }
            else
            {
                _items.Insert(0, new Entry { Kind = EntryKind.Text, Text = text });
                TrimLocked();
            }
            SaveSoonLocked();
        }
        Changed?.Invoke();
    }

    public void AddFiles(string[] files)
    {
        var paths = files.Where(f => File.Exists(f) || Directory.Exists(f)).ToArray();
        if (paths.Length == 0) return;
        var text = string.Join("\n", paths);
        lock (_gate)
        {
            if (_items.Count > 0 && _items[0].Kind == EntryKind.Files && _items[0].Text == text)
            {
                var top = _items[0];
                _items.RemoveAt(0);
                top.Ts = DateTime.Now;
                _items.Insert(0, top);
            }
            else
            {
                _items.Insert(0, new Entry { Kind = EntryKind.Files, Text = text });
                TrimLocked();
            }
            SaveSoonLocked();
        }
        Changed?.Invoke();
    }

    public void AddImage(Bitmap bmp)
    {
        try
        {
            Directory.CreateDirectory(_s.ImagesDir);
            var file = Path.Combine(_s.ImagesDir, Guid.NewGuid().ToString("N") + ".png");
            bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            lock (_gate)
            {
                _items.Insert(0, new Entry { Kind = EntryKind.Image, ImageFile = file, W = bmp.Width, H = bmp.Height });
                TrimLocked();
                SaveSoonLocked();
            }
            Changed?.Invoke();
        }
        catch (Exception e) { Log.Error("image save", e); }
    }

    public void Remove(Entry e)
    {
        lock (_gate)
        {
            _items.RemoveAll(x => x.Id == e.Id);
            if (e.ImageFile != null) TryDelete(e.ImageFile);
            SaveNowLocked();
        }
        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_gate)
        {
            foreach (var it in _items)
                if (it.ImageFile != null) TryDelete(it.ImageFile);
            _items.Clear();
            SaveNowLocked();
        }
        Changed?.Invoke();
    }

    void TrimLocked()
    {
        while (_items.Count > _s.HistoryLimit)
        {
            var last = _items[^1];
            _items.RemoveAt(_items.Count - 1);
            if (last.ImageFile != null) TryDelete(last.ImageFile);
        }
    }

    void TryDelete(string f)
    {
        try { if (File.Exists(f)) File.Delete(f); } catch { }
    }

    string HistPath => Path.Combine(Settings.AppDir, "history.json");

    // добавления копятся и пишутся отложенно (history.json может быть большим),
    // удаления — сразу, чтобы не «воскресали» после перезапуска
    bool _savePending;

    void SaveSoonLocked()
    {
        if (_savePending) return;
        _savePending = true;
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            lock (_gate)
            {
                _savePending = false;
                SaveNowLocked();
            }
        });
    }

    void SaveNowLocked()
    {
        try
        {
            Directory.CreateDirectory(Settings.AppDir);
            File.WriteAllText(HistPath, JsonSerializer.Serialize(_items));
        }
        catch (Exception e) { Log.Error("history save", e); }
    }

    void Load()
    {
        try
        {
            if (!File.Exists(HistPath)) return;
            var list = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(HistPath));
            if (list == null) return;
            lock (_gate)
            {
                _items.AddRange(list.Where(x =>
                    x.Kind == EntryKind.Text ||
                    x.Kind == EntryKind.Files ||
                    (x.Kind == EntryKind.Image && x.ImageFile != null && File.Exists(x.ImageFile))));
                TrimLocked();
            }
        }
        catch (Exception e) { Log.Error("history load", e); }
    }

    /// <summary>PNG без записи в истории (краш между сохранением файла и history.json) — в мусор.</summary>
    void CleanupOrphanImages()
    {
        try
        {
            if (!Directory.Exists(_s.ImagesDir)) return;
            HashSet<string> keep;
            lock (_gate)
                keep = new HashSet<string>(
                    _items.Where(x => x.ImageFile != null).Select(x => x.ImageFile!),
                    StringComparer.OrdinalIgnoreCase);
            foreach (var f in Directory.EnumerateFiles(_s.ImagesDir, "*.png"))
                if (!keep.Contains(f)) TryDelete(f);
        }
        catch (Exception e) { Log.Error("image cleanup", e); }
    }
}
