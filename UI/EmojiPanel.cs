using Copeer.Core;

namespace Copeer.UI;

/// <summary>Всплывающая панель эмодзи и специальных символов (Win+.).</summary>
class EmojiPanel : Form
{
    readonly AppContext _c;
    readonly TextBox _search;
    readonly FlowLayoutPanel _chips;
    readonly FlowLayoutPanel _grid;
    readonly List<(Button Btn, EmojiData.Sym Sym)> _buttons = new();
    readonly Dictionary<EmojiData.Sym, Button> _symButtons = new();
    readonly List<Button> _orderedButtons = new(); // видимый порядок после фильтра — для навигации стрелками
    readonly List<Button> _chipButtons = new();
    readonly List<Image> _emojiImages = new(); // цветные PNG (Twemoji) — освобождаются при закрытии

    static readonly string EmojiDir = Path.Combine(Application.StartupPath, "emoji");
    readonly ToolTip _tip = new();
    string _cat = "Все";
    DateTime _shownAt;

    public EmojiPanel(AppContext c)
    {
        _c = c;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Theme.Back;
        KeyPreview = true;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(432, 428);

        var top = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = Theme.Back, Padding = new Padding(10, 8, 10, 4) };
        _search = new TextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 10.5f)
        };
        _search.TextChanged += (s, e) => ApplyFilter();
        _search.KeyDown += (s, e) =>
        {
            // Down из поиска — к первой кнопке сетки
            if (e.KeyCode == Keys.Down && _orderedButtons.Count > 0)
            {
                e.Handled = e.SuppressKeyPress = true;
                _orderedButtons[0].Focus();
            }
        };
        top.Controls.Add(_search);

        _chips = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,          // высота по содержимому: категории переносятся на 2-ю строку, ничего не обрезается
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Back,
            Padding = new Padding(8, 2, 8, 2),
            WrapContents = true
        };
        foreach (var cat in EmojiData.Categories)
        {
            var b = new Button
            {
                Text = cat,
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Chip,
                ForeColor = Theme.Text,
                Font = new Font("Segoe UI", 9f),
                Margin = new Padding(2, 4, 2, 2),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            b.FlatAppearance.BorderColor = Theme.Border;
            b.Click += (s, e) => { _cat = cat; MarkChips(); ApplyFilter(); };
            _chips.Controls.Add(b);
            _chipButtons.Add(b);
        }

        _grid = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Theme.Back,
            Padding = new Padding(6)
        };
        _grid.HandleCreated += (s, e) => Native.ScrollbarTheme(_grid.Handle);
        foreach (var sym in EmojiData.All)
        {
            var b = new Button
            {
                Width = 38,
                Height = 38,
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Panel,
                ForeColor = Theme.Text,
                Margin = new Padding(1),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            b.FlatAppearance.BorderSize = 0;

            // GDI-шрифт не рисует цветные эмодзи — используем готовые PNG (Twemoji),
            // а для символов без ассета (тире, стрелки…) остаётся шрифтовой глиф
            var imgPath = EmojiImagePath(sym.Ch);
            if (imgPath != null)
            {
                try
                {
                    using var src = new Bitmap(imgPath);
                    var img = new Bitmap(src, 28, 28);
                    _emojiImages.Add(img);
                    b.Image = img;
                    b.ImageAlign = ContentAlignment.MiddleCenter;
                }
                catch { }
            }
            if (b.Image == null)
            {
                b.Text = sym.Ch;
                b.Font = new Font(sym.Cat == "Символы" ? "Segoe UI" : "Segoe UI Emoji", 15f);
            }

            b.Click += (s, e) =>
            {
                _c.AddRecentSymbol(sym.Ch);
                Paster.PasteText(_c, sym.Ch);
            };
            _tip.SetToolTip(b, sym.Name);
            _grid.Controls.Add(b);
            _buttons.Add((b, sym));
            _symButtons[sym] = b;
        }

        Controls.Add(_grid);
        Controls.Add(_chips);
        Controls.Add(top);

        Deactivate += (s, e) => { if ((DateTime.UtcNow - _shownAt).TotalMilliseconds > 300) Hide(); };
        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Escape) { Hide(); return; }
            // стрелки между кнопками сетки, когда фокус на одной из них
            if (ActiveControl is Button && _grid.Contains(ActiveControl))
                MoveGridFocus(e);
        };
        MarkChips();
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ClassStyle |= 0x00020000; return cp; } // CS_DROPSHADOW
    }

    public void Open()
    {
        _search.Text = "";
        _cat = "Все";
        MarkChips();
        ApplyFilter();

        Ui.PlacePopup(this, _c.S);

        _shownAt = DateTime.UtcNow;
        Show();
        Activate();
        _search.Focus();
        Ui.RetryFocus(this);
    }

    void MoveGridFocus(KeyEventArgs e)
    {
        if (_orderedButtons.Count == 0) return;
        int idx = ActiveControl is Button b ? _orderedButtons.IndexOf(b) : -1;
        if (idx < 0) { _orderedButtons[0].Focus(); return; }

        int columns = Math.Max(1, (_grid.ClientSize.Width - _grid.Padding.Horizontal) / 40);
        int target = e.KeyCode switch
        {
            Keys.Left => idx - 1,
            Keys.Right => idx + 1,
            Keys.Up => idx - columns,
            Keys.Down => idx + columns,
            Keys.Home => 0,
            Keys.End => _orderedButtons.Count - 1,
            _ => idx
        };
        if (target >= 0 && target < _orderedButtons.Count)
        {
            e.Handled = e.SuppressKeyPress = true;
            _orderedButtons[target].Focus();
        }
    }

    void MarkChips()
    {
        for (int i = 0; i < _chipButtons.Count; i++)
            _chipButtons[i].BackColor = EmojiData.Categories[i] == _cat ? Theme.Accent : Theme.Chip;
    }

    void ApplyFilter()
    {
        _grid.SuspendLayout();
        var q = _search.Text.Trim();
        var ordered = new List<EmojiData.Sym>();

        if (q.Length == 0 && _cat == "Все")
            foreach (var ch in _c.S.RecentSymbols)
            {
                var f = EmojiData.Find(ch);
                if (f != null && !ordered.Contains(f)) ordered.Add(f);
            }

        foreach (var s in EmojiData.All)
        {
            if (_cat != "Все" && s.Cat != _cat) continue;
            if (q.Length > 0 && !s.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            if (!ordered.Contains(s)) ordered.Add(s);
        }

        var visible = new HashSet<EmojiData.Sym>(ordered);
        for (int i = 0; i < ordered.Count; i++)
        {
            var pair = _buttons.FirstOrDefault(b => b.Sym == ordered[i]);
            if (pair.Btn != null) _grid.Controls.SetChildIndex(pair.Btn, i);
        }
        foreach (var (btn, sym) in _buttons) btn.Visible = visible.Contains(sym);

        _orderedButtons.Clear();
        foreach (var s in ordered)
            if (_symButtons.TryGetValue(s, out var b)) _orderedButtons.Add(b);

        _grid.ResumeLayout();
    }

    // twemoji-файл эмодзи: кодпоинты в hex через '-', без FE0F; нет файла — рисуем шрифтом
    static string? EmojiImagePath(string ch)
    {
        var parts = new List<string>();
        foreach (var r in ch.EnumerateRunes())
            if (r.Value != 0xFE0F) parts.Add(r.Value.ToString("x"));
        if (parts.Count == 0) return null;
        var p = Path.Combine(EmojiDir, string.Join("-", parts) + ".png");
        return File.Exists(p) ? p : null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tip.Dispose();
            foreach (var img in _emojiImages) img.Dispose();
            _emojiImages.Clear();
        }
        base.Dispose(disposing);
    }
}
