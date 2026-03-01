using System.Text.Json;

namespace SoundBox.Audio
{
    public enum PortKind { Audio, Control }
    public enum PortDirection { Input, Output }

    public class Port
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public PortDirection Direction { get; set; }
        public PortKind Kind { get; set; }
        public float[] Buffer { get; set; } = Array.Empty<float>();

        // Parameter properties (for input control ports)
        public bool IsParameter { get; set; }
        public float BaseValue { get; set; }
        public float Min { get; set; }
        public float Max { get; set; }
        public string Unit { get; set; } = "";
        public string[]? Labels { get; set; }  // Display names for integer-indexed values

        public string DisplayValue
        {
            get
            {
                int idx = (int)MathF.Round(BaseValue);
                if (Labels != null && idx >= 0 && idx < Labels.Length)
                    return Labels[idx];
                float range = Max - Min;
                string fmt = range >= 100 ? "F0" : range >= 5 ? "F1" : "F2";
                return BaseValue.ToString(fmt) + Unit;
            }
        }

        // Connection: for input ports, points to the output port supplying data
        public Port? Source { get; set; }

        public Color PortColor => Kind == PortKind.Audio
            ? Color.FromArgb(0, 212, 170)   // Teal
            : Color.FromArgb(255, 140, 50);  // Orange

        public void EnsureBuffer(int size)
        {
            if (Buffer.Length < size) Buffer = new float[size];
        }

        // Read a sample: if connected, read from source; else use base value
        public float Read(int i)
        {
            if (Source?.Buffer != null && i < Source.Buffer.Length)
                return Source.Buffer[i];
            if (IsParameter) return BaseValue;
            return (Buffer != null && i < Buffer.Length) ? Buffer[i] : 0f;
        }

