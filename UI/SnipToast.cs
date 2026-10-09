using Copeer.Core;

namespace Copeer.UI;

/// <summary>
/// Плашка после скриншота: «Снимок в буфере» + кнопка «Распознать текст» (если движок настроен).
/// Не забирает фокус (WS_EX_NOACTIVATE), перетаскивается мышью.
/// Пока курсор на плашке — не исчезает; после ухода курсора живёт
/// SnipToastTimeoutSec секунд (0 — скрыть сразу, -1 — пока не закроют крестиком).
/// </summary>
class SnipToast : Form
{
    readonly AppContext _c;
    readonly Label _text = null!;
    readonly Button _ocr = null!;
    readonly Button _close = null!;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    Bitmap? _shot;
    bool _busy, _dragging;
    Point _dragCursor, _dragLocation;
    DateTime? _leftAt;

    const int WS_EX_NOACTIVATE = 0x08000000;
    const int WS_EX_TOPMOST = 0x00000008;

    public SnipToast(AppContext c)
    {
        _c = c;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Theme.Panel;
        KeyPreview = false;
        Cursor = Cursors.SizeAll;

        _text = new Label
        {
            Text = "Снимок в буфере",
            AutoSize = true,
            Location = new Point(12, 13),
            ForeColor = Theme.Text,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 9.5f),
            Cursor = Cursors.SizeAll
        };
        Controls.Add(_text);

        _ocr = new Button
        {
            Text = "Распознать текст",
            AutoSize = true,
            Location = new Point(12, 5),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Accent,
            ForeColor = Color.White,
            Visible = false,
            Cursor = Cursors.Hand
        };
        _ocr.FlatAppearance.BorderSize = 0;
        _ocr.Click += async (s, e) => await RunOcr();
        Controls.Add(_ocr);

        _close = new Button
        {
            Text = "✕",
            Size = new Size(22, 22),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = Theme.TextDim,
            Cursor = Cursors.Hand,
            TabStop = false
        };
        _close.FlatAppearance.BorderSize = 0;
        _close.Click += (s, e) => Dismiss();
        Controls.Add(_close);

        // перетаскивание за фон и за надпись (не за кнопки)
        MouseDown += StartDrag;
        MouseMove += DoDrag;
        MouseUp += (s, e) => _dragging = false;
        _text.MouseDown += StartDrag;
        _text.MouseMove += DoDrag;
        _text.MouseUp += (s, e) => _dragging = false;

        MouseClick += (s, e) => { if (e.Button == MouseButtons.Right) Dismiss(); };

        _timer.Tick += (s, e) => TickVisibility();
    }

    void StartDrag(object? s, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _dragCursor = Cursor.Position;
        _dragLocation = Location;
    }

    void DoDrag(object? s, MouseEventArgs e)
    {
        if (!_dragging) return;
        Location = new Point(
            _dragLocation.X + Cursor.Position.X - _dragCursor.X,
            _dragLocation.Y + Cursor.Position.Y - _dragCursor.Y);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return cp;
        }
    }

    public void ShowFor(Bitmap shot)
    {
        _shot = shot;
        _text.Visible = true;
        _ocr.Visible = Ocr.IsConfigured(_c.S); // кнопка появляется, только когда движок выбран (и ключ указан)
        _ocr.Text = "Распознать текст";
        _ocr.Enabled = true;
        Height = 42;
        _leftAt = null;
        Relayout();
        Show();
        _timer.Start();
    }

    void Dismiss()
    {
        _timer.Stop();
        Hide();
    }

    // видимость: пока курсор на плашке — живём; после ухода — отсчёт SnipToastTimeoutSec
    void TickVisibility()
    {
        if (_busy) return;
        int t = _c.S.SnipToastTimeoutSec;
        if (t < 0) return; // режим «пока не закроют»

        bool hovered = Bounds.Contains(Cursor.Position);
        if (hovered) { _leftAt = null; return; }
        if (t == 0) { Dismiss(); return; }

        _leftAt ??= DateTime.UtcNow;
        if ((DateTime.UtcNow - _leftAt.Value).TotalSeconds >= t) Dismiss();
    }

    void Relayout()
    {
        int w = 12;
        _text.Location = new Point(w, 13);
        if (_text.Visible) w += _text.PreferredWidth + 10;
        _ocr.Location = new Point(w, 5);
        if (_ocr.Visible) w += _ocr.PreferredSize.Width + 8;
        _close.Location = new Point(w, 10);
        Width = Math.Max(120, w + _close.Width + 8);
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Bottom - Height - 12);
    }

    async Task RunOcr()
    {
        if (_busy || _shot == null) return;
        _busy = true;
        _ocr.Enabled = false;
        _ocr.Text = "Распознаю…";
        Relayout();

        var res = await Ocr.RecognizeAsync(_c.S, _shot);
        if (res.Ok)
        {
            _c.SuppressClipboard();
            try { Clipboard.SetText(res.Text); } catch { }
            _c.Store.AddText(res.Text);
            _ocr.Text = $"✓ {res.Text.Length} симв. в буфере";
        }
        else
        {
            _ocr.Text = "✗ " + (res.Error.Length > 26 ? res.Error[..26] + "…" : res.Error);
        }
        _busy = false;
        _ocr.Enabled = true;
        _leftAt = null; // после результата даём полный таймаут заново
        Relayout();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
