using Copeer.Core;

namespace Copeer.UI;

/// <summary>Окно настроек. Источник правды — settings.json, окно его удобно редактирует.</summary>
class SettingsForm : Form
{
    readonly AppContext _c;
    readonly NeuroooClient _api = new();
    List<(string Id, string Name)> _engines = new();

    readonly TextBox _key;
    readonly Button _showKey;
    readonly Button _check;
    readonly Label _keyStatus;
    readonly ComboBox _lang;
    readonly ComboBox _engine;
    readonly NumericUpDown _limit;
    readonly NumericUpDown _maxLen;
    readonly CheckBox _captureImages;
    readonly CheckBox _captureFiles;
    readonly NumericUpDown _delay;
    readonly CheckBox _leavePasted;
    readonly ComboBox _position;
    readonly ComboBox _theme;
    readonly ComboBox _ocrEngine;
    readonly TextBox _gcvKey;
    readonly CheckBox _chkPrint;
    readonly NumericUpDown _toastTime;
    readonly CheckBox _rememberTimeout;
    readonly CheckBox _autostart;
    bool _busy;
    bool _saved;

    static readonly Font SectionFont = new("Segoe UI", 9f, FontStyle.Bold);
    static readonly Font UiFont = new("Segoe UI", 9.5f);

    public SettingsForm(AppContext c)
    {
        _c = c;
        Text = "Copeer — настройки";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 848);
        Font = UiFont;
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        Label Section(string text, int y) => new Label
        {
            Text = text,
            AutoSize = true,
            Location = new Point(14, y),
            Font = SectionFont,
            ForeColor = Theme.Accent
        };
        Label L(string text, int x, int y) => new Label
        {
            Text = text,
            AutoSize = true,
            Location = new Point(x, y),
            ForeColor = Theme.Text
        };

        Controls.Add(Section("NEUROOO — ПЕРЕВОД", 12));

        Controls.Add(L("API-ключ", 14, 40));
        _key = new TextBox
        {
            Location = new Point(14, 60),
            Size = new Size(330, 25),
            UseSystemPasswordChar = true,
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            // иначе пустое поле при «Сохранить» стирало бы уже заданный ключ
            Text = _c.S.NeuroooApiKey
        };
        Controls.Add(_key);

        _showKey = MakeButton("Показать", new Point(350, 59), new Size(76, 26));
        _showKey.Click += (s, e) =>
        {
            _key.UseSystemPasswordChar = !_key.UseSystemPasswordChar;
            _showKey.Text = _key.UseSystemPasswordChar ? "Показать" : "Скрыть";
        };
        Controls.Add(_showKey);

        _check = MakeButton("Проверить", new Point(432, 59), new Size(114, 26), accent: true);
        _check.Click += (s, e) => _ = CheckKeyAsync();
        Controls.Add(_check);

        _keyStatus = new Label
        {
            AutoSize = true,
            Location = new Point(14, 92),
            ForeColor = Theme.TextDim,
            Text = HintKey()
        };
        Controls.Add(_keyStatus);

        var link = new LinkLabel
        {
            AutoSize = true,
            Location = new Point(440, 92),
            Text = "Получить ключ ↗",
            LinkColor = Theme.Accent
        };
        link.LinkClicked += (s, e) => OpenUrl("https://neurooo.com/en/api");
        Controls.Add(link);

