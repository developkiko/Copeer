using Copeer.Core;

namespace Copeer.UI;

/// <summary>Окно переводчика на Neurooo API (Win+Shift+W). Открывается с выделенным текстом или из буфера обмена.</summary>
class TranslatorForm : Form
{
    readonly AppContext _c;
    readonly NeuroooClient _api = new();
    readonly TextBox _src = null!;
    readonly TextBox _dst = null!;
    readonly ComboBox _srcLang = null!; // язык исходного текста (по умолчанию — автоопределение, не запоминается)
    readonly ComboBox _dstLang = null!; // язык перевода — запоминается в настройках
    readonly ComboBox _engine = null!;
    readonly Label _status = null!;
    readonly Button _go = null!;
    readonly Button _btnCopy = null!;
    readonly Button _btnInsert = null!;
    readonly Button _btnFile = null!;
    bool _busy;

    public TranslatorForm(AppContext c)
    {
        _c = c;
        Icon = _c.AppIcon;
        Text = "Copeer — переводчик (Neurooo)";
        StartPosition = FormStartPosition.Manual;
        TopMost = true; // не теряется за проводником и другими окнами
        Size = new Size(560, 480);
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Hide(); };

        Label L(string t, int y, int x = 14) => new Label
        {
            Text = t,
            AutoSize = true,
            Location = new Point(x, y),
            ForeColor = Theme.TextDim
        };

