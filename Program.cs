using Copeer.Core;
using Copeer.UI;
using System.Drawing.Drawing2D;

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

    HotkeyHost host = null!;
    NotifyIcon tray = null!;

    HistoryPanel? history;
    EmojiPanel? emoji;
    TranslatorForm? translator;
    SnipOverlay? snip;
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
        _ = host.Handle; // создаём окно сразу

        tray = new NotifyIcon
        {
            Icon = MakeIcon(),
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
        hook.ComboHit = vk =>
        {
            switch ((uint)vk)
            {
                case KeyboardHook.VK_PERIOD: ShowEmoji(); break;
                case KeyboardHook.VK_S: ShowSnip(); break;
                case KeyboardHook.VK_T: ShowTranslator(); break;
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
    }

    public void AddRecentSymbol(string ch)
    {
        var list = S.RecentSymbols;
        list.Remove(ch);
        list.Insert(0, ch);
        if (list.Count > 24) list.RemoveRange(24, list.Count - 24);
        S.Save();
    }

    /// <summary>Принимает владение снимком: битмап не диспоузится, пока живёт на клипборде.</summary>
    public void OnScreenshot(Bitmap bmp)
    {
        SuppressClipboard();
        try { Clipboard.SetImage(bmp); } catch (Exception e) { Log.Error("screenshot clipboard", e); }
        Store.AddImage(new Bitmap(bmp));
    }

    // ——— буфер обмена ———

    void OnClipboardChanged()
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
                    Log.Info($"clipboard: text ({t.Length} симв.)");
                    Store.AddText(t);
                    return;
                }
            }

            if (CaptureImage(data)) return;

            Log.Info($"clipboard: нет поддерживаемого формата [{string.Join(", ", data.GetFormats().Take(8))}]");
        }
        catch (Exception ex) { Log.Error("clipboard read", ex); }
    }

    bool CaptureImage(IDataObject data)
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
                        Log.Info($"clipboard: image {img.Width}x{img.Height}");
                        Store.AddImage(new Bitmap(img));
                        return true;
                    }
                }
                if (data.GetDataPresent("PNG"))
                {
                    if (data.GetData("PNG") is MemoryStream ms)
                    {
                        using var png = new Bitmap(ms);
                        Log.Info($"clipboard: png {png.Width}x{png.Height}");
                        Store.AddImage(new Bitmap(png));
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                // источник мог ещё не отдать данные (delayed rendering) — пробуем снова
                Log.Error($"clipboard image (попытка {attempt + 1})", ex);
                Thread.Sleep(60);
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
        if (_iconHandle != IntPtr.Zero) Native.DestroyIcon(_iconHandle);
        host.Dispose();
        Application.Exit();
    }

    IntPtr _iconHandle;

    Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = new GraphicsPath();
            path.AddArc(0, 0, 12, 12, 180, 180);
            path.AddArc(20, 0, 12, 12, 270, 180);
            path.AddArc(20, 20, 12, 12, 0, 180);
            path.AddArc(0, 20, 12, 12, 90, 180);
            path.CloseFigure();
            using var br = new LinearGradientBrush(new Rectangle(0, 0, 32, 32),
                Color.FromArgb(0x4A, 0x9E, 0xFF), Color.FromArgb(0x7B, 0x4A, 0xE2), 45f);
            g.FillPath(br, path);
            using var f = new Font("Segoe UI", 17f, FontStyle.Bold, GraphicsUnit.Pixel);
            var sz = g.MeasureString("C", f);
            g.DrawString("C", f, Brushes.White, 16 - sz.Width / 2, 15 - sz.Height / 2);
        }
        _iconHandle = bmp.GetHicon(); // освобождаем DestroyIcon'ом в ExitApp
        return Icon.FromHandle(_iconHandle);
    }
}
