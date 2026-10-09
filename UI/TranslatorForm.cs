using Copeer.Core;

namespace Copeer.UI;

/// <summary>Окно переводчика на Neurooo API (Win+T). Открывается с текстом из буфера обмена.</summary>
class TranslatorForm : Form
{
    readonly AppContext _c;
    readonly NeuroooClient _api = new();
    readonly TextBox _src = null!;
    readonly TextBox _dst = null!;
    readonly ComboBox _lang = null!;
    readonly ComboBox _engine = null!;
    readonly Label _status = null!;
    readonly Button _go = null!;
    bool _busy;

    public TranslatorForm(AppContext c)
    {
        _c = c;
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

        Label L(string t, int y) => new Label
        {
            Text = t,
            AutoSize = true,
            Location = new Point(14, y),
            ForeColor = Theme.TextDim
        };

        Controls.Add(L("Исходный текст  (Ctrl+Enter — перевести)", 12));
        _src = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(14, 32),
            Size = new Size(518, 96),
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

        _lang = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(14, 140),
            Size = new Size(170, 26),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Field,
            ForeColor = Theme.Text
        };
        foreach (var l in Langs.List) _lang.Items.Add(l.Name);
        var defIdx = Langs.IndexOf(_c.S.TargetLanguage);
        _lang.SelectedIndex = defIdx >= 0 ? defIdx : 1;
        _lang.SelectedIndexChanged += (s, e) =>
        {
            _c.S.TargetLanguage = Langs.List[_lang.SelectedIndex].Code;
            _c.S.Save();
        };
        Controls.Add(_lang);

        _engine = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(196, 140),
            Size = new Size(192, 26),
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
        Controls.Add(_engine);

        _go = new Button
        {
            Text = "Перевести",
            Location = new Point(400, 139),
            Size = new Size(132, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Accent,
            ForeColor = Color.White
        };
        _go.FlatAppearance.BorderSize = 0;
        _go.Click += (s, e) => RunTranslate();
        Controls.Add(_go);

        Controls.Add(L("Результат", 178));
        _dst = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            Location = new Point(14, 198),
            Size = new Size(518, 180),
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        _dst.HandleCreated += (s, e) => Native.ScrollbarTheme(_dst.Handle);
        Controls.Add(_dst);

        var copy = MakeButton("Копировать", new Point(14, 390), new Size(120, 30));
        copy.Click += (s, e) =>
        {
            if (_dst.Text.Length == 0) return;
            _c.SuppressClipboard();
            try { Clipboard.SetText(_dst.Text); _c.Store.AddText(_dst.Text); _status.Text = "Скопировано ✓"; }
            catch { }
        };
        Controls.Add(copy);

        var insert = MakeButton("Вставить в окно", new Point(142, 390), new Size(150, 30));
        insert.Click += (s, e) => { if (_dst.Text.Length > 0) Paster.PasteText(_c, _dst.Text); };
        Controls.Add(insert);

        _status = new Label
        {
            Location = new Point(306, 398),
            AutoSize = true,
            ForeColor = Theme.TextDim
        };
        Controls.Add(_status);

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

    public void OpenWithClipboard()
    {
        var clip = TryClipboardText();
        if (!string.IsNullOrEmpty(clip))
        {
            // Win+T всегда означает «перевести то, что в буфере сейчас»;
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
            var srcCode = Langs.List[_lang.SelectedIndex].Code;
            var (result, detected) = await _api.TranslateAsync(
                _c.S, text, _c.S.TargetLanguage,
                sourceLang: srcCode == "auto" ? null : srcCode,
                engine: string.IsNullOrWhiteSpace(_c.S.Engine) ? null : _c.S.Engine);
            _dst.Text = result;
            var from = srcCode == "auto" && detected.Length > 0 ? detected : srcCode;
            _status.Text = $"{from} → {_c.S.TargetLanguage} • {text.Length} симв.";
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
