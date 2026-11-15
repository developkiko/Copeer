using Copeer.Core;

namespace Copeer.UI;

/// <summary>
/// Окно распознавания: снимок сверху, распознанный текст — снизу (можно править).
/// Кнопки: Копировать / Перевести / Вставить / В файл / Отмена — disabled, пока текста нет.
/// </summary>
class OcrForm : Form
{
    readonly AppContext _c;
    readonly TextBox _text = null!;
    readonly Label _status = null!;
    readonly Button _btnCopy, _btnTr, _btnPaste, _btnFile, _btnCancel;
    readonly Bitmap _shot;
    bool _busy;

    public OcrForm(AppContext c, Bitmap shot)
    {
        _c = c;
        _shot = shot;
        Icon = _c.AppIcon;
        Text = "Copeer — распознавание текста";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        KeyPreview = true;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Theme.Back;
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        double scale = Math.Min(1.0, Math.Min(536.0 / Math.Max(1, shot.Width), 230.0 / Math.Max(1, shot.Height)));
        int pw = Math.Max(1, (int)Math.Round(shot.Width * scale));
        int ph = Math.Max(1, (int)Math.Round(shot.Height * scale));

        var preview = new PictureBox
        {
            Image = shot,
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(pw, ph),
            Location = new Point((560 - pw) / 2, 12),
            BackColor = Theme.Field
        };
        Controls.Add(preview);

        _status = new Label
        {
            AutoSize = true,
            Location = new Point(12, 12 + ph + 8),
            ForeColor = Theme.TextDim,
            Text = "Распознаю…"
        };
        Controls.Add(_status);

        _text = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(12, 12 + ph + 30),
            Size = new Size(536, 150),
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        _text.HandleCreated += (s, e) => Native.ScrollbarTheme(_text.Handle);
        _text.TextChanged += (s, e) => UpdateButtons();
        Controls.Add(_text);

        int by = 12 + ph + 30 + 150 + 10;
        _btnCopy = MakeButton("Копировать", 12, by, 110, accent: true);
        _btnCopy.Click += (s, e) => CopyAction();
        _btnTr = MakeButton("Перевести", 130, by, 110);
        _btnTr.Click += (s, e) => TranslateAction();
        _btnPaste = MakeButton("Вставить", 248, by, 105);
        _btnPaste.Click += (s, e) => PasteAction();
        _btnFile = MakeButton("В файл", 361, by, 90);
        _btnFile.Click += (s, e) => FileAction();
        _btnCancel = MakeButton("Отмена", 459, by, 89);
        _btnCancel.Click += (s, e) => Close();

        ClientSize = new Size(560, by + 42);
        Controls.AddRange(new Control[] { _btnCopy, _btnTr, _btnPaste, _btnFile, _btnCancel });
        UpdateButtons();
    }

    // открывается по Show() — позиция по экрану с курсором и сразу запуск OCR
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
        Activate();
        Ui.RetryFocus(this);
        _ = RunAsync();
    }

    async Task RunAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var res = await Ocr.RecognizeAsync(_c.S, _shot);
            if (res.Ok)
            {
                _text.Text = res.Text;
                _status.ForeColor = Theme.Ok;
                _status.Text = $"✓ {res.Text.Length} симв.";
            }
            else
            {
                _status.ForeColor = Theme.Err;
                _status.Text = "✗ " + res.Error;
            }
        }
        catch (Exception ex)
        {
            Log.Error("ocr form", ex);
            _status.ForeColor = Theme.Err;
            _status.Text = "✗ " + ex.Message;
        }
        _busy = false;
        UpdateButtons();
    }

    void UpdateButtons()
    {
        bool has = _text.Text.Trim().Length > 0;
        _btnCopy.Enabled = has;
        _btnTr.Enabled = has;
        _btnPaste.Enabled = has;
        _btnFile.Enabled = has;
    }

    void CopyAction()
    {
        _c.SuppressClipboard();
        try
        {
            Clipboard.SetText(_text.Text);
            _c.Store.AddText(_text.Text); // в историю — как обычное копирование
            _status.ForeColor = Theme.Ok;
            _status.Text = "Скопировано ✓";
        }
        catch (Exception e) { Log.Error("ocr copy", e); }
    }

    void TranslateAction()
    {
        var text = _text.Text;
        // цель вставки — то окно, откуда вызвали скриншот (цепочка таргетов сохраняется)
        var target = _c.PasteTarget;
        Close();
        _c.ShowTranslatorWithText(text, target);
    }

    void PasteAction()
    {
        Paster.PasteText(_c, _text.Text);
        Close();
    }

    void FileAction()
    {
        try
        {
            if (SaveHelper.SaveText(this, _text.Text))
            {
                _status.ForeColor = Theme.Ok;
                _status.Text = "Сохранено ✓";
            }
        }
        catch (Exception ex)
        {
            Log.Error("ocr save", ex);
            MessageBox.Show(this, "Не удалось сохранить: " + ex.Message, "В файл",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DarkTitleBar(Handle);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _shot.Dispose();
    }

    Button MakeButton(string text, int x, int y, int w, bool accent = false) => new Button
    {
        Text = text,
        Location = new Point(x, y),
        Size = new Size(w, 30),
        FlatStyle = FlatStyle.Flat,
        BackColor = accent ? Theme.Accent : Theme.Button,
        ForeColor = accent ? Color.White : Theme.Text,
        Cursor = Cursors.Hand,
        TabStop = false
    };
}
