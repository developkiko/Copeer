using Copeer.Core;
using Copeer.UI;

namespace Copeer;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "Copeer.SingleInstance", out var isNew);
        if (!isNew) return;

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new AppContext());
    }
}

public class AppContext : ApplicationContext
{
    public Settings S = null!;
    public ClipboardStore Store = null!;
    public Icon AppIcon = null!; // достаём из exe — одна иконка везде (трей, окна, проводник)

    HotkeyHost host = null!;
    NotifyIcon tray = null!;

    HistoryPanel? history;
    EmojiPanel? emoji;
    TranslatorForm? translator;
    SnipOverlay? snip;
    SnipToast? snipToast;
    KeyboardHook? hook;

    DateTime _suppressUntil;
    bool[] _hotkeyOwned = new bool[5];
    public IntPtr PasteTarget { get; private set; }

    public AppContext()
    {
        S = Settings.Load();
        Theme.Set(S.Theme);
        Store = new ClipboardStore(S);

        host = new HotkeyHost();
        host.Hotkey += OnHotkey;
        host.ClipboardChanged += OnClipboardChanged;
        host.TaskbarRecreated += () => { tray.Visible = false; tray.Visible = true; };
        _ = host.Handle; // создаём окно сразу

        AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        tray = new NotifyIcon
        {
            Icon = AppIcon,
            Text = "Copeer — мультибуфер",
            Visible = true
        };
        tray.ContextMenuStrip = BuildMenu();
        tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowHistory(); };

        RegisterHotkey(1, Native.MOD_WIN, Native.VK_V, "Win+V (история буфера)");
        RegisterHotkey(2, Native.MOD_WIN, Native.VK_OEM_PERIOD, "Win+. (эмодзи и символы)");
        RegisterHotkey(3, Native.MOD_WIN | Native.MOD_SHIFT, Native.VK_S, "Win+Shift+S (скриншот)");
        RegisterHotkey(4, Native.MOD_WIN, Native.VK_T, "Win+T (переводчик)");

        // комбинации, которые не удалось занять через RegisterHotKey (держит сама Windows),
        // перехватываем низкоуровневым хуком; обработка уходит из колбэка хука в очередь хоста
        hook = new KeyboardHook { Sync = host };
        hook.InterceptSnapshot = S.InterceptPrintScreen;
        hook.ComboHit = vk =>
        {
            switch ((uint)vk)
            {
                case KeyboardHook.VK_PERIOD: ShowEmoji(); break;
                case KeyboardHook.VK_S: ShowSnip(); break;
                case KeyboardHook.VK_T: ShowTranslator(); break;
                case 0x2C: ShowSnip(); break; // PrintScreen
            }
        };
        // клавиши, успешно занятые через RegisterHotKey, хук не трогает (иначе двойное срабатывание);
        // остальные — ровно те, что держит Windows — перехватывает хук
        if (_hotkeyOwned[1]) hook.Skip(KeyboardHook.VK_V);
        if (_hotkeyOwned[2]) hook.Skip(KeyboardHook.VK_PERIOD);
        if (_hotkeyOwned[3]) hook.Skip(KeyboardHook.VK_S);
        if (_hotkeyOwned[4]) hook.Skip(KeyboardHook.VK_T);

