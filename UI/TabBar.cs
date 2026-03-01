using System.Drawing.Text;

namespace SoundBox.UI
{
    public class TabBar : Control
    {
        private readonly List<string> _tabNames = new();
        private int _selectedIndex;
        private int _activeIndex;
        private int _hoverIndex = -1;

        private const int TabH = 26;
        private const int TabMinW = 80;
        private const int TabMaxW = 160;
        private const int AddBtnW = 28;
        private const int CloseW = 16;

        public int SelectedIndex
        {
            get => _selectedIndex;
            set { _selectedIndex = Math.Clamp(value, 0, Math.Max(0, _tabNames.Count - 1)); Invalidate(); }
        }
        public int ActiveIndex
        {
            get => _activeIndex;
            set { _activeIndex = Math.Clamp(value, 0, Math.Max(0, _tabNames.Count - 1)); Invalidate(); }
        }
        public int TabCount => _tabNames.Count;

        public event Action<int>? TabSelected;      // single click
        public event Action<int>? TabActivated;     // double click
        public event Action? TabAdded;
        public event Action<int>? TabClosed;
        public event Action<int, string>? TabRenamed;

        public TabBar()
        {
            DoubleBuffered = true;
            Height = TabH;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(20, 20, 26);
        }

        public void SetTabs(IEnumerable<string> names)
        {
            _tabNames.Clear();
            _tabNames.AddRange(names);
            Invalidate();
        }

        public void SetTabName(int index, string name)
        {
            if (index >= 0 && index < _tabNames.Count)
            {
                _tabNames[index] = name;
                Invalidate();
            }
        }

        private int CalcTabWidth()
        {
            if (_tabNames.Count == 0) return TabMinW;
            int avail = Width - AddBtnW - 4;
            int w = avail / _tabNames.Count;
            return Math.Clamp(w, TabMinW, TabMaxW);
        }

        private Rectangle GetTabRect(int index)
        {
            int w = CalcTabWidth();
            return new Rectangle(index * w + 2, 0, w, TabH);
        }

        private Rectangle GetAddBtnRect()
        {
            int w = CalcTabWidth();
            return new Rectangle(_tabNames.Count * w + 4, 2, AddBtnW - 4, TabH - 4);
        }

        private Rectangle GetCloseRect(Rectangle tabRect)
        {
            return new Rectangle(tabRect.Right - CloseW - 4, tabRect.Y + 6, CloseW, CloseW);
        }

        // ==========================================================
        // PAINTING
        // ==========================================================
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // Background
            using (var bg = new SolidBrush(Color.FromArgb(20, 20, 26)))
                g.FillRectangle(bg, ClientRectangle);

            // Bottom border line
            using (var pen = new Pen(DarkTheme.Border))
                g.DrawLine(pen, 0, Height - 1, Width, Height - 1);

            for (int i = 0; i < _tabNames.Count; i++)
            {
                var rect = GetTabRect(i);
                bool isSelected = i == _selectedIndex;
                bool isActive = i == _activeIndex;
                bool isHover = i == _hoverIndex;

                // Tab background
                Color bgColor;
                if (isSelected)
                    bgColor = Color.FromArgb(40, 40, 50);
                else if (isHover)
                    bgColor = Color.FromArgb(30, 30, 38);
                else
                    bgColor = Color.FromArgb(22, 22, 28);
                using (var brush = new SolidBrush(bgColor))
                    g.FillRectangle(brush, rect);

                // Active indicator: accent underline for the processing tab
                if (isActive)
                {
                    using var accent = new SolidBrush(DarkTheme.Accent);
                    g.FillRectangle(accent, rect.X, rect.Bottom - 3, rect.Width, 3);
                }

                // Selected top highlight (blue stripe) when not the active tab
                if (isSelected && !isActive)
                {
                    using var sel = new SolidBrush(Color.FromArgb(80, 180, 255));
                    g.FillRectangle(sel, rect.X, rect.Y, rect.Width, 2);
                }

                // Tab text
                var textColor = isActive && isSelected ? DarkTheme.TextBright
                              : isActive ? DarkTheme.Accent
                              : isSelected ? DarkTheme.TextBright
                              : DarkTheme.TextDim;
                using (var tb = new SolidBrush(textColor))
                {
                    var textRect = new RectangleF(rect.X + 6, rect.Y + 6, rect.Width - CloseW - 10, rect.Height - 8);
                    using var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                    g.DrawString(_tabNames[i], DarkTheme.SmallFont, tb, textRect, sf);
                }

                // Close button (only if more than 1 tab)
                if (_tabNames.Count > 1)
                {
                    var closeRect = GetCloseRect(rect);
                    using var cb = new SolidBrush(DarkTheme.TextMuted);
                    g.DrawString("\u00d7", DarkTheme.SmallFont, cb, closeRect.X, closeRect.Y - 1);
                }

                // Right separator
                using (var pen = new Pen(Color.FromArgb(40, 40, 50)))
                    g.DrawLine(pen, rect.Right - 1, rect.Y + 4, rect.Right - 1, rect.Bottom - 4);
            }

