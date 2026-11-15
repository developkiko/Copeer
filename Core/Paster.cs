namespace Copeer.Core;

/// <summary>
/// Вставка содержимого в предыдущее активное окно:
/// сохраняем текущий буфер, кладём нужное, шлём Ctrl+V, затем возвращаем старый буфер.
/// Возврат отменяется, если пользователь за это время успел скопировать что-то своё.
/// </summary>
public static class Paster
{
    // номер текущей вставки: быстрые вставки подряд не должны
    // перебивать Ctrl+V и возврат буфера друг у друга
    static int _generation;

    public static void Paste(AppContext ctx, Entry entry)
    {
        try
        {
            switch (entry.Kind)
            {
                case EntryKind.Text:
                    _ = PasteTextAsync(ctx, entry.Text);
                    break;
                case EntryKind.Files:
                    _ = PasteFilesAsync(ctx, entry.Text);
                    break;
                default:
                    using (var full = LoadBitmap(entry))
                        if (full != null)
                            _ = PasteBitmapAsync(ctx, (Bitmap)full.Clone());
                    break;
            }
        }
        catch (Exception e) { Log.Error("paste", e); }
    }

    public static void PasteText(AppContext ctx, string text)
    {
        try { _ = PasteTextAsync(ctx, text); }
        catch (Exception e) { Log.Error("paste text", e); }
    }

    /// <summary>Вставка картинки (окно скриншота): битмап не диспоузится — живёт в DataObject на клипборде.</summary>
    public static void PasteBitmap(AppContext ctx, Bitmap bmp)
    {
        try { _ = PasteBitmapAsync(ctx, bmp); }
        catch (Exception e) { Log.Error("paste bitmap", e); }
    }

    /// <summary>Вставка готового DataObject (например, HTML с локальной картинкой для GIF-панели).</summary>
    public static void PasteDataObject(AppContext ctx, DataObject data)
    {
        try { _ = PasteDataAsync(ctx, data); }
        catch (Exception e) { Log.Error("paste data", e); }
    }

    /// <summary>
    /// Вставка нескольких записей: тексты — через \n, картинки — в HTML-формате
    /// (приложение вставит их, если умеет; иначе останется только текст), файлы — FileDrop.
    /// </summary>
    public static void PasteMulti(AppContext ctx, IReadOnlyList<Entry> entries)
    {
        try { _ = PasteMultiAsync(ctx, entries); }
        catch (Exception e) { Log.Error("paste multi", e); }
    }

    static async Task PasteTextAsync(AppContext ctx, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var data = new DataObject();
        data.SetText(text);
        await PasteDataAsync(ctx, data);
    }

    static async Task PasteFilesAsync(AppContext ctx, string pathsText)
    {
        var paths = pathsText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (paths.Length == 0) return;
        await PasteDataAsync(ctx, FilesDataObject(paths));
    }

    static async Task PasteBitmapAsync(AppContext ctx, Bitmap bmp)
    {
        var data = new DataObject();
        data.SetImage(bmp);
        await PasteDataAsync(ctx, data);
    }

    static async Task PasteMultiAsync(AppContext ctx, IReadOnlyList<Entry> entries)
    {
        if (entries.Count == 0) return;
        if (entries.Count == 1) { Paste(ctx, entries[0]); return; }

        var data = new DataObject();
        var textLines = new List<string>();
        var htmlParts = new List<string>();
        var filePaths = new List<string>();
        bool anyImage = false;

        foreach (var e in entries)
        {
            if (e.Kind == EntryKind.Image)
            {
                if (e.ImageFile == null || !File.Exists(e.ImageFile)) continue;
                anyImage = true;
                var url = HtmlClipboard.FileUrl(e.ImageFile);
                htmlParts.Add($"<img src=\"{url}\" width=\"{Math.Max(1, e.W)}\" height=\"{Math.Max(1, e.H)}\">");
            }
            else
            {
                var t = e.Text.Replace("\r", "");
                textLines.Add(t);
                htmlParts.Add(HtmlClipboard.EscapeText(t));
                if (e.Kind == EntryKind.Files)
                    filePaths.AddRange(t.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
        }

        if (textLines.Count == 0 && !anyImage && filePaths.Count == 0) return;
        if (textLines.Count > 0) data.SetText(string.Join("\n", textLines));
        if (anyImage)
            data.SetData(DataFormats.Html, HtmlClipboard.Wrap(string.Join("\r\n", htmlParts)));
        if (filePaths.Count > 0)
            data.SetFileDropList(ToCollection(filePaths));

        await PasteDataAsync(ctx, data);
    }

    static DataObject FilesDataObject(string[] paths)
    {
        var data = new DataObject();
        data.SetFileDropList(ToCollection(paths.ToList()));
        return data;
    }

    static System.Collections.Specialized.StringCollection ToCollection(List<string> paths)
    {
        var list = new System.Collections.Specialized.StringCollection();
        list.AddRange(paths.ToArray());
        return list;
    }

    /// <summary>Общий скелет любой вставки: подмена буфера → активация таргета → Ctrl+V → возврат.</summary>
    static async Task PasteDataAsync(AppContext ctx, DataObject data)
    {
        var g = ++_generation;
        ctx.HidePanels();
        var prev = Snapshot();
        ctx.SuppressClipboard();
        try { Clipboard.SetDataObject(data, true); }
        catch (Exception e) { Log.Error("clipboard set", e); return; }
        var seq = Native.GetClipboardSequenceNumber();
        await ActivateTargetAsync(ctx);
        if (_generation != g) return;
        Native.SendCtrlV();
        await Task.Delay(ctx.S.PasteRestoreDelayMs);
        if (_generation != g) return;
        Restore(ctx, prev, seq);
    }

    static async Task ActivateTargetAsync(AppContext ctx)
    {
        await Task.Delay(150);
        var target = ctx.PasteTarget;
        // окно могли закрыть после открытия панели — тогда не дёргаем фокус,
        // Ctrl+V уйдёт в то, что реально в фокусе сейчас
        if (target == IntPtr.Zero || !Native.IsWindow(target) || target == Native.GetForegroundWindow()) return;

        Native.NudgeForeground();
        Native.SetForegroundWindow(target);
        await Task.Delay(120);
        if (target != Native.GetForegroundWindow())
        {
            // некоторые окна (проводник, UWP) с первого раза не поддаются
            Native.NudgeForeground();
            Native.SetForegroundWindow(target);
            await Task.Delay(120);
        }
    }

    static IDataObject? Snapshot()
    {
        try { return Clipboard.GetDataObject(); }
        catch { return null; }
    }

    static void Restore(AppContext ctx, IDataObject? prev, uint seqAfterPaste)
    {
        // режим «оставлять вставленное»: после вставки буфер не трогаем
        if (ctx.S.LeavePastedInClipboard) return;

        // номер изменения буфера не совпадает с тем, что был сразу после нашей вставки,
        // — пользователь уже скопировал что-то своё (любого типа), не затираем
        if (Native.GetClipboardSequenceNumber() != seqAfterPaste) return;

        ctx.SuppressClipboard();
        try
        {
            if (prev != null) Clipboard.SetDataObject(prev, true);
            else Clipboard.Clear();
        }
        catch { }
    }

    static Bitmap? LoadBitmap(Entry e)
    {
        try { return e.ImageFile == null ? null : new Bitmap(e.ImageFile); }
        catch (Exception ex) { Log.Error("image load", ex); return null; }
    }
}
