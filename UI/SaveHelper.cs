using Copeer.Core;

namespace Copeer.UI;

/// <summary>
/// «В файл»: стандартный диалог сохранения Windows в режиме All files — расширение
/// можно указать своё или не указывать вовсе. Текст пишется в выбранный файл;
/// картинки и файловые записи из выделения сохраняются рядом с ним:
/// &lt;имя файла&gt;-img1.png, &lt;имя файла&gt;-file1.ext, …
/// </summary>
static class SaveHelper
{
    /// <summary>Сохранение выделенных записей истории. Возвращает true, если файл записан.</summary>
    public static bool SaveEntries(IWin32Window owner, IReadOnlyList<Entry> entries)
    {
        if (entries.Count == 0) return false;

        var path = AskPath(owner);
        if (path == null) return false;

        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir)) dir = Settings.AppDir;
        var baseName = Path.GetFileNameWithoutExtension(path);
        if (baseName.Length == 0) baseName = "copeer";

        var texts = new List<string>();
        int img = 0, file = 0;
        foreach (var e in entries)
        {
            if (e.Kind == EntryKind.Image)
            {
                if (e.ImageFile == null || !File.Exists(e.ImageFile)) continue;
                img++;
                TryCopy(e.ImageFile, Path.Combine(dir, $"{baseName}-img{img}.png"));
            }
            else if (e.Kind == EntryKind.Files)
            {
                // одна запись = один или несколько путей из проводника; расширение сохраняется
                foreach (var p in e.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    file++;
                    TryCopy(p, Path.Combine(dir, $"{baseName}-file{file}{Path.GetExtension(p)}"));
                }
            }
            else
            {
                texts.Add(e.Text.Replace("\r", ""));
            }
        }

        // сам файл — только текст, без картинок и файлов
        File.WriteAllText(path, string.Join("\n", texts));
        return true;
    }

    /// <summary>Сохранение просто текста (окно распознавания, переводчик).</summary>
    public static bool SaveText(IWin32Window owner, string text)
    {
        var path = AskPath(owner);
        if (path == null) return false;
        File.WriteAllText(path, text);
        return true;
    }

    static string? AskPath(IWin32Window owner)
    {
        using var dlg = new SaveFileDialog
        {
            Title = "Сохранить как",
            Filter = "All files (*.*)|*.*",
            AddExtension = false,
            OverwritePrompt = true
        };
        return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.FileName : null;
    }

    static void TryCopy(string src, string dst)
    {
        try { File.Copy(src, dst, overwrite: true); }
        catch (Exception e) { Log.Error($"save file → {dst}", e); }
    }
}
