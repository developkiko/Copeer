using Copeer.Core;

namespace Copeer.UI;

/// <summary>
/// Окно скриншота: снимок на превью и кнопки Распознать / Копировать / Вставить / Отмена.
/// Копирование и вставка кладут снимок в историю; Отмена (и Esc) — отбрасывает его.
/// </summary>
class ShotForm : Form
{
    readonly AppContext _c;
    readonly PictureBox _preview;
    readonly Button _btnOcr, _btnCopy, _btnPaste, _btnCancel;
    Bitmap? _shot;

    public ShotForm(AppContext c, Bitmap shot)
    {
        _c = c;
        _shot = shot;
        Icon = _c.AppIcon;
        Text = "Copeer — снимок";
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

        // превью: снимок вписывается, окно не раздувается на больших выделениях
        double scale = Math.Min(1.0, Math.Min(540.0 / Math.Max(1, shot.Width), 320.0 / Math.Max(1, shot.Height)));
        int pw = Math.Max(1, (int)Math.Round(shot.Width * scale));
        int ph = Math.Max(1, (int)Math.Round(shot.Height * scale));

        _preview = new PictureBox
        {
            Image = shot,
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(pw, ph),
            Location = new Point(12, 12),
            BackColor = Theme.Field
        };

        int w = Math.Max(pw + 24, 470);
        int by = 12 + ph + 12;
        int bx = (w - 470) / 2 + 12;
        _btnOcr = MakeButton("Распознать", bx, by, 112, accent: true);
        _btnOcr.Click += (s, e) => OcrAction();
        _btnCopy = MakeButton("Копировать", bx + 120, by, 112);
        _btnCopy.Click += (s, e) => CopyAction();
        _btnPaste = MakeButton("Вставить", bx + 240, by, 104);
        _btnPaste.Click += (s, e) => PasteAction();
        _btnCancel = MakeButton("Отмена", bx + 352, by, 94);
        _btnCancel.Click += (s, e) => Close();

        ClientSize = new Size(w, by + 42);
        Controls.AddRange(new Control[] { _preview, _btnOcr, _btnCopy, _btnPaste, _btnCancel });
    }

    public void Open()
    {
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
        Show();
        Activate();
        Ui.RetryFocus(this);
    }

    void CopyAction()
    {
        if (_shot == null) return;
        _c.SuppressClipboard();
        try { Clipboard.SetImage(_shot); }
        catch (Exception e) { Log.Error("shot clipboard", e); return; }
        _c.Store.AddImage(_shot); // снимок попадает в историю (Win+V)
        Close();
    }

    void PasteAction()
    {
        if (_shot == null) return;
        _c.Store.AddImage(_shot);
        // PasteTarget — окно, активное до оверлея: Ctrl+V уйдёт именно туда
        Paster.PasteBitmap(_c, _shot);
        Close();
    }

    void OcrAction()
    {
        var bmp = _shot;
        _shot = null; // владение переходит в окно распознавания
        Close();
        _c.ShowOcrForm(bmp!);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DarkTitleBar(Handle);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _shot?.Dispose();
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