        tray.ShowBalloonTip(4000, "Copeer запущен",
            "Win+V — история буфера\nWin+. — эмодзи и символы\nWin+Shift+S — скриншот\nWin+T — переводчик",
            ToolTipIcon.Info);
    }

    void RegisterHotkey(int id, uint mods, uint vk, string name)
    {
        if (Native.RegisterHotKey(host.Handle, id, mods, vk))
        {
            _hotkeyOwned[id] = true;
            Log.Info($"hotkey OK: {name}");
        }
        else
        {
            Log.Error($"hotkey BUSY: {name} — попробую низкоуровневый перехват");
        }
    }

    void OnHotkey(int id)
    {
        try
        {
            switch (id)
            {
                case 1: ShowHistory(); break;
                case 2: ShowEmoji(); break;
                case 3: ShowSnip(); break;
                case 4: ShowTranslator(); break;
            }
        }
        catch (Exception ex) { Log.Error("hotkey dispatch", ex); }
    }

    // ——— команды ———

    public void CapturePasteTarget() => PasteTarget = Native.GetForegroundWindow();

    public void SuppressClipboard(double seconds = 2.0) => _suppressUntil = DateTime.UtcNow.AddSeconds(seconds);

    public void HidePanels()
    {
        history?.Hide();
        emoji?.Hide();
        snip?.Hide();
    }

    void ShowHistory()
    {
        Log.Info("show: history");
        CapturePasteTarget();
        history ??= new HistoryPanel(this);
        history.Open();
    }

    void ShowEmoji()
    {
        Log.Info("show: emoji");
        CapturePasteTarget();
        emoji ??= new EmojiPanel(this);
        emoji.Open();
    }

    void ShowSnip()
    {
        Log.Info("show: snip");
        HidePanels();
        snip ??= new SnipOverlay(this);
        snip.BeginSnip();
    }

    void ShowTranslator()
    {
        Log.Info("show: translator");
        CapturePasteTarget();
        translator ??= new TranslatorForm(this);
        translator.OpenWithClipboard();
    }

    /// <summary>Открыть переводчик сразу с заданным текстом (например, из истории буфера).</summary>
    public void ShowTranslatorWithText(string text)
    {
        Log.Info("show: translator (текст из истории)");
        CapturePasteTarget();
        translator ??= new TranslatorForm(this);
        translator.OpenWithText(text);
    }

    SettingsForm? settings;

    void ShowSettings()
    {
        Log.Info("show: settings");
        if (settings != null && !settings.IsDisposed)
        {
            settings.Activate();
            return;
        }
        settings = new SettingsForm(this);
        settings.Show();
    }

    /// <summary>Смена темы: окна пересоздаются с новой палитрой при следующем открытии.</summary>
    public void ApplyTheme()
    {
        Theme.Set(S.Theme);
        Log.Info($"theme → {S.Theme}");
        void Recreate(ref HistoryPanel? f) { f?.Dispose(); f = null; }
        Recreate(ref history);
        emoji?.Dispose(); emoji = null;
        translator?.Dispose(); translator = null;
        snip?.Dispose(); snip = null;
        snipToast?.Dispose(); snipToast = null;
    }

    /// <summary>Применение настроек, влияющих на хук (вызывается после сохранения настроек).</summary>
    public void ApplyHookSettings()
    {
        if (hook != null) hook.InterceptSnapshot = S.InterceptPrintScreen;
    }

    public void AddRecentSymbol(string ch)
    {
        var list = S.RecentSymbols;
        list.Remove(ch);
        list.Insert(0, ch);
        if (list.Count > 24) list.RemoveRange(24, list.Count - 24);
        S.Save();
    }

    Bitmap? _lastShot;

    /// <summary>Принимает владение снимком: битмап не диспоузится, пока живёт на клипборде.</summary>
    public void OnScreenshot(Bitmap bmp)
    {
        SuppressClipboard();
        try { Clipboard.SetImage(bmp); } catch (Exception e) { Log.Error("screenshot clipboard", e); }
        Store.AddImage(new Bitmap(bmp));

        // держим ссылку на время возможного OCR, показываем плашку (она фокус не забирает)
        _lastShot = bmp;
        snipToast ??= new SnipToast(this);
        snipToast.ShowFor(bmp);
    }

    // ——— буфер обмена ———

    void OnClipboardChanged()
    {
        _ = HandleClipboardChangedAsync();
    }

    async Task HandleClipboardChangedAsync()
    {
        try
        {
            if (DateTime.UtcNow < _suppressUntil) return;

            IDataObject? data = null;
            try { data = Clipboard.GetDataObject(); }
            catch (Exception ex) { Log.Error("clipboard GetDataObject", ex); return; }
            if (data == null) return;

            // скопированные файлы/папки (проводник)
            if (S.CaptureFiles && data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = data.GetData(DataFormats.FileDrop) as string[];
                if (files is { Length: > 0 })
                {
                    if (IsDuplicate("F:" + string.Join("\n", files))) return;
                    Log.Info($"clipboard: files x{files.Length}");
                    Store.AddFiles(files);
                    return;
                }
            }

            if (data.GetDataPresent(DataFormats.Text))
            {
                var t = data.GetData(DataFormats.Text) as string;
                if (!string.IsNullOrEmpty(t))
                {
                    if (IsDuplicate("T:" + t)) return;
                    Log.Info($"clipboard: text ({t.Length} симв.)");
                    Store.AddText(t);
                    return;
                }
            }

            if (await CaptureImageAsync(data)) return;

            var formats = data.GetFormats();
            if (formats.Length == 0) return; // переходное состояние, не мусорим в лог
            Log.Info($"clipboard: нет поддерживаемого формата [{string.Join(", ", formats.Take(8))}]");
        }
        catch (Exception ex) { Log.Error("clipboard read", ex); }
    }

    // Explorer выставляет буфер в несколько приёмов (FileDrop, DropEffect, служебные) —
    // одно копирование даёт 2-3 обновления; отсекаем повторы, чтобы не плодить дубли в истории
    string _lastSig = "";
    DateTime _lastSigAt = DateTime.MinValue;

    bool IsDuplicate(string sig)
    {
        var now = DateTime.UtcNow;
        if (sig == _lastSig && (now - _lastSigAt).TotalMilliseconds < 800) return true;
        _lastSig = sig;
        _lastSigAt = now;
        return false;
    }

    static byte[]? PngBytes(object? raw) => raw switch
    {
        MemoryStream ms => ms.ToArray(),
        byte[] b => b,
        Stream st => StreamToBytes(st),
        _ => null
    };

    static byte[] StreamToBytes(Stream st)
    {
        using var m = new MemoryStream();
        st.CopyTo(m);
        return m.ToArray();
    }

    async Task<bool> CaptureImageAsync(IDataObject data)
    {
        if (!S.CaptureImages) return false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (data.GetDataPresent(DataFormats.Bitmap))
                {
                    using var img = data.GetData(DataFormats.Bitmap) as Image;
                    if (img != null)
                    {
                        if (IsDuplicate($"I:{img.Width}x{img.Height}")) return true;
                        Log.Info($"clipboard: image {img.Width}x{img.Height}");
                        Store.AddImage(new Bitmap(img));
                        return true;
                    }
                }
                if (data.GetDataPresent("PNG"))
                {
                    // источник может отдать PNG как MemoryStream, byte[] или любой Stream (Telegram — по-разному)
                    byte[]? bytes = PngBytes(data.GetData("PNG"));
                    if (bytes != null)
                    {
                        using var png = new Bitmap(new MemoryStream(bytes));
                        if (IsDuplicate($"I:{png.Width}x{png.Height}")) return true;
                        Log.Info($"clipboard: png {png.Width}x{png.Height}");
                        Store.AddImage(new Bitmap(png));
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                // источник мог ещё не отдать данные (delayed rendering) — пробуем снова,
                // но ждём через Task.Delay, не блокируя UI-поток
                Log.Error($"clipboard image (попытка {attempt + 1})", ex);
                await Task.Delay(60);
            }
        }
        return false;
    }

    // ——— трей ———

    ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        void Add(string text, Action action)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += (s, e) => action();
            menu.Items.Add(item);
        }

        Add("История буфера (Win+V)", ShowHistory);
        Add("Эмодзи и символы (Win+.)", ShowEmoji);
        Add("Скриншот (Win+Shift+S)", ShowSnip);
        Add("Переводчик (Win+T)", ShowTranslator);
        menu.Items.Add(new ToolStripSeparator());
        Add("Настройки…", ShowSettings);
        Add("Открыть папку данных", () =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Settings.AppDir) { UseShellExecute = true }); }
            catch { }
        });
        menu.Items.Add(new ToolStripSeparator());
        Add("Выход", ExitApp);
        return menu;
    }

    void ExitApp()
    {
        try
        {
            for (int id = 1; id <= 4; id++)
                if (_hotkeyOwned[id]) Native.UnregisterHotKey(host.Handle, id);
            Native.RemoveClipboardFormatListener(host.Handle);
            hook?.Dispose();
        }
        catch { }
        tray.Visible = false;
        tray.Dispose();
        AppIcon.Dispose();
        host.Dispose();
        Application.Exit();
    }
}