        // Get modulated parameter value: base + source, clamped
        public float Modulated(int i)
        {
            float v = BaseValue;
            if (Source?.Buffer != null && i < Source.Buffer.Length)
                v += Source.Buffer[i];
            return Math.Clamp(v, Min, Max);
        }
    }

    public abstract class AudioNode : IDisposable
    {
        private static int _nextPortId = 1;
        public static void SetNextPortId(int id) { if (id > _nextPortId) _nextPortId = id; }

        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string TypeName { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public Color AccentColor { get; set; } = Color.FromArgb(0, 212, 170);

        public List<Port> Inputs { get; } = new();
        public List<Port> Outputs { get; } = new();

        protected Port AddInput(string name, PortKind kind = PortKind.Audio)
        {
            var p = new Port { Id = _nextPortId++, Name = name, Direction = PortDirection.Input, Kind = kind };
            Inputs.Add(p);
            return p;
        }

        protected Port AddOutput(string name, PortKind kind = PortKind.Audio)
        {
            var p = new Port { Id = _nextPortId++, Name = name, Direction = PortDirection.Output, Kind = kind };
            Outputs.Add(p);
            return p;
        }

        protected Port AddParameter(string name, float min, float max, float def, string unit = "")
        {
            var p = new Port
            {
                Id = _nextPortId++, Name = name, Direction = PortDirection.Input,
                Kind = PortKind.Control, IsParameter = true,
                BaseValue = def, Min = min, Max = max, Unit = unit
            };
            Inputs.Add(p);
            return p;
        }

        protected Port AddParameter(string name, float min, float max, float def, string[] labels)
        {
            var p = new Port
            {
                Id = _nextPortId++, Name = name, Direction = PortDirection.Input,
                Kind = PortKind.Control, IsParameter = true,
                BaseValue = def, Min = min, Max = max, Labels = labels
            };
            Inputs.Add(p);
            return p;
        }

        public void EnsureBuffers(int blockSize)
        {
            foreach (var p in Outputs) p.EnsureBuffer(blockSize);
            foreach (var p in Inputs)
                if (!p.IsParameter && p.Source == null) p.EnsureBuffer(blockSize);
        }

        public abstract void Process(int blockSize, int sampleRate);
        public virtual void Initialize(int sampleRate) { }
        public virtual void Dispose() { }

        // Find a port by Id across inputs and outputs
        public Port? FindPort(int portId) =>
            Inputs.FirstOrDefault(p => p.Id == portId) ??
            Outputs.FirstOrDefault(p => p.Id == portId);
    }

    public class Connection
    {
        public int SrcNodeId { get; set; }
        public int SrcPortId { get; set; }
        public int DstNodeId { get; set; }
        public int DstPortId { get; set; }
    }

    public class AudioGraph
    {
        private readonly List<AudioNode> _nodes = new();
        private readonly List<Connection> _connections = new();
        private List<AudioNode>? _sorted;
        private int _sampleRate = 48000;
        private int _nextNodeId = 1;

        public string Name { get; set; } = "Default";
        public IReadOnlyList<AudioNode> Nodes => _nodes;
        public IReadOnlyList<Connection> Connections => _connections;
        public int SampleRate { get => _sampleRate; set => _sampleRate = value; }

        // Events
        public event Action? GraphChanged;

        public T AddNode<T>(float x = 0, float y = 0) where T : AudioNode, new()
        {
            var node = new T { Id = _nextNodeId++, X = x, Y = y };
            _nodes.Add(node);
            node.Initialize(_sampleRate);
            _sorted = null;
            GraphChanged?.Invoke();
            return node;
        }

        public AudioNode AddNode(AudioNode node, float x = 0, float y = 0)
        {
            node.Id = _nextNodeId++;
            node.X = x;
            node.Y = y;
            _nodes.Add(node);
            node.Initialize(_sampleRate);
            _sorted = null;
            GraphChanged?.Invoke();
            return node;
        }

        public void RemoveNode(AudioNode node)
        {
            // Remove all connections involving this node
            _connections.RemoveAll(c => c.SrcNodeId == node.Id || c.DstNodeId == node.Id);
            foreach (var n in _nodes)
                foreach (var p in n.Inputs)
                    if (p.Source != null && node.Outputs.Contains(p.Source))
                        p.Source = null;
            node.Dispose();
            _nodes.Remove(node);
            _sorted = null;
            GraphChanged?.Invoke();
        }

        public AudioNode? FindNode(int id) => _nodes.FirstOrDefault(n => n.Id == id);

        public bool Connect(int srcNodeId, int srcPortId, int dstNodeId, int dstPortId)
        {
            var srcNode = FindNode(srcNodeId);
            var dstNode = FindNode(dstNodeId);
            if (srcNode == null || dstNode == null) return false;

            var srcPort = srcNode.Outputs.FirstOrDefault(p => p.Id == srcPortId);
            var dstPort = dstNode.Inputs.FirstOrDefault(p => p.Id == dstPortId);
            if (srcPort == null || dstPort == null) return false;

            // Audio ports can only connect to audio ports
            if (dstPort.Kind == PortKind.Audio && srcPort.Kind != PortKind.Audio) return false;

            // Disconnect existing
            dstPort.Source = null;
            _connections.RemoveAll(c => c.DstNodeId == dstNodeId && c.DstPortId == dstPortId);

            dstPort.Source = srcPort;
            _connections.Add(new Connection
            {
                SrcNodeId = srcNodeId, SrcPortId = srcPortId,
                DstNodeId = dstNodeId, DstPortId = dstPortId
            });
            _sorted = null;
            GraphChanged?.Invoke();
            return true;
        }

        public void Disconnect(int dstNodeId, int dstPortId)
        {
            var dstNode = FindNode(dstNodeId);
            var dstPort = dstNode?.Inputs.FirstOrDefault(p => p.Id == dstPortId);
            if (dstPort != null) dstPort.Source = null;
            _connections.RemoveAll(c => c.DstNodeId == dstNodeId && c.DstPortId == dstPortId);
            _sorted = null;
            GraphChanged?.Invoke();
        }

        public void DisconnectPort(int nodeId, int portId)
        {
            // Remove connections where this port is source or dest
            var node = FindNode(nodeId);
            if (node == null) return;
            var port = node.FindPort(portId);
            if (port == null) return;

            if (port.Direction == PortDirection.Output)
            {
                // Disconnect all inputs connected to this output
                foreach (var n in _nodes)
                    foreach (var p in n.Inputs)
                        if (p.Source == port) p.Source = null;
                _connections.RemoveAll(c => c.SrcNodeId == nodeId && c.SrcPortId == portId);
            }
            else
            {
                port.Source = null;
                _connections.RemoveAll(c => c.DstNodeId == nodeId && c.DstPortId == portId);
            }
            _sorted = null;
            GraphChanged?.Invoke();
        }

        public void Process(float[] inputBuffer, int totalSamples, int channels = 1)
        {
            int frameCount = totalSamples / Math.Max(1, channels);
            if (_sorted == null) TopologicalSort();

            // Feed input buffer to InputNode(s) - deinterleaves stereo
            foreach (var node in _nodes)
                if (node is InputNode inp) inp.SetInputBuffer(inputBuffer, totalSamples, channels);

            // Process in topological order (per-frame, mono signals)
            foreach (var node in _sorted!)
            {
                node.EnsureBuffers(frameCount);
                try { node.Process(frameCount, _sampleRate); } catch { }
            }
            // Outputs are read by the engine from each OutputNode individually
        }

        public IEnumerable<OutputNode> GetOutputNodes() => _nodes.OfType<OutputNode>();

        private void TopologicalSort()
        {
            var sorted = new List<AudioNode>();
            var visited = new HashSet<int>();
            var visiting = new HashSet<int>();

            void Visit(AudioNode node)
            {
                if (visited.Contains(node.Id)) return;
                if (visiting.Contains(node.Id)) return; // cycle
                visiting.Add(node.Id);
                foreach (var input in node.Inputs)
                {
                    if (input.Source != null)
                    {
                        var src = _nodes.FirstOrDefault(n => n.Outputs.Contains(input.Source));
                        if (src != null) Visit(src);
                    }
                }
                visiting.Remove(node.Id);
                visited.Add(node.Id);
                sorted.Add(node);
            }

            foreach (var node in _nodes) Visit(node);
            _sorted = sorted;
        }

        // Serialization
        public GraphData SaveToData()
        {
            var data = new GraphData { Name = Name, NextNodeId = _nextNodeId };
            foreach (var node in _nodes)
            {
                var nd = new NodeData
                {
                    Id = node.Id, TypeName = node.TypeName,
                    X = node.X, Y = node.Y, Params = new()
                };
                foreach (var p in node.Inputs.Where(p => p.IsParameter))
                    nd.Params[p.Name] = p.BaseValue;
                // Save port IDs for robust serialization
                nd.InputPortIds = node.Inputs.Select(p => p.Id).ToList();
                nd.OutputPortIds = node.Outputs.Select(p => p.Id).ToList();
                // Save string properties (e.g. OutputNode device)
                if (node is OutputNode outNode)
                {
                    if (!string.IsNullOrEmpty(outNode.DeviceId))
                        nd.Props["DeviceId"] = outNode.DeviceId;
                    if (!string.IsNullOrEmpty(outNode.DeviceName))
                        nd.Props["DeviceName"] = outNode.DeviceName;
                }
                data.Nodes.Add(nd);
            }
            foreach (var c in _connections)
                data.Connections.Add(new ConnData
                {
                    SrcNode = c.SrcNodeId, SrcPort = c.SrcPortId,
                    DstNode = c.DstNodeId, DstPort = c.DstPortId
                });
            return data;
        }

        public void LoadFromData(GraphData data)
        {
            // Clear existing
            foreach (var n in _nodes) n.Dispose();
            _nodes.Clear();
            _connections.Clear();
            _sorted = null;

            int maxPortId = 0;
            foreach (var nd in data.Nodes)
            {
                var node = NodeFactory.Create(nd.TypeName);
                if (node == null) continue;
                node.Id = nd.Id;
                node.X = nd.X;
                node.Y = nd.Y;

                // Restore port IDs if saved
                if (nd.InputPortIds.Count > 0)
                {
                    for (int i = 0; i < Math.Min(nd.InputPortIds.Count, node.Inputs.Count); i++)
                    {
                        node.Inputs[i].Id = nd.InputPortIds[i];
                        if (nd.InputPortIds[i] > maxPortId) maxPortId = nd.InputPortIds[i];
                    }
                }
                if (nd.OutputPortIds.Count > 0)
                {
                    for (int i = 0; i < Math.Min(nd.OutputPortIds.Count, node.Outputs.Count); i++)
                    {
                        node.Outputs[i].Id = nd.OutputPortIds[i];
                        if (nd.OutputPortIds[i] > maxPortId) maxPortId = nd.OutputPortIds[i];
                    }
                }

                _nodes.Add(node);
                node.Initialize(_sampleRate);
                // Restore string properties
                if (node is OutputNode outNode && nd.Props.Count > 0)
                {
                    outNode.DeviceId = nd.Props.GetValueOrDefault("DeviceId", "");
                    outNode.DeviceName = nd.Props.GetValueOrDefault("DeviceName", "");
                }
                foreach (var kv in nd.Params)
                {
                    var port = node.Inputs.FirstOrDefault(p => p.IsParameter && p.Name == kv.Key);
                    if (port != null) port.BaseValue = kv.Value;
                }

                // Track max port ID from current ports
                foreach (var p in node.Inputs) if (p.Id > maxPortId) maxPortId = p.Id;
                foreach (var p in node.Outputs) if (p.Id > maxPortId) maxPortId = p.Id;
            }
            Name = data.Name ?? "Default";
            _nextNodeId = data.NextNodeId;
            AudioNode.SetNextPortId(maxPortId + 1);

            foreach (var cd in data.Connections)
                Connect(cd.SrcNode, cd.SrcPort, cd.DstNode, cd.DstPort);
        }

        // Create default graph
        public void CreateDefault()
        {
            var input = AddNode<InputNode>(50, 200);
            var gain = AddNode<GainNode>(300, 180);
            var output = AddNode<OutputNode>(550, 200);

            // Connect: Input -> Gain -> Output
            if (input.Outputs.Count > 0 && gain.Inputs.Count > 0)
                Connect(input.Id, input.Outputs[0].Id, gain.Id, gain.Inputs[0].Id);
            if (gain.Outputs.Count > 0 && output.Inputs.Count > 0)
                Connect(gain.Id, gain.Outputs[0].Id, output.Id, output.Inputs[0].Id);
        }
    }

    // Serialization DTOs
    public class GraphData
    {
        public string Name { get; set; } = "Default";
        public List<NodeData> Nodes { get; set; } = new();
        public List<ConnData> Connections { get; set; } = new();
        public int NextNodeId { get; set; } = 1;
    }

    public class PresetsFile
    {
        public List<GraphData> Presets { get; set; } = new();
        public int ActiveIndex { get; set; }
        public bool AutoSwitch { get; set; } = true;
    }

    public class NodeData
    {
        public int Id { get; set; }
        public string TypeName { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public Dictionary<string, float> Params { get; set; } = new();
        public List<int> InputPortIds { get; set; } = new();
        public List<int> OutputPortIds { get; set; } = new();
        public Dictionary<string, string> Props { get; set; } = new();
    }

    public class ConnData
    {
        public int SrcNode { get; set; }
        public int SrcPort { get; set; }
        public int DstNode { get; set; }
        public int DstPort { get; set; }
    }

    // Factory for creating nodes by type name
    public static class NodeFactory
    {
        public static readonly (string TypeName, string DisplayName, Color Color)[] NodeTypes = new[]
        {
            ("Input",        "Input",        Color.FromArgb(0, 212, 170)),
            ("Output",       "Output",       Color.FromArgb(0, 212, 170)),
            ("Gain",         "Gain",         Color.FromArgb(0, 212, 170)),
            ("NoiseGate",    "Noise Gate",   Color.FromArgb(230, 190, 40)),
            ("Reverb",       "Reverb",       Color.FromArgb(255, 140, 50)),
            ("Compressor",   "Compressor",   Color.FromArgb(160, 100, 240)),
            ("AutoTune",     "Auto-Tune",    Color.FromArgb(240, 80, 160)),
            ("PitchShift",   "Pitch Shift",  Color.FromArgb(240, 80, 160)),
            ("Pan",          "Pan",          Color.FromArgb(80, 180, 255)),
            ("Vocoder",      "Vocoder",      Color.FromArgb(200, 60, 200)),
            ("Wah",          "Wah",          Color.FromArgb(255, 100, 100)),
            ("AutoWah",      "Auto-Wah",     Color.FromArgb(255, 100, 100)),
            ("Oscillator",   "Oscillator",   Color.FromArgb(100, 220, 100)),
            ("PowerDetect",  "Power Detect", Color.FromArgb(255, 200, 80)),
            ("Mixer",        "Mixer",        Color.FromArgb(150, 150, 170)),
            ("MathAdd",      "Add",          Color.FromArgb(120, 200, 230)),
            ("MathSub",      "Subtract",     Color.FromArgb(120, 200, 230)),
            ("MathMul",      "Multiply",     Color.FromArgb(120, 200, 230)),
            ("Clip",         "Clip",         Color.FromArgb(120, 200, 230)),
            ("Splitter",     "Splitter",     Color.FromArgb(150, 150, 170)),
            ("StereoMerge",  "Stereo Merge", Color.FromArgb(80, 180, 255)),
            ("StereoSplit",  "Stereo Split", Color.FromArgb(80, 180, 255)),
            ("Constant",     "Constant",     Color.FromArgb(180, 180, 200)),
        };

        public static AudioNode? Create(string typeName)
        {
            return typeName switch
            {
                "Input"       => new InputNode(),
                "Output"      => new OutputNode(),
                "Gain"        => new GainNode(),
                "NoiseGate"   => new NoiseGateNode(),
                "Reverb"      => new ReverbNode(),
                "Compressor"  => new CompressorNode(),
                "AutoTune"    => new AutoTuneNode(),
                "PitchShift"  => new PitchShiftNode(),
                "Pan"         => new PanNode(),
                "Vocoder"     => new VocoderNode(),
                "Wah"         => new WahNode(),
                "AutoWah"     => new AutoWahNode(),
                "Oscillator"  => new OscillatorNode(),
                "PowerDetect" => new PowerDetectNode(),
                "Mixer"       => new MixerNode(),
                "MathAdd"     => new MathAddNode(),
                "MathSub"     => new MathSubNode(),
                "MathMul"     => new MathMulNode(),
                "Clip"        => new ClipNode(),
                "Splitter"    => new SplitterNode(),
                "StereoMerge" => new StereoMergeNode(),
                "StereoSplit" => new StereoSplitNode(),
                "Constant"    => new ConstantNode(),
                _ => null
            };
        }
    }
}
