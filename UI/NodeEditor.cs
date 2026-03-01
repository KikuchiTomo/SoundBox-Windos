using System.Drawing.Drawing2D;
using System.Drawing.Text;
using SoundBox.Audio;

namespace SoundBox.UI
{
    public class NodeEditor : Control
    {
        private AudioGraph _graph = new();
        private readonly List<VisualNode> _visualNodes = new();

        // View transform
        private float _zoom = 1f;
        private PointF _pan = new(0, 0);

        // Interaction state
        private VisualNode? _selectedNode;
        private VisualNode? _dragNode;
        private PointF _dragStart;
        private PointF _dragNodeStart;

        // Connection dragging
        private bool _draggingConnection;
        private Port? _dragPort;
        private AudioNode? _dragPortNode;
        private PointF _dragEndPoint;

        // Middle-button panning
        private bool _panning;
        private PointF _panStart;
        private PointF _panStartOffset;

        // Parameter drag
        private Port? _paramDragPort;
        private float _paramDragStartValue;
        private Point _paramDragStartMouse;
        private RectangleF _paramSliderRect;

        // Inline text edit
        private TextBox? _editBox;
        private Port? _editPort;

        // Constants
        private const int NodeW = 180;
        private const int HeaderH = 26;
        private const int PortH = 20;
        private const int PortR = 5;
        private const int ParamSliderW = 60;

        public AudioGraph Graph
        {
            get => _graph;
            set
            {
                _graph = value;
                RebuildVisuals();
                Invalidate();
            }
        }

        public event Action? GraphModified;

        public NodeEditor()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(18, 18, 22);
        }

        public void RebuildVisuals()
        {
            _visualNodes.Clear();
            foreach (var node in _graph.Nodes)
            {
                int portCount = Math.Max(node.Inputs.Count, node.Outputs.Count);
                int h = HeaderH + portCount * PortH + 10;
                _visualNodes.Add(new VisualNode
                {
                    Node = node,
                    Bounds = new RectangleF(node.X, node.Y, NodeW, h)
                });
            }
        }

        // ============================================================
        // PAINTING
        // ============================================================
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // Background
            using (var bg = new SolidBrush(Color.FromArgb(18, 18, 22)))
                g.FillRectangle(bg, ClientRectangle);

            // Grid
            DrawGrid(g);

            // Apply view transform
            g.TranslateTransform(_pan.X, _pan.Y);
            g.ScaleTransform(_zoom, _zoom);

            // Draw connections first (behind nodes)
            DrawConnections(g);

            // Draw rubber band connection if dragging
            if (_draggingConnection && _dragPort != null && _dragPortNode != null)
            {
                var start = GetPortScreenPos(_dragPortNode, _dragPort);
                var end = ScreenToWorld(_dragEndPoint);
                DrawBezier(g, start, end, _dragPort.PortColor, 2f);
            }

