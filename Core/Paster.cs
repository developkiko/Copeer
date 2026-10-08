namespace Copeer.Core;

/// <summary>
/// Вставка содержимого в предыдущее активное окно:
/// сохраняем текущий буфер, кладём нужное, шлём Ctrl+V, затем возвращаем старый буфер.
/// Возврат отменяется, если пользователь за это время успел скопировать что-то своё.
/// </summary>
public static class Paster
{
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
                            _ = PasteImageAsync(ctx, (Bitmap)full.Clone());
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

    static async Task PasteTextAsync(AppContext ctx, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        ctx.HidePanels();
        var prev = Snapshot();
        ctx.SuppressClipboard();
        try { Clipboard.SetText(text); }
        catch (Exception e) { Log.Error("clipboard set text", e); return; }
        var seq = Native.GetClipboardSequenceNumber();
        await ActivateTargetAsync(ctx);
        Native.SendCtrlV();
        await Task.Delay(ctx.S.PasteRestoreDelayMs);
        Restore(ctx, prev, seq);
    }

    static async Task PasteFilesAsync(AppContext ctx, string pathsText)
    {
        var paths = pathsText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (paths.Length == 0) return;
        ctx.HidePanels();
        var prev = Snapshot();
        ctx.SuppressClipboard();
        try
        {
            var list = new System.Collections.Specialized.StringCollection();
            list.AddRange(paths);
            var data = new DataObject();
            data.SetFileDropList(list);
            Clipboard.SetDataObject(data, true);
        }
        catch (Exception e) { Log.Error("clipboard set files", e); return; }
        var seq = Native.GetClipboardSequenceNumber();
        await ActivateTargetAsync(ctx);
        Native.SendCtrlV();
        await Task.Delay(ctx.S.PasteRestoreDelayMs);
        Restore(ctx, prev, seq);
    }

    static async Task PasteImageAsync(AppContext ctx, Bitmap bmp)
    {
        ctx.HidePanels();
        var prev = Snapshot();
        ctx.SuppressClipboard();
        try
        {
            var data = new DataObject();
            data.SetImage(bmp);
            Clipboard.SetDataObject(data, true);
        }
        catch (Exception e) { Log.Error("clipboard set image", e); return; }
        var seq = Native.GetClipboardSequenceNumber();
        await ActivateTargetAsync(ctx);
        Native.SendCtrlV();
        await Task.Delay(ctx.S.PasteRestoreDelayMs);
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
