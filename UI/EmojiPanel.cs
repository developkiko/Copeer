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
    readonly List<Button> _chipButtons = new();
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
        top.Controls.Add(_search);

        _chips = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
            BackColor = Theme.Back,
            Padding = new Padding(8, 2, 8, 2),
            WrapContents = false
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
        foreach (var sym in EmojiData.All)
        {
            var b = new Button
            {
                Text = sym.Ch,
                Width = 38,
                Height = 38,
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Panel,
                ForeColor = Theme.Text,
                Font = new Font(sym.Cat == "Символы" ? "Segoe UI" : "Segoe UI Emoji", 15f),
                Margin = new Padding(1),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (s, e) =>
            {
                _c.AddRecentSymbol(sym.Ch);
                Paster.PasteText(_c, sym.Ch);
            };
            _tip.SetToolTip(b, sym.Name);
            _grid.Controls.Add(b);
            _buttons.Add((b, sym));
        }

        Controls.Add(_grid);
        Controls.Add(_chips);
        Controls.Add(top);

        Deactivate += (s, e) => { if ((DateTime.UtcNow - _shownAt).TotalMilliseconds > 300) Hide(); };
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Hide(); };
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
        _grid.ResumeLayout();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}