            // Draw nodes
            foreach (var vn in _visualNodes)
                DrawNode(g, vn);
        }

        private void DrawGrid(Graphics g)
        {
            float gridSize = 30f * _zoom;
            float offsetX = _pan.X % gridSize;
            float offsetY = _pan.Y % gridSize;

            using var pen = new Pen(Color.FromArgb(25, 255, 255, 255), 1f);
            for (float x = offsetX; x < Width; x += gridSize)
                g.DrawLine(pen, x, 0, x, Height);
            for (float y = offsetY; y < Height; y += gridSize)
                g.DrawLine(pen, 0, y, Width, y);
        }

        private void DrawConnections(Graphics g)
        {
            foreach (var conn in _graph.Connections)
            {
                var srcNode = _graph.FindNode(conn.SrcNodeId);
                var dstNode = _graph.FindNode(conn.DstNodeId);
                if (srcNode == null || dstNode == null) continue;

                var srcPort = srcNode.Outputs.FirstOrDefault(p => p.Id == conn.SrcPortId);
                var dstPort = dstNode.Inputs.FirstOrDefault(p => p.Id == conn.DstPortId);
                if (srcPort == null || dstPort == null) continue;

                var start = GetPortWorldPos(srcNode, srcPort);
                var end = GetPortWorldPos(dstNode, dstPort);
                DrawBezier(g, start, end, srcPort.PortColor, 2f);
            }
        }

        private void DrawBezier(Graphics g, PointF start, PointF end, Color color, float width)
        {
            float dx = MathF.Abs(end.X - start.X) * 0.5f;
            dx = MathF.Max(dx, 50f);
            var cp1 = new PointF(start.X + dx, start.Y);
            var cp2 = new PointF(end.X - dx, end.Y);

            using var pen = new Pen(color, width);
            g.DrawBezier(pen, start, cp1, cp2, end);
        }

        private void DrawNode(Graphics g, VisualNode vn)
        {
            var node = vn.Node;
            var b = vn.Bounds;
            bool selected = vn == _selectedNode;

            // Node body
            using (var bodyBrush = new SolidBrush(Color.FromArgb(36, 36, 44)))
            {
                var path = RoundedRect(b, 6);
                g.FillPath(bodyBrush, path);
            }

            // Header
            using (var headerBrush = new SolidBrush(Color.FromArgb(
                (int)(node.AccentColor.R * 0.3f), (int)(node.AccentColor.G * 0.3f), (int)(node.AccentColor.B * 0.3f))))
            {
                var headerRect = new RectangleF(b.X, b.Y, b.Width, HeaderH);
                var hp = RoundedRectTop(headerRect, 6);
                g.FillPath(headerBrush, hp);
            }

            // Accent line
            using (var accent = new SolidBrush(node.AccentColor))
                g.FillRectangle(accent, b.X, b.Y + HeaderH - 2, b.Width, 2);

            // Title
            using (var tb = new SolidBrush(Color.FromArgb(240, 240, 245)))
                g.DrawString(node.Name, DarkTheme.ModuleTitle, tb, b.X + 8, b.Y + 5);

            // Border
            var borderColor = selected ? node.AccentColor : Color.FromArgb(60, 60, 72);
            using (var pen = new Pen(borderColor, selected ? 2f : 1f))
            {
                var path = RoundedRect(b, 6);
                g.DrawPath(pen, path);
            }

            // Ports
            float yOff = b.Y + HeaderH + 6;

            foreach (var port in node.Inputs)
            {
                var portPos = new PointF(b.X, yOff + PortH * node.Inputs.IndexOf(port));
                DrawPort(g, portPos, port, true, b.Width);
            }

            foreach (var port in node.Outputs)
            {
                var portPos = new PointF(b.X + b.Width, yOff + PortH * node.Outputs.IndexOf(port));
                DrawPort(g, portPos, port, false, b.Width);
            }
        }

        private void DrawPort(Graphics g, PointF pos, Port port, bool isInput, float nodeWidth)
        {
            float cx = pos.X;
            float cy = pos.Y + PortR;
            var color = port.PortColor;
            bool connected = port.Source != null || (!isInput && _graph.Connections.Any(c => c.SrcPortId == port.Id));

            // Port circle
            using (var brush = new SolidBrush(connected ? color : Color.FromArgb(60, color)))
                g.FillEllipse(brush, cx - PortR, cy - PortR, PortR * 2, PortR * 2);
            using (var pen = new Pen(color, 1f))
                g.DrawEllipse(pen, cx - PortR, cy - PortR, PortR * 2, PortR * 2);

            if (isInput && port.IsParameter)
            {
                // Draw slider for parameter
                var sr = CalcSliderRect(pos, nodeWidth);

                // Background
                using (var bg = new SolidBrush(Color.FromArgb(28, 28, 34)))
                    g.FillRectangle(bg, sr);

                // Fill bar
                float norm = Math.Clamp((port.BaseValue - port.Min) / (port.Max - port.Min), 0f, 1f);
                if (norm > 0.005f)
                {
                    int alpha = port.Source != null ? 40 : 90;
                    using var fill = new SolidBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
                    g.FillRectangle(fill, sr.X, sr.Y, sr.Width * norm, sr.Height);
                }

                // Border
                using (var pen2 = new Pen(Color.FromArgb(50, 50, 62)))
                    g.DrawRectangle(pen2, sr.X, sr.Y, sr.Width, sr.Height);

                // Value text
                string label = $"{port.Name}: {port.DisplayValue}";
                using var tb = new SolidBrush(Color.FromArgb(200, 200, 210));
                g.DrawString(label, DarkTheme.SmallFont, tb, sr.X + 3, sr.Y + 1);
            }
            else
            {
                // Normal port label
                using var textBrush = new SolidBrush(Color.FromArgb(190, 190, 200));
                if (isInput)
                {
                    g.DrawString(port.Name, DarkTheme.SmallFont, textBrush, cx + PortR + 4, cy - 6);
                }
                else
                {
                    var sz = g.MeasureString(port.Name, DarkTheme.SmallFont);
                    g.DrawString(port.Name, DarkTheme.SmallFont, textBrush, cx - PortR - sz.Width - 4, cy - 6);
                }
            }
        }

        // ============================================================
        // MOUSE INTERACTION
        // ============================================================
        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();

            if (e.Button == MouseButtons.Middle)
            {
                _panning = true;
                _panStart = e.Location;
                _panStartOffset = _pan;
                Cursor = Cursors.SizeAll;
                return;
            }

            var worldPos = ScreenToWorld(e.Location);

            if (e.Button == MouseButtons.Right)
            {
                // Check if right-clicking a connected port
                foreach (var vn in _visualNodes.AsEnumerable().Reverse())
                {
                    var (port, isOutput) = HitTestPort(vn, worldPos);
                    if (port != null)
                    {
                        bool hasConn = isOutput
                            ? _graph.Connections.Any(c => c.SrcNodeId == vn.Node.Id && c.SrcPortId == port.Id)
                            : port.Source != null;
                        if (hasConn)
                        {
                            ShowPortMenu(e.Location, vn.Node, port);
                            return;
                        }
                    }
                }

                // Check if right-clicking inside an OutputNode → device selection
                foreach (var vn in _visualNodes.AsEnumerable().Reverse())
                {
                    if (vn.Node is OutputNode outNode && vn.Bounds.Contains(worldPos))
                    {
                        ShowOutputDeviceMenu(e.Location, outNode);
                        return;
                    }
                }

                ShowContextMenu(e.Location, worldPos);
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                // Check slider click first (parameter adjustment when not connected)
                foreach (var vn in _visualNodes.AsEnumerable().Reverse())
                {
                    var (param, sliderRect) = HitTestSlider(vn, worldPos);
                    if (param != null)
                    {
                        float norm = (worldPos.X - sliderRect.X) / sliderRect.Width;
                        norm = Math.Clamp(norm, 0f, 1f);
                        param.BaseValue = param.Min + (param.Max - param.Min) * norm;
                        _paramDragPort = param;
                        _paramSliderRect = sliderRect;
                        _paramDragStartValue = param.BaseValue;
                        _paramDragStartMouse = e.Location;
                        GraphModified?.Invoke();
                        Invalidate();
                        return;
                    }
                }

                // Check port click (connection dragging for all ports)
                foreach (var vn in _visualNodes.AsEnumerable().Reverse())
                {
                    var (port, isOutput) = HitTestPort(vn, worldPos);
                    if (port != null)
                    {
                        _draggingConnection = true;
                        _dragPort = port;
                        _dragPortNode = vn.Node;
                        _dragEndPoint = e.Location;
                        return;
                    }
                }

                // Check node click
                foreach (var vn in _visualNodes.AsEnumerable().Reverse())
                {
                    if (vn.Bounds.Contains(worldPos))
                    {
                        _selectedNode = vn;
                        _dragNode = vn;
                        _dragStart = worldPos;
                        _dragNodeStart = new PointF(vn.Bounds.X, vn.Bounds.Y);
                        Invalidate();
                        return;
                    }
                }

                _selectedNode = null;
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_panning)
            {
                _pan = new PointF(
                    _panStartOffset.X + (e.X - _panStart.X),
                    _panStartOffset.Y + (e.Y - _panStart.Y));
                Invalidate();
                return;
            }

            if (_draggingConnection)
            {
                _dragEndPoint = e.Location;
                Invalidate();
                return;
            }

            if (_dragNode != null)
            {
                var worldPos = ScreenToWorld(e.Location);
                float dx = worldPos.X - _dragStart.X;
                float dy = worldPos.Y - _dragStart.Y;
                _dragNode.Bounds = new RectangleF(
                    _dragNodeStart.X + dx, _dragNodeStart.Y + dy,
                    _dragNode.Bounds.Width, _dragNode.Bounds.Height);
                _dragNode.Node.X = _dragNode.Bounds.X;
                _dragNode.Node.Y = _dragNode.Bounds.Y;
                Invalidate();
                return;
            }

            if (_paramDragPort != null)
            {
                var worldPos = ScreenToWorld(e.Location);
                float norm = (worldPos.X - _paramSliderRect.X) / _paramSliderRect.Width;
                norm = Math.Clamp(norm, 0f, 1f);
                float val = _paramDragPort.Min + (_paramDragPort.Max - _paramDragPort.Min) * norm;
                // Snap to integer for labeled (enum) parameters
                if (_paramDragPort.Labels != null)
                    val = MathF.Round(val);
                _paramDragPort.BaseValue = Math.Clamp(val, _paramDragPort.Min, _paramDragPort.Max);
                GraphModified?.Invoke();
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_panning)
            {
                _panning = false;
                Cursor = Cursors.Default;
                return;
            }

            if (_draggingConnection && _dragPort != null && _dragPortNode != null)
            {
                var worldPos = ScreenToWorld(e.Location);

                // Find target port
                foreach (var vn in _visualNodes)
                {
                    var (port, isOutput) = HitTestPort(vn, worldPos);
                    if (port != null && port != _dragPort && vn.Node != _dragPortNode)
                    {
                        bool dragIsOutput = _dragPort.Direction == PortDirection.Output;
                        if (dragIsOutput && port.Direction == PortDirection.Input)
                        {
                            _graph.Connect(_dragPortNode.Id, _dragPort.Id, vn.Node.Id, port.Id);
                            GraphModified?.Invoke();
                        }
                        else if (!dragIsOutput && port.Direction == PortDirection.Output)
                        {
                            _graph.Connect(vn.Node.Id, port.Id, _dragPortNode.Id, _dragPort.Id);
                            GraphModified?.Invoke();
                        }
                    }
                }

                _draggingConnection = false;
                _dragPort = null;
                _dragPortNode = null;
                Invalidate();
                return;
            }

            _dragNode = null;
            _paramDragPort = null;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            float oldZoom = _zoom;
            _zoom *= e.Delta > 0 ? 1.1f : 0.9f;
            _zoom = Math.Clamp(_zoom, 0.3f, 3f);

            // Zoom toward mouse position
            float factor = _zoom / oldZoom;
            _pan = new PointF(
                e.X - (e.X - _pan.X) * factor,
                e.Y - (e.Y - _pan.Y) * factor);

            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var worldPos = ScreenToWorld(e.Location);

            foreach (var vn in _visualNodes.AsEnumerable().Reverse())
            {
                var (param, sliderRect) = HitTestSlider(vn, worldPos);
                if (param != null)
                {
                    ShowParamEditor(param, sliderRect);
                    return;
                }
            }
        }

        private void ShowParamEditor(Port port, RectangleF sliderWorldRect)
        {
            // Close existing editor
            CloseParamEditor(false);

            _editPort = port;

            // Convert world rect to screen coords
            var topLeft = WorldToScreen(new PointF(sliderWorldRect.X, sliderWorldRect.Y));
            var botRight = WorldToScreen(new PointF(sliderWorldRect.Right, sliderWorldRect.Bottom));
            int w = Math.Max(40, (int)(botRight.X - topLeft.X));
            int h = Math.Max(18, (int)(botRight.Y - topLeft.Y));

            var box = new TextBox
            {
                Text = port.Labels != null
                    ? ((int)MathF.Round(port.BaseValue)).ToString()
                    : port.BaseValue.ToString("G5"),
                Font = DarkTheme.SmallFont,
                BackColor = Color.FromArgb(20, 20, 26),
                ForeColor = Color.FromArgb(240, 240, 245),
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point((int)topLeft.X, (int)topLeft.Y),
                Size = new Size(w, h),
                TextAlign = HorizontalAlignment.Center
            };
            box.SelectAll();
            box.KeyDown += (_, ke) =>
            {
                if (ke.KeyCode == Keys.Enter) { CloseParamEditor(true); ke.SuppressKeyPress = true; }
                else if (ke.KeyCode == Keys.Escape) { CloseParamEditor(false); ke.SuppressKeyPress = true; }
            };
            box.LostFocus += (_, _) => CloseParamEditor(true);

            _editBox = box;
            Controls.Add(box);
            box.BringToFront();
            box.Focus();
        }

        private void CloseParamEditor(bool apply)
        {
            if (_editBox == null || _editPort == null) return;

            // Grab references and null out fields FIRST to prevent reentrancy
            var box = _editBox;
            var port = _editPort;
            _editBox = null;
            _editPort = null;

            if (apply && float.TryParse(box.Text, out float val))
            {
                port.BaseValue = Math.Clamp(val, port.Min, port.Max);
                GraphModified?.Invoke();
            }

            Controls.Remove(box);
            box.Dispose();
            Invalidate();
            Focus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete && _selectedNode != null)
            {
                // Don't delete Input/Output nodes
                if (_selectedNode.Node is not InputNode && _selectedNode.Node is not OutputNode)
                {
                    _graph.RemoveNode(_selectedNode.Node);
                    _visualNodes.Remove(_selectedNode);
                    _selectedNode = null;
                    RebuildVisuals();
                    GraphModified?.Invoke();
                    Invalidate();
                }
            }
        }

        // ============================================================
        // HIT TESTING
        // ============================================================
        private static RectangleF CalcSliderRect(PointF portPos, float nodeWidth)
        {
            return new RectangleF(
                portPos.X + PortR * 2 + 2, portPos.Y + 2,
                nodeWidth - PortR * 2 - 10, PortH - 4);
        }

        private (Port? port, RectangleF rect) HitTestSlider(VisualNode vn, PointF worldPos)
        {
            float yOff = vn.Bounds.Y + HeaderH + 6;
            for (int i = 0; i < vn.Node.Inputs.Count; i++)
            {
                var port = vn.Node.Inputs[i];
                if (!port.IsParameter || port.Source != null) continue;
                var portPos = new PointF(vn.Bounds.X, yOff + i * PortH);
                var rect = CalcSliderRect(portPos, vn.Bounds.Width);
                if (rect.Contains(worldPos))
                    return (port, rect);
            }
            return (null, RectangleF.Empty);
        }

        private (Port? port, bool isOutput) HitTestPort(VisualNode vn, PointF worldPos)
        {
            float yOff = vn.Bounds.Y + HeaderH + 6;

            for (int i = 0; i < vn.Node.Inputs.Count; i++)
            {
                float cx = vn.Bounds.X;
                float cy = yOff + i * PortH + PortR;
                if (Distance(worldPos, new PointF(cx, cy)) < PortR + 6)
                    return (vn.Node.Inputs[i], false);
            }

            for (int i = 0; i < vn.Node.Outputs.Count; i++)
            {
                float cx = vn.Bounds.X + vn.Bounds.Width;
                float cy = yOff + i * PortH + PortR;
                if (Distance(worldPos, new PointF(cx, cy)) < PortR + 6)
                    return (vn.Node.Outputs[i], true);
            }

            return (null, false);
        }

        private static float Distance(PointF a, PointF b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        // ============================================================
        // CONTEXT MENU
        // ============================================================
        private void ShowContextMenu(Point screenPos, PointF worldPos)
        {
            var menu = new ContextMenuStrip();
            menu.BackColor = Color.FromArgb(36, 36, 44);
            menu.ForeColor = Color.FromArgb(220, 220, 230);
            menu.Renderer = new DarkMenuRenderer();

            foreach (var (typeName, displayName, color) in NodeFactory.NodeTypes)
            {
                // Skip Input if one already exists (only one input device)
                if (typeName == "Input" && _graph.Nodes.Any(n => n is InputNode)) continue;

                var item = new ToolStripMenuItem(displayName);
                item.ForeColor = color;
                string tn = typeName;
                float wx = worldPos.X, wy = worldPos.Y;
                item.Click += (_, _) =>
                {
                    var node = NodeFactory.Create(tn);
                    if (node != null)
                    {
                        _graph.AddNode(node, wx, wy);
                        RebuildVisuals();
                        GraphModified?.Invoke();
                        Invalidate();
                    }
                };
                menu.Items.Add(item);
            }

            // Separator + Delete for selected node
            if (_selectedNode != null && _selectedNode.Node is not InputNode && _selectedNode.Node is not OutputNode)
            {
                menu.Items.Add(new ToolStripSeparator());
                var del = new ToolStripMenuItem("Delete Node");
                del.ForeColor = Color.FromArgb(255, 70, 70);
                del.Click += (_, _) =>
                {
                    _graph.RemoveNode(_selectedNode.Node);
                    _visualNodes.Remove(_selectedNode);
                    _selectedNode = null;
                    RebuildVisuals();
                    GraphModified?.Invoke();
                    Invalidate();
                };
                menu.Items.Add(del);
            }

            menu.Show(this, screenPos);
        }

        private void ShowOutputDeviceMenu(Point screenPos, OutputNode outNode)
        {
            var menu = new ContextMenuStrip();
            menu.BackColor = Color.FromArgb(36, 36, 44);
            menu.ForeColor = Color.FromArgb(220, 220, 230);
            menu.Renderer = new DarkMenuRenderer();

            var header = new ToolStripMenuItem("Select Output Device") { Enabled = false };
            menu.Items.Add(header);
            menu.Items.Add(new ToolStripSeparator());

            var devices = AudioEngine.GetOutputDevices();
            foreach (var (id, name) in devices)
            {
                var item = new ToolStripMenuItem(name);
                if (outNode.DeviceId == id)
                    item.Checked = true;
                string devId = id, devName = name;
                item.Click += (_, _) =>
                {
                    outNode.DeviceId = devId;
                    outNode.DeviceName = devName;
                    RebuildVisuals();
                    GraphModified?.Invoke();
                    Invalidate();
                };
                menu.Items.Add(item);
            }

            menu.Show(this, screenPos);
        }

        private void ShowPortMenu(Point screenPos, AudioNode node, Port port)
        {
            var menu = new ContextMenuStrip();
            menu.BackColor = Color.FromArgb(36, 36, 44);
            menu.ForeColor = Color.FromArgb(220, 220, 230);
            menu.Renderer = new DarkMenuRenderer();

            var disconnect = new ToolStripMenuItem("Disconnect");
            disconnect.ForeColor = Color.FromArgb(255, 140, 50);
            disconnect.Click += (_, _) =>
            {
                _graph.DisconnectPort(node.Id, port.Id);
                GraphModified?.Invoke();
                Invalidate();
            };
            menu.Items.Add(disconnect);

            menu.Show(this, screenPos);
        }

        // ============================================================
        // COORDINATE TRANSFORMS
        // ============================================================
        private PointF ScreenToWorld(PointF screen) => ScreenToWorld(new Point((int)screen.X, (int)screen.Y));
        private PointF ScreenToWorld(Point screen)
        {
            return new PointF(
                (screen.X - _pan.X) / _zoom,
                (screen.Y - _pan.Y) / _zoom);
        }

        private PointF WorldToScreen(PointF world)
        {
            return new PointF(
                world.X * _zoom + _pan.X,
                world.Y * _zoom + _pan.Y);
        }

        private PointF GetPortWorldPos(AudioNode node, Port port)
        {
            var vn = _visualNodes.FirstOrDefault(v => v.Node == node);
            if (vn == null) return PointF.Empty;
            return GetPortPos(vn, port);
        }

        private PointF GetPortScreenPos(AudioNode node, Port port)
        {
            return GetPortWorldPos(node, port);
        }

        private PointF GetPortPos(VisualNode vn, Port port)
        {
            float yOff = vn.Bounds.Y + HeaderH + 6;

            int idx = vn.Node.Inputs.IndexOf(port);
            if (idx >= 0)
                return new PointF(vn.Bounds.X, yOff + idx * PortH + PortR);

            idx = vn.Node.Outputs.IndexOf(port);
            if (idx >= 0)
                return new PointF(vn.Bounds.X + vn.Bounds.Width, yOff + idx * PortH + PortR);

            return PointF.Empty;
        }

        // ============================================================
        // HELPERS
        // ============================================================
        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static GraphicsPath RoundedRectTop(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
            path.CloseFigure();
            return path;
        }

        private class VisualNode
        {
            public AudioNode Node { get; set; } = null!;
            public RectangleF Bounds { get; set; }
        }
    }

    // Dark context menu renderer
    internal class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColors()) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var r = new Rectangle(Point.Empty, e.Item.Size);
            var color = e.Item.Selected ? Color.FromArgb(50, 50, 60) : Color.FromArgb(36, 36, 44);
            using var brush = new SolidBrush(color);
            e.Graphics.FillRectangle(brush, r);
        }
    }

    internal class DarkMenuColors : ProfessionalColorTable
    {
        public override Color MenuBorder => Color.FromArgb(60, 60, 72);
        public override Color MenuItemBorder => Color.FromArgb(60, 60, 72);
        public override Color MenuItemSelected => Color.FromArgb(50, 50, 60);
        public override Color MenuStripGradientBegin => Color.FromArgb(36, 36, 44);
        public override Color MenuStripGradientEnd => Color.FromArgb(36, 36, 44);
        public override Color ToolStripDropDownBackground => Color.FromArgb(36, 36, 44);
        public override Color ImageMarginGradientBegin => Color.FromArgb(36, 36, 44);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(36, 36, 44);
        public override Color ImageMarginGradientEnd => Color.FromArgb(36, 36, 44);
        public override Color SeparatorDark => Color.FromArgb(60, 60, 72);
        public override Color SeparatorLight => Color.FromArgb(60, 60, 72);
    }
}
