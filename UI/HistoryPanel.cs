using Copeer.Core;

namespace Copeer.UI;

/// <summary>
/// Всплывающая панель истории буфера (Win+V): поиск, мультивыделение Ctrl/Shift,
/// кнопки Распознать / Перевести / Вставить / В файл / Отмена.
/// Enter или двойной клик — вставить (одиночное или мульти).
/// </summary>
class HistoryPanel : Form
{
    readonly AppContext _c;
    readonly TextBox _search = null!;
    readonly ListBox _list = null!;
    readonly Button _btnOcr = null!;
    readonly Button _btnTr = null!;
    readonly Button _btnPaste = null!;
    readonly Button _btnFile = null!;
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
            IntegralHeight = false,
            SelectionMode = SelectionMode.MultiExtended // нативные Ctrl/Shift Windows
        };
        _list.DrawItem += List_DrawItem;
        _list.HandleCreated += (s, e) => Native.ScrollbarTheme(_list.Handle);
        _list.DoubleClick += (s, e) => PasteSelected();
        _list.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Delete)
            {
                foreach (var it in SelectedEntries) _c.Store.Remove(it);
                e.Handled = e.SuppressKeyPress = true;
            }
        };

        // нижняя панель кнопок действий над выделением
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = Theme.Panel, Padding = new Padding(6, 5, 6, 5) };
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Panel,
            WrapContents = false
        };
        _btnOcr = BarButton("Распознать", 88);
        _btnOcr.Click += (s, e) => OcrSelected();
        _btnTr = BarButton("Перевести", 82);
        _btnTr.Click += (s, e) => TranslateSelected();
        _btnPaste = BarButton("Вставить", 78);
        _btnPaste.Click += (s, e) => PasteSelected();
        _btnFile = BarButton("В файл", 68);
        _btnFile.Click += (s, e) => FileSelected();
        var btnCancel = BarButton("Отмена", 70);
        btnCancel.Click += (s, e) => HideLive();
        bar.Controls.AddRange(new Control[] { _btnOcr, _btnTr, _btnPaste, _btnFile, btnCancel });
        bottom.Controls.Add(bar);

        Controls.Add(_list);
        Controls.Add(top);
        Controls.Add(bottom);

        _list.SelectedIndexChanged += (s, e) => UpdateActions();
        UpdateActions();

        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Escape) { HideLive(); return; }
            if (ActiveControl == _search)
            {
                // Enter в поиске = вставить выделенный (по умолчанию верхний результат)
                if (e.KeyCode == Keys.Enter && SelectedEntries.Count > 0)
                {
                    e.Handled = e.SuppressKeyPress = true;
                    PasteSelected();
                }
                return; // в поиске печатают — L/T не перехватываем
            }
            if (e.Control && e.KeyCode == Keys.A)
            {
                e.Handled = e.SuppressKeyPress = true;
                SelectAll();
                return;
            }
            if (e.KeyCode == Keys.Enter && SelectedEntries.Count > 0)
            {
                e.Handled = e.SuppressKeyPress = true;
                PasteSelected();
                return;
            }
            if (e.KeyCode == Keys.L && _btnOcr.Enabled) { e.Handled = e.SuppressKeyPress = true; OcrSelected(); }
            if (e.KeyCode == Keys.T && _btnTr.Enabled) { e.Handled = e.SuppressKeyPress = true; TranslateSelected(); }
        };
        // правый клик — закрыть (для режима «пока не закроют»)
        MouseClick += (s, e) => { if (e.Button == MouseButtons.Right) HideLive(); };
        _list.MouseClick += (s, e) => { if (e.Button == MouseButtons.Right) HideLive(); };
        _life.Tick += (s, e) => TickLife();
        _c.Store.Changed += OnStoreChanged;

        // окно можно таскать за фоны: подложку поиска и подложку кнопок
        // (клик по самому полю/кнопкам работает как обычно — MouseDown до них не доходит)
        Ui.MakeDraggable(this, top, bottom, bar);
    }

    Button BarButton(string text, int width) => new Button
    {
        Text = text,
        Width = width,
        Height = 29,
        FlatStyle = FlatStyle.Flat,
        BackColor = Theme.Button,
        ForeColor = Theme.Text,
        Margin = new Padding(2, 0, 2, 0),
        Cursor = Cursors.Hand,
        TabStop = false,
        Enabled = false
    };

    // видимость панели — по настройке PanelHideMode (см. Ui.PanelShouldHide)
    void TickLife()
    {
        if (Ui.PanelShouldHide(this, _c.S, _leftAt, out var left))
        {
            HideLive();
            return;
        }
        _leftAt = left;
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

    List<Entry> SelectedEntries
    {
        get
        {
            var res = new List<Entry>();
            foreach (int i in _list.SelectedIndices)
                if (i >= 0 && i < _view.Count) res.Add(_view[i]);
            return res;
        }
    }

    void SelectAll()
    {
        _list.BeginUpdate();
        for (int i = 0; i < _view.Count; i++) _list.SetSelected(i, true);
        _list.EndUpdate();
    }

    void UpdateActions()
    {
        var sel = SelectedEntries;
        _btnOcr.Enabled = sel.Any(x => x is { Kind: EntryKind.Image, ImageFile: { } }) && Ocr.IsConfigured(_c.S);
        _btnTr.Enabled = sel.Any(x => x is { Kind: EntryKind.Text, Text.Length: > 0 });
        _btnPaste.Enabled = _btnFile.Enabled = sel.Count > 0;
    }

    void OcrSelected()
    {
        var img = SelectedEntries.FirstOrDefault(x => x is { Kind: EntryKind.Image, ImageFile: { } });
        if (img == null) return;
        try
        {
            var bmp = new Bitmap(img.ImageFile!);
            // окно, вызвавшее распознавание, закрывается — открывается окно распознавания
            HideLive();
            _c.ShowOcrForm(bmp);
        }
        catch (Exception ex) { Log.Error("history ocr open", ex); }
    }

    void TranslateSelected()
    {
        var texts = SelectedEntries
            .Where(x => x is { Kind: EntryKind.Text, Text.Length: > 0 })
            .Select(x => x.Text)
            .ToList();
        if (texts.Count == 0) return;
        HideLive();
        _c.ShowTranslatorWithText(string.Join("\n", texts));
    }

    void FileSelected()
    {
        var sel = SelectedEntries;
        if (sel.Count == 0) return;
        try
        {
            SaveHelper.SaveEntries(this, sel);
        }
        catch (Exception ex)
        {
            Log.Error("save to file", ex);
            MessageBox.Show(this, "Не удалось сохранить: " + ex.Message, "В файл",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // новый элемент появился, пока панель открыта — перестраиваем, сохраняя выделение
    void OnStoreChanged()
    {
        if (!Visible) return;
        var selIds = new HashSet<string>(SelectedEntries.Select(x => x.Id));
        Repopulate();
        for (int i = 0; i < _view.Count; i++)
            if (selIds.Contains(_view[i].Id)) _list.SetSelected(i, true);
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
        int content = 46 + _view.Count * _list.ItemHeight + 40 + 8;
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
        UpdateActions();
    }

    void PasteSelected()
    {
        var sel = SelectedEntries;
        if (sel.Count == 0) return;
        if (sel.Count == 1) Paster.Paste(_c, sel[0]);
        else Paster.PasteMulti(_c, sel);
    }

    static readonly Font TsFont = new("Segoe UI", 7.5f);

    void List_DrawItem(object? s, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        bool zebra = e.Index % 2 == 1; // чередование строк: фон и слегка его затемнённая версия
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
