using Copeer.Core;

namespace Copeer.UI;

/// <summary>Всплывающая панель истории буфера (Win+V): поиск, Enter/двойной клик — вставить.</summary>
class HistoryPanel : Form
{
    readonly AppContext _c;
    readonly TextBox _search = null!;
    readonly ListBox _list = null!;
    readonly Button _btnOcr = null!;
    readonly Button _btnTr = null!;
    readonly Label _hint = null!;
    readonly List<Entry> _view = new();
    readonly Dictionary<string, Image> _thumbs = new();
    readonly System.Windows.Forms.Timer _life = new() { Interval = 250 };
    DateTime? _leftAt;

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
        AutoScaleMode = AutoScaleMode.Dpi; // не «мурашиться» на 125–150% масштабе
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
        _search.TextChanged += (s, e) => Repopulate();        _search.KeyDown += (s, e) =>
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
        // кнопки действий над выбранной строкой: распознать картинку / перевести текст
        _btnOcr = new Button
        {
            Text = "🔍",
            Dock = DockStyle.Right,
            Width = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Button,
            ForeColor = Theme.Text,
            Enabled = false,
            TabStop = false,
            Cursor = Cursors.Hand
        };
        _btnOcr.FlatAppearance.BorderSize = 0;
        _btnOcr.Click += (s, e) => _ = OcrSelectedAsync();
        _btnTr = new Button
        {
            Text = "🌐",
            Dock = DockStyle.Right,
            Width = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Button,
            ForeColor = Theme.Text,
            Enabled = false,
            TabStop = false,
            Cursor = Cursors.Hand
        };
        _btnTr.FlatAppearance.BorderSize = 0;
        _btnTr.Click += (s, e) => TranslateSelected();
        top.Controls.Add(_search);
        top.Controls.Add(_btnOcr);
        top.Controls.Add(_btnTr);

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
        _list.HandleCreated += (s, e) => Native.ScrollbarTheme(_list.Handle);
        _list.DoubleClick += (s, e) => PasteSelected();
        _list.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Delete && _list.SelectedIndex >= 0)
            {
                // Changed перестроит список и почистит миниатюру
                _c.Store.Remove(_view[_list.SelectedIndex]);
            }
        };

        var hint = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 24,
            Text = "Enter / 2×клик — вставить   •   L — распознать   •   T — перевести   •   Del — удалить   •   Esc",
            ForeColor = Theme.Text,
            BackColor = Theme.Panel, // статусная строка выделена цветом панели
            Font = new Font("Segoe UI", 8.5f),
            TextAlign = ContentAlignment.MiddleCenter
        };
        _hint = hint;

        Controls.Add(_list);
        Controls.Add(top);
        Controls.Add(hint);

        _list.SelectedIndexChanged += (s, e) => UpdateActions();
        UpdateActions();

        KeyDown += (s, e) =>        {
            if (e.KeyCode == Keys.Escape) { HideLive(); return; }
            if (ActiveControl == _search)
            {
                // Enter в поиске = вставить верхний результат
                if (e.KeyCode == Keys.Enter && Selected != null)
                {
                    e.Handled = e.SuppressKeyPress = true;
                    PasteSelected();
                }
                return; // в поиске печатают — L/T не перехватываем
            }
            if (e.KeyCode == Keys.Enter && Selected != null)
            {
                e.Handled = e.SuppressKeyPress = true;
                PasteSelected();
                return;
            }
            if (e.KeyCode == Keys.L && _btnOcr.Enabled) { e.Handled = e.SuppressKeyPress = true; _ = OcrSelectedAsync(); }
            if (e.KeyCode == Keys.T && _btnTr.Enabled) { e.Handled = e.SuppressKeyPress = true; TranslateSelected(); }
        };
        // правый клик — закрыть (для режима «пока не закроют»)
        MouseClick += (s, e) => { if (e.Button == MouseButtons.Right) HideLive(); };
        _list.MouseClick += (s, e) => { if (e.Button == MouseButtons.Right) HideLive(); };
        _life.Tick += (s, e) => TickLife();
        _c.Store.Changed += OnStoreChanged;
    }

    // видимость: пока фокус или курсор на панели — живём; потеряли и то и другое —
    // отсчёт SnipToastTimeoutSec (0 — скрыть сразу, -1 — пока не закроют)
    void TickLife()
    {
        int t = _c.S.SnipToastTimeoutSec;
        if (t < 0) return;
        if (ContainsFocus || Bounds.Contains(Cursor.Position)) { _leftAt = null; return; }
        if (t == 0) { HideLive(); return; }
        _leftAt ??= DateTime.UtcNow;
        if ((DateTime.UtcNow - _leftAt.Value).TotalSeconds >= t) HideLive();
    }

    void HideLive()
    {
        _leftAt = null;
        _life.Stop();
        Hide();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible) _life.Stop();
    }

    Entry? Selected => _list.SelectedIndex >= 0 && _list.SelectedIndex < _view.Count ? _view[_list.SelectedIndex] : null;

    void UpdateActions()
    {
        var it = Selected;
        _btnOcr.Enabled = it is { Kind: EntryKind.Image } && Ocr.IsConfigured(_c.S);
        _btnTr.Enabled = it is { Kind: EntryKind.Text };
    }

    async Task OcrSelectedAsync()
    {
        if (Selected is not { Kind: EntryKind.Image, ImageFile: { } file }) return;
        try
        {
            using var bmp = new Bitmap(file);
            var res = await Ocr.RecognizeAsync(_c.S, bmp);
            if (res.Ok)
            {
                _c.SuppressClipboard();
                try { Clipboard.SetText(res.Text); } catch { }
                _c.Store.AddText(res.Text); // Changed обновит список, текст окажется сверху
            }
            else
            {
                MessageBox.Show(this, res.Error, "Распознавание текста", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex) { Log.Error("history ocr", ex); }
    }

    void TranslateSelected()
    {
        if (Selected is { Kind: EntryKind.Text } t && t.Text.Length > 0)
            _c.ShowTranslatorWithText(t.Text);
    }

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

        // разворачиваем список на максимум места от курсора (до 760px),
        // низ панели — прямо у курсора: последний видимый пункт кликается сразу,
        // без прокрутки. Сторону выбираем ту, где больше места (обычно над курсором)
        var p = Cursor.Position;
        var wa = Screen.FromPoint(p).WorkingArea;
        int content = 46 + _view.Count * _list.ItemHeight + 24 + 10;
        int spaceAbove = p.Y - wa.Top - 14;
        int spaceBelow = wa.Bottom - p.Y - 14;
        bool above = spaceAbove >= spaceBelow;
        int space = Math.Max(220, above ? spaceAbove : spaceBelow);
        int h = Math.Max(220, Math.Min(Math.Min(760, content), space));
        Size = new Size(Width, h);

        int x = p.X + 10;
        if (x + Width > wa.Right) x = wa.Right - Width - 4;
        int y = above ? p.Y - h - 8 : p.Y + 8;
        if (y < wa.Top) y = wa.Top;
        Location = new Point(x, y);

        _leftAt = null;
        Show();
        Activate();
        _search.Focus();
        Ui.RetryFocus(this);
        _life.Start();
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

    static readonly Font TsFont = new("Segoe UI", 7.5f);

    void List_DrawItem(object? s, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        bool zebra = e.Index % 2 == 1; // чередование строк
        using (var bg = new SolidBrush(sel ? Theme.SelRow : zebra ? Theme.Zebra : Theme.Back))
            e.Graphics.FillRectangle(bg, e.Bounds);

        if (e.Index >= _view.Count) return;
        var it = _view[e.Index];

        if (it.Kind == EntryKind.Image)
        {
            var thumb = GetThumb(it);
            if (thumb != null) e.Graphics.DrawImage(thumb, e.Bounds.X + 8, e.Bounds.Y + 4, 48, 32);
            var rect = new Rectangle(e.Bounds.X + 64, e.Bounds.Y, e.Bounds.Width - 74 - 120, e.Bounds.Height);
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
            var rect = new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 20 - 124, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, text, Font, rect, Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        // дата и время записи в правом углу — на полупрозрачной пелене,
        // чтобы читались на любом фоне (картинки, выделение, светлая тема)
        var tsRect = new Rectangle(e.Bounds.Right - 126, e.Bounds.Bottom - 19, 120, 17);
        using (var veil = new SolidBrush(Color.FromArgb(sel ? 150 : 115, 0, 0, 0)))
            e.Graphics.FillRectangle(veil, tsRect);
        TextRenderer.DrawText(e.Graphics, it.Ts.ToString("ddd dd.MM.yy HH:mm"), TsFont, tsRect,
            Color.FromArgb(235, 238, 242),
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
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
