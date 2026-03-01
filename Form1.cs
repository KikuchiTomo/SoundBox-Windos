using System.Text.Json;
using SoundBox.Audio;
using SoundBox.UI;

namespace SoundBox
{
    public partial class Form1 : Form
    {
        private readonly AudioEngine _engine = new();
        private readonly List<AudioGraph> _graphs = new();
        private int _activeIndex;       // which graph is being processed
        private int _selectedIndex;     // which graph is being edited (displayed)
        private bool _autoSwitch = true;

        private ComboBox _inputCombo = null!;
        private ComboBox _outputCombo = null!;
        private NodeEditor _nodeEditor = null!;
        private TabBar _tabBar = null!;
        private CheckBox _autoSwitchCheck = null!;
        private LevelMeter _meterL = null!;
        private LevelMeter _meterR = null!;
        private WaveformDisplay _waveform = null!;
        private Label _statusLabel = null!;

        private bool _dragging;
        private Point _dragOffset;

        private const int FormW = 1100;
        private const int FormH = 750;
        private const int TitleBarH = 38;
        private const int ToolBarH = 40;

        public Form1()
        {
            InitializeComponent();
            BuildUI();
            LoadPresets();

            _engine.SetGraph(_graphs[_activeIndex]);

            _engine.LevelUpdated += level =>
            {
                if (InvokeRequired) BeginInvoke(() => UpdateMeters(level));
                else UpdateMeters(level);
            };

            _engine.WaveformUpdated += samples =>
            {
                _waveform.PushSamples(samples, Math.Min(samples.Length, 512));
            };
        }

        private void BuildUI()
        {
            Text = "SoundBox";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(FormW, FormH);
            BackColor = DarkTheme.BgDark;
            DoubleBuffered = true;
            Padding = new Padding(1);

            Paint += (_, e) =>
            {
                using var pen = new Pen(Color.FromArgb(0x2E, 0x2E, 0x2E));
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            };

            // =============================================
            // TITLE BAR
            // =============================================
            var titleBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = TitleBarH,
                BackColor = Color.FromArgb(14, 14, 18)
            };
            titleBar.Paint += (_, e) =>
            {
                var g = e.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using var accent = new SolidBrush(DarkTheme.Accent);
                g.DrawString("SOUNDBOX", DarkTheme.TitleFont, accent, 12, 6);
                using var dim = new SolidBrush(DarkTheme.TextMuted);
                g.DrawString("Node Audio Processor", DarkTheme.SmallFont, dim, 162, 16);
                using var line = new Pen(DarkTheme.Border);
                g.DrawLine(line, 0, titleBar.Height - 1, titleBar.Width, titleBar.Height - 1);
            };
            titleBar.MouseDown += (_, e) => { _dragging = true; _dragOffset = e.Location; };
            titleBar.MouseMove += (_, e) =>
            {
                if (_dragging) Location = new Point(Location.X + e.X - _dragOffset.X, Location.Y + e.Y - _dragOffset.Y);
            };
            titleBar.MouseUp += (_, _) => _dragging = false;

            var closeBtn = MakeTitleBtn("\u2715", DarkTheme.Danger);
            closeBtn.Click += (_, _) => Close();
            var minBtn = MakeTitleBtn("\u2500", DarkTheme.TextDim);
            minBtn.Click += (_, _) => WindowState = FormWindowState.Minimized;
            titleBar.Controls.AddRange(new Control[] { closeBtn, minBtn });
            titleBar.Resize += (_, _) => { closeBtn.Left = titleBar.Width - 38; minBtn.Left = titleBar.Width - 76; };

            // =============================================
            // TOOLBAR
            // =============================================
            var toolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = ToolBarH,
                BackColor = Color.FromArgb(24, 24, 30)
            };

            // Input device
            var inputLabel = new Label
            {
                Text = "IN:", Font = DarkTheme.LabelFont, ForeColor = DarkTheme.TextDim,
                AutoSize = true, Location = new Point(8, 12), BackColor = Color.Transparent
            };
            _inputCombo = MakeCombo(130, 24);
            _inputCombo.Location = new Point(28, 9);
            PopulateInputDevices();