            // "+" add button
            var addRect = GetAddBtnRect();
            using (var ab = new SolidBrush(DarkTheme.TextDim))
                g.DrawString("+", DarkTheme.ModuleTitle, ab, addRect.X + 6, addRect.Y + 2);
        }

        // ==========================================================
        // MOUSE
        // ==========================================================
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                // Add button
                if (GetAddBtnRect().Contains(e.Location))
                {
                    TabAdded?.Invoke();
                    return;
                }

                // Close buttons
                for (int i = 0; i < _tabNames.Count; i++)
                {
                    if (_tabNames.Count > 1)
                    {
                        var closeRect = GetCloseRect(GetTabRect(i));
                        if (closeRect.Contains(e.Location))
                        {
                            TabClosed?.Invoke(i);
                            return;
                        }
                    }
                }

                // Tab click
                for (int i = 0; i < _tabNames.Count; i++)
                {
                    if (GetTabRect(i).Contains(e.Location))
                    {
                        TabSelected?.Invoke(i);
                        return;
                    }
                }
            }

            if (e.Button == MouseButtons.Right)
            {
                for (int i = 0; i < _tabNames.Count; i++)
                {
                    if (GetTabRect(i).Contains(e.Location))
                    {
                        ShowTabMenu(e.Location, i);
                        return;
                    }
                }
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                for (int i = 0; i < _tabNames.Count; i++)
                {
                    if (GetTabRect(i).Contains(e.Location))
                    {
                        TabActivated?.Invoke(i);
                        return;
                    }
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int old = _hoverIndex;
            _hoverIndex = -1;
            for (int i = 0; i < _tabNames.Count; i++)
            {
                if (GetTabRect(i).Contains(e.Location))
                {
                    _hoverIndex = i;
                    break;
                }
            }
            if (old != _hoverIndex) Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverIndex = -1;
            Invalidate();
        }

        // ==========================================================
        // TAB CONTEXT MENU
        // ==========================================================
        private void ShowTabMenu(Point pos, int tabIndex)
        {
            var menu = new ContextMenuStrip();
            menu.BackColor = Color.FromArgb(36, 36, 44);
            menu.ForeColor = Color.FromArgb(220, 220, 230);
            menu.Renderer = new DarkMenuRenderer();

            var rename = new ToolStripMenuItem("Rename");
            rename.Click += (_, _) => ShowRenameEditor(tabIndex);
            menu.Items.Add(rename);

            if (_tabNames.Count > 1)
            {
                var del = new ToolStripMenuItem("Delete");
                del.ForeColor = DarkTheme.Danger;
                del.Click += (_, _) => TabClosed?.Invoke(tabIndex);
                menu.Items.Add(del);
            }

            menu.Show(this, pos);
        }

        private void ShowRenameEditor(int tabIndex)
        {
            var rect = GetTabRect(tabIndex);
            var box = new TextBox
            {
                Text = _tabNames[tabIndex],
                Font = DarkTheme.SmallFont,
                BackColor = Color.FromArgb(20, 20, 26),
                ForeColor = DarkTheme.TextBright,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(rect.X + 2, rect.Y + 2),
                Size = new Size(rect.Width - 4, rect.Height - 4)
            };
            box.SelectAll();
            TextBox? activeBox = box;

            void Commit()
            {
                if (activeBox == null) return;
                var b = activeBox;
                activeBox = null;
                string newName = b.Text.Trim();
                if (!string.IsNullOrEmpty(newName))
                    TabRenamed?.Invoke(tabIndex, newName);
                Controls.Remove(b);
                b.Dispose();
                Focus();
            }

            box.KeyDown += (_, ke) =>
            {
                if (ke.KeyCode == Keys.Enter) { Commit(); ke.SuppressKeyPress = true; }
                else if (ke.KeyCode == Keys.Escape)
                {
                    activeBox = null;
                    Controls.Remove(box);
                    box.Dispose();
                    Focus();
                    ke.SuppressKeyPress = true;
                }
            };
            box.LostFocus += (_, _) => Commit();
            Controls.Add(box);
            box.BringToFront();
            box.Focus();
        }
    }
}
