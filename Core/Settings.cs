using System.Text.Json;

namespace Copeer.Core;

public class Settings
{
    public string NeuroooApiKey { get; set; } = "";
    public string TargetLanguage { get; set; } = "ru";
    public string Engine { get; set; } = "";
    public int HistoryLimit { get; set; } = 500;
    public int PasteRestoreDelayMs { get; set; } = 1500;
    public List<string> RecentSymbols { get; set; } = new();

    public bool CaptureImages { get; set; } = true;
    public bool CaptureFiles { get; set; } = true;
    public int MaxTextLength { get; set; } = 500_000;
    public bool LeavePastedInClipboard { get; set; } = false;
    public string PanelPosition { get; set; } = "cursor"; // cursor | center
    public string Theme { get; set; } = "dark";           // dark | light
    public bool Autostart { get; set; } = false;          // зеркало реестра, источник правды — Autostart.Enabled

    // OCR после скриншота: "" (выключено) | "windows" (офлайн) | "gcv" (Google Cloud Vision, нужен ключ)
    public string OcrEngine { get; set; } = "";
    public string OcrGcvKey { get; set; } = "";

    // PrintScreen (с любыми модификаторами) открывает скриншот Copeer вместо системного
    public bool InterceptPrintScreen { get; set; } = true;

    // плашка после снимка: секунд до скрытия, когда курсор не на ней.
    // 0 — скрыть сразу, -1 — висит, пока пользователь не закроет
    public int SnipToastTimeoutSec { get; set; } = 30;
    public bool SnipToastRememberTimeout { get; set; } = true;

    public static string AppDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Copeer");
    public static string SettingsPath => Path.Combine(AppDir, "settings.json");
    public string ImagesDir => Path.Combine(AppDir, "images");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath));
                if (s != null) return s;
            }
        }
        catch (Exception e) { Log.Error("settings load", e); }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppDir);
            File.WriteAllText(SettingsPath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { Log.Error("settings save", e); }
    }
}

public static class Log
{
    static readonly object Gate = new();

    public static void Info(string msg) => Write("INFO", msg, null);
    public static void Error(string msg, Exception? e = null) => Write("ERROR", msg, e);

    static void Write(string level, string msg, Exception? e)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Settings.AppDir);
                var path = Path.Combine(Settings.AppDir, "copeer.log");
                RotateIfNeeded(path);
                File.AppendAllText(path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {msg}{(e == null ? "" : " :: " + e)}{Environment.NewLine}");
            }
        }
        catch { }
    }

    // трей-приложение живёт неделями — лог держим в пределах ~4 МБ (одна .old-поколение)
    static void RotateIfNeeded(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length < 2_000_000) return;
            var old = path + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(path, old);
        }
        catch { }
    }
}