            // Output device
            var outputLabel = new Label
            {
                Text = "OUT:", Font = DarkTheme.LabelFont, ForeColor = DarkTheme.TextDim,
                AutoSize = true, Location = new Point(168, 12), BackColor = Color.Transparent
            };
            _outputCombo = MakeCombo(130, 24);
            _outputCombo.Location = new Point(196, 9);
            PopulateOutputDevices();

            // Transport buttons
            var playBtn = new TransportButton { Type = TransportType.Play, Location = new Point(340, 4), Size = new Size(32, 32) };
            playBtn.Click += OnPlay;
            var stopBtn = new TransportButton { Type = TransportType.Stop, Location = new Point(378, 4), Size = new Size(32, 32) };
            stopBtn.Click += OnStop;

            _statusLabel = new Label
            {
                Text = "STOPPED", Font = DarkTheme.ValueFont, ForeColor = DarkTheme.TextMuted,
                BackColor = Color.Transparent, AutoSize = true, Location = new Point(418, 12)
            };

            // Level meters (compact, horizontal)
            _meterL = new LevelMeter { Vertical = false, Location = new Point(500, 8), Size = new Size(200, 8) };
            _meterR = new LevelMeter { Vertical = false, Location = new Point(500, 20), Size = new Size(200, 8) };

            // Waveform
            _waveform = new WaveformDisplay { Location = new Point(710, 4), Size = new Size(160, 32) };

            // Auto Switch checkbox (right side of toolbar)
            _autoSwitchCheck = new CheckBox
            {
                Text = "Auto Switch",
                Font = DarkTheme.SmallFont,
                ForeColor = DarkTheme.TextDim,
                BackColor = Color.Transparent,
                AutoSize = true,
                Checked = true,
                Location = new Point(890, 12)
            };
            _autoSwitchCheck.CheckedChanged += (_, _) => _autoSwitch = _autoSwitchCheck.Checked;

            toolbar.Controls.AddRange(new Control[]
            {
                inputLabel, _inputCombo, outputLabel, _outputCombo,
                playBtn, stopBtn, _statusLabel, _meterL, _meterR, _waveform,
                _autoSwitchCheck
            });
            toolbar.Paint += (_, e) =>
            {
                using var pen = new Pen(DarkTheme.Border);
                e.Graphics.DrawLine(pen, 0, toolbar.Height - 1, toolbar.Width, toolbar.Height - 1);
            };

            // =============================================
            // TAB BAR
            // =============================================
            _tabBar = new TabBar
            {
                Dock = DockStyle.Top
            };
            _tabBar.TabSelected += OnTabSelected;
            _tabBar.TabActivated += OnTabActivated;
            _tabBar.TabAdded += OnTabAdded;
            _tabBar.TabClosed += OnTabClosed;
            _tabBar.TabRenamed += OnTabRenamed;

            // =============================================
            // NODE EDITOR (fills remaining space)
            // =============================================
            _nodeEditor = new NodeEditor
            {
                Dock = DockStyle.Fill
            };
            _nodeEditor.GraphModified += () => SavePresets();