        Controls.Add(L("Язык перевода", 14, 122));
        _lang = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(14, 142),
            Size = new Size(240, 26),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Field,
            ForeColor = Theme.Text
        };
        foreach (var l in Langs.List) _lang.Items.Add(l.Name);
        var li = Langs.IndexOf(_c.S.TargetLanguage);
        _lang.SelectedIndex = li >= 0 ? li : 1;
        Controls.Add(_lang);

        Controls.Add(L("Движок", 276, 122));
        _engine = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(276, 142),
            Size = new Size(270, 26),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Field,
            ForeColor = Theme.Text
        };
        Controls.Add(_engine);
        _ = LoadEnginesAsync();

        Controls.Add(Section("ИСТОРИЯ БУФЕРА", 186));

        Controls.Add(L("Лимит записей", 14, 216));
        _limit = new NumericUpDown
        {
            Location = new Point(300, 213),
            Size = new Size(120, 25),
            Minimum = 50,
            Maximum = 10000,
            Increment = 50,
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        _limit.Value = _c.S.HistoryLimit;
        Controls.Add(_limit);

        Controls.Add(L("Макс. длина текста, символов", 14, 246));
        _maxLen = new NumericUpDown
        {
            Location = new Point(300, 243),
            Size = new Size(120, 25),
            Minimum = 1000,
            Maximum = 2_000_000,
            Increment = 10000,
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        _maxLen.Value = _c.S.MaxTextLength;
        Controls.Add(_maxLen);

        _captureImages = MakeCheck("Захватывать изображения", 14, 276, _c.S.CaptureImages);
        _captureFiles = MakeCheck("Захватывать файлы из проводника", 280, 276, _c.S.CaptureFiles);
        Controls.Add(_captureImages);
        Controls.Add(_captureFiles);

        Controls.Add(Section("ВСТАВКА", 308));

        Controls.Add(L("Задержка возврата буфера, мс", 14, 338));
        _delay = new NumericUpDown
        {
            Location = new Point(300, 335),
            Size = new Size(120, 25),
            Minimum = 0,
            Maximum = 5000,
            Increment = 100,
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        _delay.Value = _c.S.PasteRestoreDelayMs;
        Controls.Add(_delay);

        _leavePasted = MakeCheck("Оставлять вставленное в буфере (не возвращать старое)", 14, 368, _c.S.LeavePastedInClipboard);
        Controls.Add(_leavePasted);

        Controls.Add(Section("ИНТЕРФЕЙС", 400));

        Controls.Add(L("Позиция панелей", 14, 430));
        _position = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(300, 427),
            Size = new Size(220, 26),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Field,
            ForeColor = Theme.Text
        };
        _position.Items.Add("У курсора");
        _position.Items.Add("По центру экрана");
        _position.SelectedIndex = _c.S.PanelPosition == "center" ? 1 : 0;
        Controls.Add(_position);

        Controls.Add(L("Тема", 14, 462));
        _theme = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(300, 459),
            Size = new Size(220, 26),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Field,
            ForeColor = Theme.Text
        };
        _theme.Items.Add("Тёмная");
        _theme.Items.Add("Светлая");
        _theme.SelectedIndex = _c.S.Theme == "light" ? 1 : 0;
        _theme.SelectedIndexChanged += (s, e) => PreviewTheme();
        Controls.Add(_theme);

        Controls.Add(Section("РАСПОЗНАВАНИЕ ТЕКСТА (OCR)", 494));

        Controls.Add(L("Движок", 14, 524));
        _ocrEngine = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(300, 521),
            Size = new Size(220, 26),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Field,
            ForeColor = Theme.Text
        };
        _ocrEngine.Items.Add("Выключено");
        _ocrEngine.Items.Add("Встроенный (Windows, офлайн)");
        _ocrEngine.Items.Add("Google Cloud Vision");
        _ocrEngine.SelectedIndex = _c.S.OcrEngine switch
        {
            "windows" => 1,
            "gcv" => 2,
            _ => 0
        };
        Controls.Add(_ocrEngine);

        Controls.Add(L("Ключ Vision API", 14, 556));
        _gcvKey = new TextBox
        {
            Location = new Point(300, 553),
            Size = new Size(220, 25),
            UseSystemPasswordChar = true,
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Text = _c.S.OcrGcvKey
        };
        Controls.Add(_gcvKey);

        Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(14, 586),
            ForeColor = Theme.TextDim,
            Text = "Ключ: console.cloud.google.com → Cloud Vision API → Credentials (1000 запросов/мес бесплатно)"
        });

        Controls.Add(Section("СКРИНШОТ И ПЛАШКА", 614));

        _chkPrint = MakeCheck("PrintScreen (с Shift/Alt/Ctrl) открывает скриншот Copeer", 14, 644, _c.S.InterceptPrintScreen);
        Controls.Add(_chkPrint);

        Controls.Add(L("Плашка после снимка: секунд до скрытия", 14, 676));
        _toastTime = new NumericUpDown
        {
            Location = new Point(300, 673),
            Size = new Size(90, 25),
            Minimum = -1,
            Maximum = 3600,
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        _toastTime.Value = _c.S.SnipToastTimeoutSec;
        Controls.Add(_toastTime);

        _rememberTimeout = MakeCheck("запомнить", 400, 675, _c.S.SnipToastRememberTimeout);
        Controls.Add(_rememberTimeout);

        Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(14, 706),
            ForeColor = Theme.TextDim,
            Text = "0 — скрыть сразу после ухода курсора, −1 — пока не закрыть крестиком; плашку можно таскать мышью"
        });

        Controls.Add(Section("СИСТЕМА", 736));

        _autostart = MakeCheck("Автозапуск с Windows", 14, 766, Autostart.Enabled);
        Controls.Add(_autostart);

        var save = MakeButton("Сохранить", new Point(14, 800), new Size(130, 32), accent: true);
        save.Click += (s, e) => Save();
        Controls.Add(save);

        var cancel = MakeButton("Отмена", new Point(152, 800), new Size(100, 32));
        cancel.Click += (s, e) => Close();
        Controls.Add(cancel);

        AcceptButton = save;
        ApplyThemeToSelf();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DarkTitleBar(Handle);
    }

    // превью темы меняет глобальную палитру сразу; без сохранения — откатываем к настройкам
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (!_saved) Theme.Set(_c.S.Theme);
    }

    void ApplyThemeToSelf()
    {
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        foreach (Control ctl in Controls)
            if (ctl is Label { } lbl && lbl.Font != SectionFont)
                lbl.ForeColor = Theme.Text;
        _keyStatus.ForeColor = Theme.TextDim;
        _key.BackColor = Theme.Field; _key.ForeColor = Theme.Text;
        foreach (var cb in new[] { _lang, _engine, _position, _theme, _ocrEngine })
        {
            cb.BackColor = Theme.Field;
            cb.ForeColor = Theme.Text;
        }
        foreach (var n in new[] { _limit, _maxLen, _delay, _toastTime })
        {
            n.BackColor = Theme.Field;
            n.ForeColor = Theme.Text;
        }
        foreach (var chk in new[] { _captureImages, _captureFiles, _leavePasted, _autostart, _chkPrint, _rememberTimeout })
            chk.ForeColor = Theme.Text;
    }

    string HintKey() => string.IsNullOrWhiteSpace(_c.S.NeuroooApiKey)
        ? "Ключ не задан — переводчик попросит его при первом запуске"
        : "Ключ задан";

    Button MakeButton(string text, Point loc, Size size, bool accent = false) => new Button
    {
        Text = text,
        Location = loc,
        Size = size,
        FlatStyle = FlatStyle.Flat,
        BackColor = accent ? Theme.Accent : Theme.Button,
        ForeColor = accent ? Color.White : Theme.Text,
        Cursor = Cursors.Hand
    };

    CheckBox MakeCheck(string text, int x, int y, bool value) => new CheckBox
    {
        Text = text,
        AutoSize = true,
        Location = new Point(x, y),
        Checked = value,
        ForeColor = Theme.Text
    };

    static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    async Task CheckKeyAsync()
    {
        if (_busy) return;
        var key = _key.Text.Trim();
        if (key.Length == 0)
        {
            _keyStatus.ForeColor = Theme.Err;
            _keyStatus.Text = "✗ Введите ключ";
            return;
        }
        _busy = true;
        _check.Enabled = false;
        _keyStatus.ForeColor = Theme.TextDim;
        _keyStatus.Text = "Проверяю…";
        try
        {
            var tmp = new Settings { NeuroooApiKey = key };
            var (code, name) = await _api.DetectAsync(tmp, "Bonjour, comment allez-vous?");
            _keyStatus.ForeColor = Theme.Ok;
            _keyStatus.Text = $"✓ Ключ работает (тест: язык определён — {name})";
            await LoadEnginesAsync(key);
        }
        catch (Exception ex)
        {
            _keyStatus.ForeColor = Theme.Err;
            _keyStatus.Text = $"✗ {ex.Message}";
        }
        finally
        {
            _busy = false;
            _check.Enabled = true;
        }
    }

    async Task LoadEnginesAsync(string? keyOverride = null)
    {
        var key = keyOverride ?? _key.Text.Trim();
        if (key.Length == 0) key = _c.S.NeuroooApiKey;
        if (string.IsNullOrWhiteSpace(key)) return;

        var selectedEngine = _c.S.Engine;
        try
        {
            var engines = await _api.EnginesAsync(new Settings { NeuroooApiKey = key });
            if (engines.Count == 0) return;
            _engines = engines;
            _engine.Items.Clear();
            _engine.Items.Add("по умолчанию");
            foreach (var (_, name) in engines) _engine.Items.Add(name);
            var idx = 0;
            for (int i = 0; i < engines.Count; i++)
                if (engines[i].Id == selectedEngine) { idx = i + 1; break; }
            _engine.SelectedIndex = idx;
        }
        catch { /* без ключа/офлайн остаётся пусто — при сохранении будет «по умолчанию» */ }
    }

    void PreviewTheme()
    {
        var name = _theme.SelectedIndex == 1 ? "light" : "dark";
        if (name == Theme.Name) return;
        Theme.Set(name);
        ApplyThemeToSelf();
    }

    void Save()
    {
        var key = _key.Text.Trim();
        var themeChanged = (_theme.SelectedIndex == 1 ? "light" : "dark") != _c.S.Theme;

        _c.S.NeuroooApiKey = key;
        _c.S.TargetLanguage = Langs.List[Math.Max(0, _lang.SelectedIndex)].Code;
        // список движков мог не загрузиться (офлайн) — тогда движок не трогаем
        _c.S.Engine = _engines.Count > 0
            ? (_engine.SelectedIndex > 0 && _engine.SelectedIndex - 1 < _engines.Count
                ? _engines[_engine.SelectedIndex - 1].Id
                : "")
            : _c.S.Engine;
        _c.S.HistoryLimit = (int)_limit.Value;
        _c.S.MaxTextLength = (int)_maxLen.Value;
        _c.S.CaptureImages = _captureImages.Checked;
        _c.S.CaptureFiles = _captureFiles.Checked;
        _c.S.PasteRestoreDelayMs = (int)_delay.Value;
        _c.S.LeavePastedInClipboard = _leavePasted.Checked;
        _c.S.PanelPosition = _position.SelectedIndex == 1 ? "center" : "cursor";
        _c.S.Theme = _theme.SelectedIndex == 1 ? "light" : "dark";
        _c.S.OcrEngine = _ocrEngine.SelectedIndex switch { 1 => "windows", 2 => "gcv", _ => "" };
        _c.S.OcrGcvKey = _gcvKey.Text.Trim();
        _c.S.InterceptPrintScreen = _chkPrint.Checked;
        _c.S.SnipToastRememberTimeout = _rememberTimeout.Checked;
        // без галочки «запомнить» время живёт только до перезапуска
        if (_rememberTimeout.Checked) _c.S.SnipToastTimeoutSec = (int)_toastTime.Value;
        _c.S.Autostart = _autostart.Checked;
        Autostart.Set(_autostart.Checked);
        _c.ApplyHookSettings();
        _c.S.Save();

        Log.Info($"settings saved (theme={_c.S.Theme}, engine='{_c.S.Engine}', autostart={_c.S.Autostart})");
        _saved = true;
        if (themeChanged) _c.ApplyTheme();
        Close();
    }
}
