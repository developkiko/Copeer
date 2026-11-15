using Copeer.Core;

namespace Copeer.UI;

/// <summary>
/// Оверлей скриншота (Win+Shift+S или PrintScreen): «замораживает» экран, пользователь
/// выделяет область (размер показывается на рамке) → открывается окно скриншота (ShotForm).
/// </summary>
class SnipOverlay : Form
{
    readonly AppContext _c;
    Bitmap? _frozen;
    Rectangle _sel;
    bool _drag;
    Point _start;

    public SnipOverlay(AppContext c)
    {
        _c = c;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Cursor = Cursors.Cross;
        KeyPreview = true;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 10f);

        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Cancel(); };
        MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left) { _drag = true; _start = e.Location; _sel = Rectangle.Empty; }
            else if (e.Button == MouseButtons.Right) Cancel(); // отмена без клавиатуры
        };
        MouseMove += (s, e) =>
        {
            if (!_drag) return;
            _sel = Rectangle.FromLTRB(
                Math.Min(_start.X, e.X), Math.Min(_start.Y, e.Y),
                Math.Max(_start.X, e.X), Math.Max(_start.Y, e.Y));
            Invalidate();
        };
        MouseUp += (s, e) =>
        {
            if (!_drag) return;
            _drag = false;
            if (_sel.Width >= 8 && _sel.Height >= 8) Complete();
            else Cancel();
        };
        Paint += OnPaint;
    }

    public void BeginSnip()
    {
        var vs = SystemInformation.VirtualScreen;
        Bounds = vs;
        _frozen?.Dispose();
        _frozen = new Bitmap(vs.Width, vs.Height);
        using (var g = Graphics.FromImage(_frozen))
            g.CopyFromScreen(vs.X, vs.Y, 0, 0, vs.Size);
        BackgroundImage = _frozen;
        _sel = Rectangle.Empty;
        Show();
        Activate();
        Focus();
    }

    void OnPaint(object? s, PaintEventArgs e)
    {
        if (_frozen == null) return;
        using (var dim = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
            e.Graphics.FillRectangle(dim, ClientRectangle);

        if (!_sel.IsEmpty)
        {
            // внутри выделения показываем экран без затемнения
            e.Graphics.DrawImage(_frozen, _sel, _sel, GraphicsUnit.Pixel);
            using var pen = new Pen(Theme.Accent, 2);
            e.Graphics.DrawRectangle(pen, _sel);
            TextRenderer.DrawText(e.Graphics, $"{_sel.Width}×{_sel.Height}", Font,
                BadgeRect(), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // размер в пикселях — прямо на рамке выделения: под нижним краем
    // (или над верхним, если до низа экрана места нет)
    Rectangle BadgeRect()
    {
        var vs = SystemInformation.VirtualScreen;
        var r = new Rectangle(_sel.X, _sel.Bottom + 3, _sel.Width, 20);
        if (r.Bottom > vs.Bottom) r.Y = _sel.Top - 23;
        return r;
    }

    void Complete()
    {
        var r = _sel;
        Hide();
        try
        {
            if (_frozen == null) return;
            // режем из «замороженного» кадра: живой экран мог ещё не убрать оверлей
            var shot = _frozen.Clone(r, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            // владение снимком переходит в окно скриншота: там пользователь решит —
            // распознать, скопировать, вставить или отменить
            _c.ShowShotForm(shot);
        }
        catch (Exception ex) { Log.Error("snip capture", ex); }
        Cleanup();
    }

    void Cancel()
    {
        Hide();
        Cleanup();
    }

    void Cleanup()
    {
        BackgroundImage = null;
        _frozen?.Dispose();
        _frozen = null;
    }
}
