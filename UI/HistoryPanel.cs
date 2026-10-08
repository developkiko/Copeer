using Copeer.Core;

namespace Copeer.UI;

/// <summary>Всплывающая панель истории буфера (Win+V): поиск, Enter/двойной клик — вставить.</summary>
class HistoryPanel : Form
{
    readonly AppContext _c;
    readonly TextBox _search = null!;
    readonly ListBox _list = null!;
    readonly List<Entry> _view = new();
    readonly Dictionary<string, Image> _thumbs = new();

    public HistoryPanel(AppContext c)
    {
        _c = c;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Theme.Back;
        KeyPreview = true;
        DoubleBuffered = true;
        Size = new Size(430, 520);

        var top = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Back, Padding = new Padding(10, 8, 10, 6) };
        _search = new TextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 10.5f)
        };
        _search.TextChanged += (s, e) => Repopulate();
        _search.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Down && _list.Items.Count > 0)
            {
                e.Handled = e.SuppressKeyPress = true;
                _list.SelectedIndex = Math.Min(_list.SelectedIndex + 1, _list.Items.Count - 1);
                _list.Focus();
            }
            else if (e.KeyCode == Keys.Up && _list.Items.Count > 0)
            {
                e.Handled = e.SuppressKeyPress = true;
                _list.SelectedIndex = Math.Max(_list.SelectedIndex - 1, 0);
                _list.Focus();
            }
        };
        top.Controls.Add(_search);

        _list = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Back,
            ForeColor = Theme.Text,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 40,
            Font = new Font("Segoe UI", 9.5f),
            IntegralHeight = false
        };
        _list.DrawItem += List_DrawItem;
        _list.DoubleClick += (s, e) => PasteSelected();
        _list.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; PasteSelected(); }
            else if (e.KeyCode == Keys.Delete && _list.SelectedIndex >= 0)
            {
                // Changed перестроит список и почистит миниатюру
                _c.Store.Remove(_view[_list.SelectedIndex]);
            }
        };

        var hint = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 24,
            Text = "Enter / 2×клик — вставить   •   Del — удалить   •   Esc — закрыть",
            ForeColor = Theme.TextDim,
            BackColor = Theme.Back,
            Font = new Font("Segoe UI", 8.5f),
            TextAlign = ContentAlignment.MiddleCenter
        };

        Controls.Add(_list);
        Controls.Add(top);
        Controls.Add(hint);

        Deactivate += (s, e) => { if ((DateTime.UtcNow - _shownAt).TotalMilliseconds > 300) Hide(); };
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Hide(); };
        _c.Store.Changed += OnStoreChanged;
    }

    DateTime _shownAt;

    // новый элемент появился, пока панель открыта — перестраиваем, сохраняя выделение
    void OnStoreChanged()
    {
        if (!Visible) return;
        var selId = _list.SelectedIndex >= 0 && _list.SelectedIndex < _view.Count
            ? _view[_list.SelectedIndex].Id : null;
        Repopulate();
        if (selId != null)
        {
            var idx = _view.FindIndex(x => x.Id == selId);
            if (idx >= 0) _list.SelectedIndex = idx;
        }
        PruneThumbs();
    }

    void PruneThumbs()
    {
        var ids = new HashSet<string>(_c.Store.Items.Select(x => x.Id));
        foreach (var stale in _thumbs.Where(kv => !ids.Contains(kv.Key)).Select(kv => kv.Key).ToList())
        {
            _thumbs[stale].Dispose();
            _thumbs.Remove(stale);
        }
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ClassStyle |= 0x00020000; return cp; } // CS_DROPSHADOW
    }

    public void Open()
    {
        _search.Text = "";
        Repopulate();

        int h = Math.Max(220, Math.Min(520, 46 + _view.Count * _list.ItemHeight + 24 + 60));
        Size = new Size(Width, h);
        Ui.PlacePopup(this, _c.S);

        _shownAt = DateTime.UtcNow;
        Show();
        Activate();
        _search.Focus();
        Ui.RetryFocus(this);
    }

    void Repopulate()
    {
        var q = _search.Text.Trim();
        _view.Clear();
        _view.AddRange(_c.Store.Items.Where(x =>
            q.Length == 0 ||
            (x.Kind != EntryKind.Image && x.Text.Contains(q, StringComparison.OrdinalIgnoreCase))));

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var it in _view) _list.Items.Add(it);
        _list.EndUpdate();
        if (_view.Count > 0) _list.SelectedIndex = 0;
    }

    void PasteSelected()
    {
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _view.Count) return;
        Paster.Paste(_c, _view[_list.SelectedIndex]);
    }

    void List_DrawItem(object? s, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using (var bg = new SolidBrush(sel ? Theme.SelRow : Theme.Back))
            e.Graphics.FillRectangle(bg, e.Bounds);

        if (e.Index >= _view.Count) return;
        var it = _view[e.Index];

        if (it.Kind == EntryKind.Image)
        {
            var thumb = GetThumb(it);
            if (thumb != null) e.Graphics.DrawImage(thumb, e.Bounds.X + 8, e.Bounds.Y + 4, 48, 32);
            var rect = new Rectangle(e.Bounds.X + 64, e.Bounds.Y, e.Bounds.Width - 74, e.Bounds.Height);
            using var f = new Font("Segoe UI", 9f);
            TextRenderer.DrawText(e.Graphics, $"Изображение {it.W}×{it.H}", f, rect,
                Theme.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        else
        {
            string text;
            if (it.Kind == EntryKind.Files)
            {
                var parts = it.Text.Split('\n');
                text = parts.Length == 1
                    ? $"Файл: {Path.GetFileName(parts[0])}"
                    : $"Файлов: {parts.Length} ({Path.GetFileName(parts[0])} …)";
            }
            else
            {
                text = it.Text.Replace("\r", "").Replace("\n", " ⏎ ");
            }
            var rect = new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 20, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, text, Font, rect, Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    Image? GetThumb(Entry it)
    {
        if (it.ImageFile == null) return null;
        if (_thumbs.TryGetValue(it.Id, out var t)) return t;
        try
        {
            using var full = new Bitmap(it.ImageFile);
            // вписываем с сохранением пропорций, а не растягиваем в 48×32
            var th = new Bitmap(48, 32);
            using (var g = Graphics.FromImage(th))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                double scale = Math.Min(48.0 / Math.Max(1, full.Width), 32.0 / Math.Max(1, full.Height));
                int w = Math.Max(1, (int)Math.Round(full.Width * scale));
                int h = Math.Max(1, (int)Math.Round(full.Height * scale));
                g.DrawImage(full, (48 - w) / 2, (32 - h) / 2, w, h);
            }
            _thumbs[it.Id] = th;
            return th;
        }
        catch { return null; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _c.Store.Changed -= OnStoreChanged;
            foreach (var t in _thumbs.Values) t.Dispose();
            _thumbs.Clear();
        }
        base.Dispose(disposing);
    }
}