            // =============================================
            // STATUS BAR
            // =============================================
            var statusBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 22,
                BackColor = Color.FromArgb(14, 14, 18)
            };
            statusBar.Paint += (_, e) =>
            {
                var g = e.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using var b = new SolidBrush(DarkTheme.TextMuted);
                bool sse = false, avx = false;
                try { sse = NativeDSP.SB_HasSSE2() != 0; avx = NativeDSP.SB_HasAVX2() != 0; } catch { }
                string simd = avx ? "AVX2" : sse ? "SSE2" : "Scalar";
                g.DrawString($"DSP: {simd}  |  FFTW3 (GPL v2)  |  Right-click to add nodes  |  SoundBox {AppVersion.Current}",
                    DarkTheme.SmallFont, b, 6, 4);
                using var pen = new Pen(DarkTheme.Border);
                g.DrawLine(pen, 0, 0, statusBar.Width, 0);
            };

            // ADD ORDER: Fill first, then Top/Bottom (WinForms dock Z-order)
            Controls.Add(_nodeEditor);
            Controls.Add(statusBar);
            Controls.Add(_tabBar);
            Controls.Add(toolbar);
            Controls.Add(titleBar);
        }

        // ============================================================
        // Tab events
        // ============================================================
        private void OnTabSelected(int index)
        {
            if (index < 0 || index >= _graphs.Count) return;
            _selectedIndex = index;
            _nodeEditor.Graph = _graphs[_selectedIndex];
            _tabBar.SelectedIndex = _selectedIndex;

            if (_autoSwitch)
            {
                _activeIndex = index;
                _engine.SetGraph(_graphs[_activeIndex]);
                _tabBar.ActiveIndex = _activeIndex;
            }
        }

        private void OnTabActivated(int index)
        {
            if (index < 0 || index >= _graphs.Count) return;
            // Double-click always activates for processing
            _activeIndex = index;
            _engine.SetGraph(_graphs[_activeIndex]);
            _tabBar.ActiveIndex = _activeIndex;
        }

        private void OnTabAdded()
        {
            var graph = new AudioGraph();
            graph.Name = $"Preset {_graphs.Count + 1}";
            graph.CreateDefault();
            _graphs.Add(graph);
            RefreshTabBar();

            // Select the new tab
            OnTabSelected(_graphs.Count - 1);
            SavePresets();
        }

        private void OnTabClosed(int index)
        {
            if (_graphs.Count <= 1) return;
            if (index < 0 || index >= _graphs.Count) return;

            // Dispose nodes in the closed graph
            foreach (var node in _graphs[index].Nodes)
                node.Dispose();
            _graphs.RemoveAt(index);

            // Adjust indices
            if (_activeIndex >= _graphs.Count)
                _activeIndex = _graphs.Count - 1;
            if (_activeIndex == index || _activeIndex > index)
                _activeIndex = Math.Max(0, _activeIndex > index ? _activeIndex - 1 : 0);

            if (_selectedIndex >= _graphs.Count)
                _selectedIndex = _graphs.Count - 1;
            else if (_selectedIndex > index)
                _selectedIndex--;

            _engine.SetGraph(_graphs[_activeIndex]);
            _nodeEditor.Graph = _graphs[_selectedIndex];
            RefreshTabBar();
            SavePresets();
        }

        private void OnTabRenamed(int index, string newName)
        {
            if (index < 0 || index >= _graphs.Count) return;
            _graphs[index].Name = newName;
            RefreshTabBar();
            SavePresets();
        }

        private void RefreshTabBar()
        {
            _tabBar.SetTabs(_graphs.Select(g => g.Name));
            _tabBar.SelectedIndex = _selectedIndex;
            _tabBar.ActiveIndex = _activeIndex;
        }

        // ============================================================
        // Events
        // ============================================================
        private void UpdateMeters(float level)
        {
            _meterL.Level = level;
            _meterR.Level = level * 0.95f;
        }

        private void OnPlay(object? sender, EventArgs e)
        {
            if (_engine.IsRunning) return;
            try
            {
                var inputId = (_inputCombo.SelectedItem as DeviceItem)?.Id;
                var outputId = (_outputCombo.SelectedItem as DeviceItem)?.Id;
                _engine.Start(inputId, outputId);
                _statusLabel.Text = "RUNNING";
                _statusLabel.ForeColor = DarkTheme.Accent;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnStop(object? sender, EventArgs e)
        {
            _engine.Stop();
            _statusLabel.Text = "STOPPED";
            _statusLabel.ForeColor = DarkTheme.TextMuted;
            _meterL.Level = 0;
            _meterR.Level = 0;
        }

        // ============================================================
        // Presets persistence
        // ============================================================
        private static string PresetsPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SoundBox", "presets.json");

        private static string LegacyGraphPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SoundBox", "graph.json");

        private void SavePresets()
        {
            try
            {
                var dir = Path.GetDirectoryName(PresetsPath)!;
                Directory.CreateDirectory(dir);

                var file = new PresetsFile
                {
                    ActiveIndex = _activeIndex,
                    AutoSwitch = _autoSwitch
                };
                foreach (var graph in _graphs)
                    file.Presets.Add(graph.SaveToData());

                var json = JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(PresetsPath, json);
            }
            catch { }
        }

        private void LoadPresets()
        {
            _graphs.Clear();

            // Try loading presets.json
            try
            {
                if (File.Exists(PresetsPath))
                {
                    var json = File.ReadAllText(PresetsPath);
                    var file = JsonSerializer.Deserialize<PresetsFile>(json);
                    if (file != null && file.Presets.Count > 0)
                    {
                        foreach (var data in file.Presets)
                        {
                            var graph = new AudioGraph();
                            graph.LoadFromData(data);
                            _graphs.Add(graph);
                        }
                        _activeIndex = Math.Clamp(file.ActiveIndex, 0, _graphs.Count - 1);
                        _selectedIndex = _activeIndex;
                        _autoSwitch = file.AutoSwitch;
                        _autoSwitchCheck.Checked = _autoSwitch;

                        _nodeEditor.Graph = _graphs[_selectedIndex];
                        RefreshTabBar();
                        return;
                    }
                }
            }
            catch { }

            // Migrate from legacy graph.json
            try
            {
                if (File.Exists(LegacyGraphPath))
                {
                    var json = File.ReadAllText(LegacyGraphPath);
                    var data = JsonSerializer.Deserialize<GraphData>(json);
                    if (data != null && data.Nodes.Count > 0)
                    {
                        data.Name = "Default";
                        var graph = new AudioGraph();
                        graph.LoadFromData(data);
                        _graphs.Add(graph);
                        _activeIndex = 0;
                        _selectedIndex = 0;

                        _nodeEditor.Graph = _graphs[0];
                        RefreshTabBar();
                        return;
                    }
                }
            }
            catch { }

            // Create default
            var defaultGraph = new AudioGraph();
            defaultGraph.Name = "Default";
            defaultGraph.CreateDefault();
            _graphs.Add(defaultGraph);
            _activeIndex = 0;
            _selectedIndex = 0;

            _nodeEditor.Graph = _graphs[0];
            RefreshTabBar();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SavePresets();
            _engine.Dispose();

            // Dispose all graph nodes
            foreach (var graph in _graphs)
                foreach (var node in graph.Nodes)
                    node.Dispose();

            base.OnFormClosing(e);
        }

        // ============================================================
        // Helpers
        // ============================================================
        private void PopulateInputDevices()
        {
            _inputCombo.Items.Clear();
            var devices = AudioEngine.GetInputDevices();
            foreach (var (id, name) in devices) _inputCombo.Items.Add(new DeviceItem(id, name));
            if (_inputCombo.Items.Count > 0) _inputCombo.SelectedIndex = 0;
        }

        private void PopulateOutputDevices()
        {
            _outputCombo.Items.Clear();
            var devices = AudioEngine.GetOutputDevices();
            foreach (var (id, name) in devices) _outputCombo.Items.Add(new DeviceItem(id, name));
            if (_outputCombo.Items.Count > 0) _outputCombo.SelectedIndex = 0;
        }

        private static ComboBox MakeCombo(int w, int h)
        {
            return new ComboBox
            {
                Size = new Size(w, h),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = DarkTheme.BgElevated,
                ForeColor = DarkTheme.TextNormal,
                FlatStyle = FlatStyle.Flat,
                Font = DarkTheme.SmallFont
            };
        }

        private static Label MakeTitleBtn(string text, Color hoverColor)
        {
            var b = new Label
            {
                Text = text, Font = new Font("Segoe UI", 10f),
                ForeColor = DarkTheme.TextMuted, BackColor = Color.Transparent,
                Size = new Size(38, TitleBarH), TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            b.MouseEnter += (_, _) => b.ForeColor = hoverColor;
            b.MouseLeave += (_, _) => b.ForeColor = DarkTheme.TextMuted;
            return b;
        }

        private class DeviceItem
        {
            public string Id { get; }
            public string Name { get; }
            public DeviceItem(string id, string name) { Id = id; Name = name; }
            public override string ToString() => Name;
        }
    }
}