        Controls.Add(L("Исходный текст  (Ctrl+Enter — перевести)", 12));
        _src = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(14, 32),
            Size = new Size(518, 88),
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            AcceptsReturn = true
        };
        _src.HandleCreated += (s, e) => Native.ScrollbarTheme(_src.Handle);
        _src.KeyDown += (s, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; RunTranslate(); }
        };
        Controls.Add(_src);

        Controls.Add(L("Исходный язык", 124));
        _srcLang = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(14, 142),
            Size = new Size(240, 26),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Field,
            ForeColor = Theme.Text
        };
        foreach (var l in Langs.List) _srcLang.Items.Add(l.Name);
        _srcLang.SelectedIndex = 0; // автоопределение — исходный язык при каждом открытии сбрасывается
        Ui.ThemedCombo(_srcLang);
        Controls.Add(_srcLang);

        Controls.Add(L("Язык перевода", 124, 270));
        _dstLang = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(270, 142),
            Size = new Size(240, 26),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Field,
            ForeColor = Theme.Text
        };
        // автоопределения для целевого языка не бывает — его API не принимает
        foreach (var l in Langs.List.Skip(1)) _dstLang.Items.Add(l.Name);
        var defIdx = Langs.IndexOf(_c.S.TargetLanguage) - 1;
        _dstLang.SelectedIndex = defIdx >= 0 ? defIdx : 0;
        _dstLang.SelectedIndexChanged += (s, e) =>
        {
            _c.S.TargetLanguage = Langs.List[_dstLang.SelectedIndex + 1].Code;
            _c.S.Save();
        };
        Ui.ThemedCombo(_dstLang);
        Controls.Add(_dstLang);

        Controls.Add(L("Движок", 174));
        _engine = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(14, 192),
            Size = new Size(240, 26),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Field,
            ForeColor = Theme.Text
        };
        _engine.SelectedIndexChanged += (s, e) =>
        {
            if (_engine.SelectedIndex > 0 && _engine.Items[_engine.SelectedIndex] is EngineItem ei)
            {
                _c.S.Engine = ei.Id;
            }
            else _c.S.Engine = "";
            _c.S.Save();
        };
        Ui.ThemedCombo(_engine);
        Controls.Add(_engine);

        _go = new Button
        {
            Text = "Перевести",
            Location = new Point(270, 191),
            Size = new Size(150, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Accent,
            ForeColor = Color.White
        };
        _go.FlatAppearance.BorderSize = 0;
        _go.Click += (s, e) => RunTranslate();
        Controls.Add(_go);

        Controls.Add(L("Результат", 230));
        _dst = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            Location = new Point(14, 248),
            Size = new Size(518, 148),
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        _dst.HandleCreated += (s, e) => Native.ScrollbarTheme(_dst.Handle);
        Controls.Add(_dst);

        // кнопки: Копировать / Вставить / В файл / Отмена — пусто = disabled
        _btnCopy = MakeButton("Копировать", new Point(14, 406), new Size(110, 30));
        _btnCopy.Click += (s, e) =>
        {
            if (_dst.Text.Length == 0) return;
            _c.SuppressClipboard();
            try { Clipboard.SetText(_dst.Text); _c.Store.AddText(_dst.Text); _status.Text = "Скопировано ✓"; }
            catch { }
        };
        Controls.Add(_btnCopy);

        _btnInsert = MakeButton("Вставить в окно", new Point(130, 406), new Size(150, 30));
        _btnInsert.Click += (s, e) => { if (_dst.Text.Length > 0) Paster.PasteText(_c, _dst.Text); };
        Controls.Add(_btnInsert);

        _btnFile = MakeButton("В файл", new Point(286, 406), new Size(100, 30));
        _btnFile.Click += (s, e) =>
        {
            // сохраняем перевод, а если его ещё нет — исходный текст
            var text = _dst.Text.Length > 0 ? _dst.Text : _src.Text;
            if (text.Length == 0) return;
            try
            {
                if (SaveHelper.SaveText(this, text)) _status.Text = "Сохранено ✓";
            }
            catch (Exception ex)
            {
                Log.Error("translator save", ex);
                MessageBox.Show(this, "Не удалось сохранить: " + ex.Message, "В файл",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        Controls.Add(_btnFile);

        var cancel = MakeButton("Отмена", new Point(392, 406), new Size(110, 30));
        cancel.Click += (s, e) => Hide();
        Controls.Add(cancel);

        _status = new Label
        {
            Location = new Point(14, 442),
            AutoSize = true,
            ForeColor = Theme.TextDim
        };
        Controls.Add(_status);

        _src.TextChanged += (s, e) => UpdateButtons();
        _dst.TextChanged += (s, e) => UpdateButtons();
        UpdateButtons();

        Load += async (s, e) => await LoadEnginesAsync();
    }

    Button MakeButton(string text, Point loc, Size size) => new Button
    {
        Text = text,
        Location = loc,
        Size = size,
        FlatStyle = FlatStyle.Flat,
        BackColor = Theme.Button,
        ForeColor = Theme.Text
    };

    // Копировать/Вставить — есть перевод; В файл — есть хоть что-то (перевод или исходник)
    void UpdateButtons()
    {
        _btnCopy.Enabled = _btnInsert.Enabled = _dst.Text.Length > 0;
        _btnFile.Enabled = _dst.Text.Length > 0 || _src.Text.Length > 0;
    }

    class EngineItem
    {
        public string Id, Name;
        public EngineItem(string id, string name) { Id = id; Name = name; }
        public override string ToString() => Name;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DarkTitleBar(Handle);
    }

    /// <summary>
    /// Хоткей Win+Shift+W: пытаемся забрать выделенный текст из активного окна
    /// (шлём Ctrl+C), если выделения нет — переводим то, что в буфере.
    /// </summary>
    public async void OpenWithSelectionAsync()
    {
        string? clip;
        if (_c.S.TranslatorGrabSelection)
            clip = await GrabSelectionTextAsync();
        else
            clip = TryClipboardText();

        FillFromClipboard(clip);
    }

    // выделяешь текст → жмёшь Win+Shift+W → Copeer шлёт Ctrl+C в активное окно и забирает выделение;
    // если скопировать было нечего — работает прежний буфер обмена
    async Task<string?> GrabSelectionTextAsync()
    {
        try
        {
            IDataObject? prev = null;
            string? prevText = null;
            try
            {
                prev = Clipboard.GetDataObject();
                if (prev != null && prev.GetDataPresent(DataFormats.Text))
                    prevText = prev.GetData(DataFormats.Text) as string;
            }
            catch { }

            await Task.Delay(150); // даём отпустить клавиши хоткея
            var seq0 = Native.GetClipboardSequenceNumber();
            Native.SendCtrlC();
            await Task.Delay(300);
            var seq1 = Native.GetClipboardSequenceNumber();

            if (seq1 == seq0) return prevText; // Ctrl+C ничего не скопировал — прежний буфер

            var text = TryClipboardText();
            if (text == null)
            {
                // в буфер попала картинка/файлы — возвращаем прежнее содержимое
                if (prev != null)
                {
                    _c.SuppressClipboard();
                    try { Clipboard.SetDataObject(prev, true); } catch { }
                }
                return prevText;
            }
            return text;
        }
        catch (Exception e)
        {
            Log.Error("translator grab selection", e);
            return null;
        }
    }

    void FillFromClipboard(string? clip)
    {
        _srcLang.SelectedIndex = 0; // исходный язык — каждый раз с автоопределения

        if (!string.IsNullOrEmpty(clip))
        {
            // хоткей всегда означает «перевести то, что сейчас в источнике»;
            // повторный вызов с тем же текстом не гоняет API повторно
            var text = clip.Length > 12_000 ? clip[..12_000] : clip;
            var changed = text != _src.Text;
            if (changed)
            {
                _src.Text = text;
                _dst.Text = "";
                _status.Text = "";
            }
            if (!string.IsNullOrWhiteSpace(_c.S.NeuroooApiKey))
            {
                if (changed || _dst.Text.Length == 0) RunTranslate();
            }
            else if (changed)
            {
                _status.Text = "Ключ Neurooo не задан — см. README";
            }
        }

        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
        Show();
        BringToFront();
        Activate();
        _src.Focus();
        _src.SelectionStart = _src.TextLength;
    }

    /// <summary>Открыть с заданным текстом (из истории буфера, окна распознавания) и сразу перевести.</summary>
    public void OpenWithText(string text)
    {
        _srcLang.SelectedIndex = 0;
        _src.Text = text.Length > 12_000 ? text[..12_000] : text;
        _dst.Text = "";
        _status.Text = "";

        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
        Show();
        BringToFront();
        Activate();
        _src.Focus();
        _src.SelectionStart = _src.TextLength;

        if (!string.IsNullOrWhiteSpace(_c.S.NeuroooApiKey)) RunTranslate();
        else _status.Text = "Ключ Neurooo не задан — см. README";
    }

    static string? TryClipboardText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
        catch { return null; }
    }

    async void RunTranslate()
    {
        if (_busy) return;
        var text = _src.Text.Trim();
        if (text.Length == 0) return;

        if (string.IsNullOrWhiteSpace(_c.S.NeuroooApiKey))
        {
            _status.ForeColor = Color.Khaki;
            _status.Text = "Нужен API-ключ";
            MessageBox.Show(this,
                "Для перевода нужен API-ключ Neurooo (бесплатный тариф есть).\n\n" +
                "1. Зарегистрируйтесь на neurooo.com\n" +
                "2. Возьмите ключ в разделе API (neurooo.com/en/api)\n" +
                "3. Впишите его в %APPDATA%\\Copeer\\settings.json →  \"NeuroooApiKey\"\n" +
                "4. Перезапустите Copeer (трей → правый клик → Выход, затем запустите заново)",
                "Neurooo API", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _busy = true;
        _go.Enabled = false;
        _status.ForeColor = Theme.TextDim;
        _status.Text = "Перевод…";
        try
        {
            var srcCode = _srcLang.SelectedIndex > 0 ? Langs.List[_srcLang.SelectedIndex].Code : null;
            var target = Langs.List[_dstLang.SelectedIndex + 1].Code;
            var (result, detected) = await _api.TranslateAsync(
                _c.S, text, target,
                sourceLang: srcCode,
                engine: string.IsNullOrWhiteSpace(_c.S.Engine) ? null : _c.S.Engine);
            _dst.Text = result;
            var from = srcCode ?? (detected.Length > 0 ? detected : "?");
            _status.Text = $"{from} → {target} • {text.Length} симв.";
        }
        catch (Exception ex)
        {
            _status.ForeColor = Theme.Err;
            _status.Text = "";
            MessageBox.Show(this, ex.Message, "Ошибка перевода", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _busy = false;
            _go.Enabled = true;
        }
    }

    async Task LoadEnginesAsync()
    {
        if (string.IsNullOrWhiteSpace(_c.S.NeuroooApiKey)) return;
        try
        {
            var engines = await _api.EnginesAsync(_c.S);
            if (engines.Count == 0) return;
            _engine.Items.Clear();
            _engine.Items.Add(new EngineItem("", "движок: по умолчанию"));
            foreach (var (id, name) in engines) _engine.Items.Add(new EngineItem(id, name));
            var idx = 0;
            for (int i = 0; i < _engine.Items.Count; i++)
                if (_engine.Items[i] is EngineItem ei && ei.Id == _c.S.Engine) { idx = i; break; }
            _engine.SelectedIndex = idx;
        }
        catch { /* без ключа/офлайн — оставляем только дефолт */ }
    }
}
