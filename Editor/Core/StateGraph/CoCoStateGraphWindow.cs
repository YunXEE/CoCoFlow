using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoCoFlow.Editor.Core.StateGraph
{
    public sealed class CoCoStateGraphWindow : EditorWindow
    {
        private readonly CoCoStateGraphRenderSettings _renderSettings =
            new CoCoStateGraphRenderSettings();

        private CoCoStateController _targetController;
        private CoCoStateGraphView _graphView;
        private ScrollView _detailsView;
        private CoCoStateGraphModel _model;
        private CoCoStateGraphSelection _selection;
        private ToolbarToggle _soloToggle;

        [MenuItem("CoCoFlow/State/State Graph Viewer")]
        public static void ShowWindow()
        {
            var window = GetWindow<CoCoStateGraphWindow>("State Graph");
            window.minSize = new Vector2(980f, 600f);
        }

        private void CreateGUI()
        {
            rootVisualElement.style.flexDirection = FlexDirection.Column;
            rootVisualElement.style.backgroundColor = new Color(0.11f, 0.11f, 0.11f);

            var controllerField = BuildToolbar();
            rootVisualElement.Add(BuildBody());
            DrawEmptyDetails("Select a CoCoStateController, then click Reanalyze.");

            if (_targetController == null && Selection.activeGameObject != null)
            {
                _targetController = Selection.activeGameObject.GetComponentInParent<CoCoStateController>();
                controllerField.value = _targetController;
            }
        }

        private ObjectField BuildToolbar()
        {
            var toolbar = new Toolbar();

            var controllerField = new ObjectField("State Controller")
            {
                objectType = typeof(CoCoStateController),
                allowSceneObjects = true
            };
            controllerField.RegisterValueChangedCallback(evt =>
            {
                _targetController = evt.newValue as CoCoStateController;
            });
            toolbar.Add(controllerField);

            toolbar.Add(MakeButton("Reanalyze", RefreshGraph));
            toolbar.Add(MakeButton("Use Selection", () =>
            {
                _targetController = Selection.activeGameObject != null
                    ? Selection.activeGameObject.GetComponentInParent<CoCoStateController>()
                    : null;
                controllerField.value = _targetController;
                RefreshGraph();
            }));
            toolbar.Add(MakeButton("Fit View", () => _graphView?.FrameAllGraph()));

            toolbar.Add(MakeToggle(
                "Transitions",
                _renderSettings.ShowTransitions,
                value => _renderSettings.ShowTransitions = value));
            toolbar.Add(MakeToggle(
                "State Layers",
                _renderSettings.ShowStateLayerLinks,
                value => _renderSettings.ShowStateLayerLinks = value));
            toolbar.Add(MakeToggle(
                "Context Dependencies",
                _renderSettings.ShowContextDependencies,
                value => _renderSettings.ShowContextDependencies = value));
            toolbar.Add(MakeToggle(
                "Operations",
                _renderSettings.ShowOperations,
                value => _renderSettings.ShowOperations = value));
            toolbar.Add(MakeToggle(
                "Common Transitions",
                _renderSettings.ShowCommonTransitions,
                value => _renderSettings.ShowCommonTransitions = value));
            toolbar.Add(MakeSoloToggle());

            rootVisualElement.Add(toolbar);
            return controllerField;
        }

        private VisualElement BuildBody()
        {
            var body = new VisualElement
            {
                name = "state-graph-body"
            };
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1f;

            _graphView = new CoCoStateGraphView();
            _graphView.OnSelectionChanged += DrawSelectionDetails;
            _graphView.style.flexGrow = 1f;
            body.Add(_graphView);

            _detailsView = new ScrollView
            {
                name = "state-graph-details"
            };
            _detailsView.style.width = 380f;
            _detailsView.style.minWidth = 320f;
            _detailsView.style.maxWidth = 460f;
            _detailsView.style.paddingLeft = 10f;
            _detailsView.style.paddingRight = 10f;
            _detailsView.style.paddingTop = 10f;
            _detailsView.style.paddingBottom = 10f;
            _detailsView.style.backgroundColor = new Color(0.13f, 0.13f, 0.13f);
            body.Add(_detailsView);

            return body;
        }

        private Button MakeButton(string text, Action clicked)
        {
            return new Button(clicked)
            {
                text = text
            };
        }

        private ToolbarToggle MakeToggle(
            string text,
            bool value,
            Action<bool> changed)
        {
            var toggle = new ToolbarToggle
            {
                text = text,
                value = value
            };
            toggle.RegisterValueChangedCallback(evt =>
            {
                changed(evt.newValue);
                RedrawGraph();
            });
            return toggle;
        }

        private ToolbarToggle MakeSoloToggle()
        {
            _soloToggle = new ToolbarToggle
            {
                text = "Solo",
                value = false
            };
            _soloToggle.RegisterValueChangedCallback(evt =>
            {
                _graphView.SetSoloEnabled(evt.newValue);
            });
            return _soloToggle;
        }

        private void RefreshGraph()
        {
            _selection = null;
            if (_soloToggle != null)
            {
                _soloToggle.SetValueWithoutNotify(false);
            }

            _graphView.ResetViewState();
            _model = CoCoStateGraphAnalyzer.Analyze(_targetController);
            RedrawGraph();
        }

        private void RedrawGraph()
        {
            _graphView.Draw(_model, _renderSettings);
            if (_selection != null)
            {
                DrawSelectionDetails(_selection);
            }
            else
            {
                DrawSummaryDetails(_model);
            }
        }

        private void DrawSummaryDetails(CoCoStateGraphModel model)
        {
            _detailsView.Clear();
            AddGraphSummary(model);
        }

        private bool AddGraphSummary(CoCoStateGraphModel model)
        {
            if (model == null || model.RootController == null)
            {
                _detailsView.Add(MakeLabel("No graph data.", Color.gray));
                return false;
            }

            var stats = _graphView.LastRenderStats;
            AddHeader(_detailsView, "Graph Summary");
            _detailsView.Add(MakeLabel($"Controller: {model.RootController.name}", Color.white));
            _detailsView.Add(MakeLabel("Controllers: 1", Color.gray));
            _detailsView.Add(MakeLabel($"State Layers: {model.Layers.Count}", Color.gray));
            _detailsView.Add(MakeLabel($"States: {model.States.Count}", Color.gray));
            _detailsView.Add(MakeLabel($"Rendered nodes: {stats.TotalNodes}", Color.gray));
            _detailsView.Add(MakeLabel($"Rendered edges: {stats.TotalEdges}", Color.gray));

            AddHeader(_detailsView, "Edge Layers");
            _detailsView.Add(MakeLegendLabel("Transitions", stats.TransitionEdges, EdgeColors.Transition));
            _detailsView.Add(MakeLegendLabel("Reciprocal Pairs", stats.ReciprocalTransitionPairs, EdgeColors.Reciprocal));
            _detailsView.Add(MakeLegendLabel("State Layer Links", stats.LayerEdges, EdgeColors.StateLayer));
            _detailsView.Add(MakeLegendLabel("Layer State Links", stats.LayerStateEdges, EdgeColors.LayerState));
            _detailsView.Add(MakeLegendLabel("Context Reads", stats.ContextReadEdges, EdgeColors.ContextRead));
            _detailsView.Add(MakeLegendLabel("Context Writes", stats.ContextWriteEdges, EdgeColors.ContextWrite));
            _detailsView.Add(MakeLegendLabel("Operations", stats.OperationEdges, EdgeColors.Operation));
            if (stats.HiddenCommonTransitionEdges > 0)
            {
                _detailsView.Add(MakeLabel(
                    $"Hidden common transitions: {stats.HiddenCommonTransitionEdges}",
                    Color.gray));
            }

            AddUnresolvedTransitions(model);
            AddWarnings(model);
            return true;
        }

        private void DrawSelectionDetails(CoCoStateGraphSelection selection)
        {
            _selection = selection;
            _graphView.SetSoloSelection(selection);
            _detailsView.Clear();
            if (!AddGraphSummary(_model) || selection == null)
            {
                return;
            }

            AddHeader(_detailsView, "Selection");
            switch (selection.Kind)
            {
                case CoCoStateGraphSelectionKind.Controller:
                    AddControllerDetails(selection.Controller);
                    break;
                case CoCoStateGraphSelectionKind.Layer:
                    AddLayerDetails(selection.Layer);
                    break;
                case CoCoStateGraphSelectionKind.State:
                    AddStateDetails(selection.State);
                    break;
                case CoCoStateGraphSelectionKind.Context:
                    AddContextDetails(selection.Layer);
                    break;
                case CoCoStateGraphSelectionKind.Operation:
                    AddOperationDetails(selection.OperationType);
                    break;
                case CoCoStateGraphSelectionKind.Edge:
                    AddEdgeDetails(selection.Edge);
                    break;
                default:
                    DrawSummaryDetails(_model);
                    break;
            }
        }

        private void AddControllerDetails(CoCoStateController controller)
        {
            if (controller == null) return;

            AddHeader(_detailsView, "State Controller");
            _detailsView.Add(MakeLabel(controller.name, Color.white));
            _detailsView.Add(MakeLabel("Single controller for explicit State Layers", Color.gray));
            _detailsView.Add(MakeLabel($"State Layers: {controller.StateLayers.Count}", Color.gray));
            _detailsView.Add(MakeLabel($"Provider: {FormatObject(controller.ContextProvider)}", Color.gray));
        }

        private void AddLayerDetails(CoCoStateLayerNode layer)
        {
            if (layer == null) return;

            AddHeader(_detailsView, "State Layer");
            _detailsView.Add(MakeLabel(layer.Name, Color.white));
            _detailsView.Add(MakeLabel("Explicit State Layer", Color.gray));
            _detailsView.Add(MakeLabel($"Order: {layer.Order}", Color.gray));
            _detailsView.Add(MakeLabel($"Update: {layer.Updates}", Color.gray));
            _detailsView.Add(MakeLabel($"FixedUpdate: {layer.FixedUpdates}", Color.gray));
            _detailsView.Add(MakeLabel($"Default: {FormatState(layer.DefaultState)}", Color.gray));
            _detailsView.Add(MakeLabel($"Current: {FormatState(layer.CurrentState)}", Color.gray));
            _detailsView.Add(MakeLabel($"Provider: {FormatObject(layer.ContextProvider)}", Color.gray));
            _detailsView.Add(MakeLabel($"Context: {FormatType(layer.ContextType)}", Color.gray));
        }

        private void AddContextDetails(CoCoStateLayerNode layer)
        {
            if (layer == null) return;

            AddHeader(_detailsView, "Context");
            _detailsView.Add(MakeLabel($"Provider: {FormatObject(layer.ContextProvider)}", Color.white));
            _detailsView.Add(MakeLabel($"Type: {FormatType(layer.ContextType)}", Color.gray));

            if (layer.ContextSources.Count > 0)
            {
                AddHeader(_detailsView, "Sources");
                foreach (var source in layer.ContextSources)
                {
                    string priority = source.Priority.HasValue ? source.Priority.Value.ToString() : "-";
                    _detailsView.Add(MakeLabel($"{source.TypeName}  priority:{priority}", new Color(0.65f, 0.85f, 1f)));
                }
            }

            if (layer.ContextFields.Count > 0)
            {
                AddHeader(_detailsView, "Fields");
                foreach (var field in layer.ContextFields)
                {
                    var color = field.IsExtension
                        ? new Color(0.45f, 0.9f, 0.6f)
                        : Color.gray;
                    _detailsView.Add(MakeLabel(
                        $"{field.Path}: {field.TypeName} ({field.DeclaringTypeName})",
                        color));
                }
            }
        }

        private void AddStateDetails(CoCoStateNode state)
        {
            if (state == null) return;

            AddHeader(_detailsView, state.Name);
            _detailsView.Add(MakeLabel($"Type: {state.State.GetType().Name}", Color.white));
            _detailsView.Add(MakeLabel(state.IsCurrent ? "Current State" : state.IsDefault ? "Default State" : "State", Color.gray));
            _detailsView.Add(MakeLabel(state.HasDefinition ? "Definition declared" : "No declarations", state.HasDefinition ? Color.gray : new Color(1f, 0.75f, 0.25f)));

            AddTransitionTopologyDetails(state);

            AddHeader(_detailsView, "Context Dependencies");
            foreach (var dependency in state.Definition.ContextDependencies)
            {
                string access = dependency.Access == CoCoStateContextAccess.Read ? "R" : "W";
                var color = dependency.Access == CoCoStateContextAccess.Read
                    ? EdgeColors.ContextRead
                    : EdgeColors.ContextWrite;
                _detailsView.Add(MakeLabel($"[{access}] {FormatType(dependency.ContextType)}.{dependency.Path} {dependency.Note}", color));
            }

            AddHeader(_detailsView, "Operations");
            foreach (var operation in state.Definition.OperationDependencies)
            {
                _detailsView.Add(MakeLabel($"Op: {FormatType(operation.ComponentType)}  {operation.Usage}", EdgeColors.Operation));
            }

            AddHeader(_detailsView, "Transitions");
            foreach (var target in state.Definition.TransitionTargets)
            {
                _detailsView.Add(MakeLabel($"-> {FormatType(target.StateType)}  {target.Note}", EdgeColors.Transition));
            }
        }

        private void AddTransitionTopologyDetails(CoCoStateNode state)
        {
            int incoming = 0;
            int outgoing = 0;
            foreach (var transition in _model.Transitions)
            {
                if (!transition.TargetResolved) continue;
                if (transition.TargetStateId == state.Id) incoming++;
                if (transition.SourceStateId == state.Id) outgoing++;
            }

            AddHeader(_detailsView, "Transition Topology");
            _detailsView.Add(MakeLabel($"IN: {incoming}  OUT: {outgoing}", Color.gray));

            AddHeader(_detailsView, "Incoming");
            bool hasIncoming = false;
            foreach (var transition in _model.Transitions)
            {
                if (!transition.TargetResolved || transition.TargetStateId != state.Id) continue;

                hasIncoming = true;
                var source = FindStateNodeById(transition.SourceStateId);
                _detailsView.Add(MakeLabel(
                    $"{FormatStateNodeName(source)} -> {state.Name}  {transition.Note}",
                    EdgeColors.Transition));
            }

            if (!hasIncoming)
            {
                _detailsView.Add(MakeLabel("None", Color.gray));
            }

            AddHeader(_detailsView, "Outgoing");
            bool hasOutgoing = false;
            foreach (var transition in _model.Transitions)
            {
                if (!transition.TargetResolved || transition.SourceStateId != state.Id) continue;

                hasOutgoing = true;
                var target = FindStateNodeById(transition.TargetStateId);
                _detailsView.Add(MakeLabel(
                    $"{state.Name} -> {FormatStateNodeName(target)}  {transition.Note}",
                    EdgeColors.Transition));
            }

            if (!hasOutgoing)
            {
                _detailsView.Add(MakeLabel("None", Color.gray));
            }
        }

        private void AddOperationDetails(Type operationType)
        {
            if (operationType == null) return;

            AddHeader(_detailsView, "Operation");
            _detailsView.Add(MakeLabel(operationType.Name, Color.white));
            _detailsView.Add(MakeLabel(operationType.FullName, Color.gray));
        }

        private void AddEdgeDetails(CoCoStateGraphRenderEdge edge)
        {
            if (edge == null) return;

            AddHeader(_detailsView, FormatEdgeKind(edge.Kind));
            _detailsView.Add(MakeLabel(edge.Title, Color.white));
            if (!string.IsNullOrEmpty(edge.Tooltip))
            {
                _detailsView.Add(MakeLabel(edge.Tooltip, Color.gray));
            }

            if (edge.Transition != null)
            {
                _detailsView.Add(MakeLabel($"Target: {edge.Transition.TargetStateName}", edge.Color));
                _detailsView.Add(MakeLabel($"Resolved: {edge.Transition.TargetResolved}", Color.gray));
            }

            if (edge.ReverseTransition != null)
            {
                _detailsView.Add(MakeLabel($"Reverse target: {edge.ReverseTransition.TargetStateName}", EdgeColors.Reciprocal));
                _detailsView.Add(MakeLabel($"Reverse resolved: {edge.ReverseTransition.TargetResolved}", Color.gray));
            }

            if (edge.LayerEdge != null)
            {
                _detailsView.Add(MakeLabel($"Layer: {edge.LayerEdge.Name}", EdgeColors.StateLayer));
                _detailsView.Add(MakeLabel($"Order: {edge.LayerEdge.Order}", Color.gray));
                _detailsView.Add(MakeLabel($"Update: {edge.LayerEdge.Updates}", Color.gray));
                _detailsView.Add(MakeLabel($"FixedUpdate: {edge.LayerEdge.FixedUpdates}", Color.gray));
            }
            else if (edge.Layer != null)
            {
                _detailsView.Add(MakeLabel($"Layer: {edge.Layer.Name}", EdgeColors.StateLayer));
                _detailsView.Add(MakeLabel("Explicit State Layer", Color.gray));
                _detailsView.Add(MakeLabel($"Order: {edge.Layer.Order}", Color.gray));
            }

            if (edge.LayerStateEdge != null)
            {
                _detailsView.Add(MakeLabel($"Layer: {edge.LayerStateEdge.LayerName}", EdgeColors.LayerState));
                _detailsView.Add(MakeLabel($"State: {edge.LayerStateEdge.StateName}", Color.gray));
                _detailsView.Add(MakeLabel($"Default: {edge.LayerStateEdge.IsDefault}", Color.gray));
            }

            foreach (var dependency in edge.ContextDependencies)
            {
                string access = dependency.Access == CoCoStateContextAccess.Read ? "R" : "W";
                _detailsView.Add(MakeLabel($"[{access}] {FormatType(dependency.ContextType)}.{dependency.Path}", edge.Color));
            }

            foreach (var dependency in edge.OperationDependencies)
            {
                _detailsView.Add(MakeLabel($"{FormatType(dependency.ComponentType)}  {dependency.Usage}", EdgeColors.Operation));
            }
        }

        private void AddUnresolvedTransitions(CoCoStateGraphModel model)
        {
            bool hasUnresolved = false;
            foreach (var transition in model.Transitions)
            {
                if (transition.TargetResolved) continue;

                if (!hasUnresolved)
                {
                    AddHeader(_detailsView, "Unresolved Transitions");
                    hasUnresolved = true;
                }

                _detailsView.Add(MakeLabel(
                    $"{transition.TargetStateName} {transition.Note}",
                    new Color(1f, 0.75f, 0.25f)));
            }
        }

        private void AddWarnings(CoCoStateGraphModel model)
        {
            AddHeader(_detailsView, "Warnings");
            if (model.Warnings.Count == 0)
            {
                _detailsView.Add(MakeLabel("No warnings.", Color.gray));
                return;
            }

            foreach (var warning in model.Warnings)
            {
                _detailsView.Add(MakeLabel(warning, new Color(1f, 0.75f, 0.25f)));
            }
        }

        private void DrawEmptyDetails(string message)
        {
            _detailsView?.Clear();
            _detailsView?.Add(MakeLabel(message, Color.gray));
        }

        private static Label MakeLegendLabel(string label, int count, Color color)
        {
            return MakeLabel($"{label}: {count}", color);
        }

        private static void AddHeader(VisualElement parent, string text)
        {
            var label = MakeLabel(text, Color.white);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 8f;
            label.style.marginBottom = 4f;
            parent.Add(label);
        }

        private static Label MakeLabel(string text, Color color)
        {
            var label = new Label(text);
            label.style.color = color;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static string FormatObject(UnityEngine.Object value)
        {
            return value != null ? $"{value.GetType().Name} ({value.name})" : "None";
        }

        private static string FormatType(Type type)
        {
            return type != null ? type.Name : "None";
        }

        private static string FormatState(CoCoStateBase state)
        {
            return state != null ? state.GetType().Name : "None";
        }

        private CoCoStateNode FindStateNodeById(string stateId)
        {
            if (_model == null || string.IsNullOrEmpty(stateId)) return null;

            foreach (var state in _model.States)
            {
                if (state.Id == stateId) return state;
            }

            return null;
        }

        private static string FormatStateNodeName(CoCoStateNode state)
        {
            return state != null ? state.Name : "Unknown";
        }

        private static string FormatEdgeKind(CoCoStateGraphEdgeKind kind)
        {
            switch (kind)
            {
                case CoCoStateGraphEdgeKind.Transition:
                    return "Transition";
                case CoCoStateGraphEdgeKind.Layer:
                    return "State Layer Link";
                case CoCoStateGraphEdgeKind.LayerState:
                    return "Layer State Link";
                case CoCoStateGraphEdgeKind.ContextRead:
                    return "Context Read";
                case CoCoStateGraphEdgeKind.ContextWrite:
                    return "Context Write";
                case CoCoStateGraphEdgeKind.Operation:
                    return "Operation Dependency";
                default:
                    return "Edge";
            }
        }
    }

    public sealed class CoCoStateGraphView : GraphView
    {
        private readonly Dictionary<string, Port> _inputs = new Dictionary<string, Port>();
        private readonly Dictionary<string, Port> _outputs = new Dictionary<string, Port>();
        private readonly Dictionary<string, Node> _nodesById = new Dictionary<string, Node>();
        private readonly List<GraphElement> _renderedElements = new List<GraphElement>();
        private readonly List<Edge> _edges = new List<Edge>();

        private VisualElement _legend;
        private MiniMap _miniMap;
        private bool _allowInternalMutation;
        private bool _soloEnabled;
        private string _soloNodeId;

        public CoCoStateGraphView()
        {
            style.flexGrow = 1f;
            style.backgroundColor = new Color(0.08f, 0.08f, 0.08f);

            Insert(0, new GridBackground());
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            graphViewChanged = PreventMutation;
        }

        public event Action<CoCoStateGraphSelection> OnSelectionChanged;

        public CoCoStateGraphRenderStats LastRenderStats { get; private set; } =
            new CoCoStateGraphRenderStats();

        public void Draw(CoCoStateGraphModel model, CoCoStateGraphRenderSettings settings)
        {
            ClearRenderedGraph();

            var renderModel = CoCoStateGraphLayout.Build(model, settings);
            LastRenderStats = renderModel.Stats;

            foreach (var node in renderModel.Nodes)
            {
                AddRenderNode(node);
            }

            foreach (var edge in renderModel.Edges)
            {
                AddRenderEdge(edge);
            }

            AddDecorations();
            ApplySoloFilter();
        }

        public void FrameAllGraph()
        {
            FrameAll();
        }

        public void ResetViewState()
        {
            _soloEnabled = false;
            _soloNodeId = null;
            LastRenderStats = new CoCoStateGraphRenderStats();
            UpdateViewTransform(Vector3.zero, Vector3.one);
            ClearRenderedGraph();
        }

        public void SetSoloEnabled(bool enabled)
        {
            _soloEnabled = enabled;
            ApplySoloFilter();
        }

        public void SetSoloSelection(CoCoStateGraphSelection selection)
        {
            if (selection != null && !string.IsNullOrEmpty(selection.NodeId))
            {
                _soloNodeId = selection.NodeId;
            }
            else if (selection == null)
            {
                _soloNodeId = null;
            }

            ApplySoloFilter();
        }

        private GraphViewChange PreventMutation(GraphViewChange change)
        {
            if (_allowInternalMutation)
            {
                return change;
            }

            change.edgesToCreate?.Clear();
            change.elementsToRemove?.Clear();

            return change;
        }

        private void ClearRenderedGraph()
        {
            _allowInternalMutation = true;
            try
            {
                DeleteElements(new List<GraphElement>(_renderedElements));
            }
            finally
            {
                _allowInternalMutation = false;
            }

            _renderedElements.Clear();
            _inputs.Clear();
            _outputs.Clear();
            _nodesById.Clear();
            _edges.Clear();
            ClearDecorations();
        }

        private void AddRenderNode(CoCoStateGraphRenderNode renderNode)
        {
            var node = new Node
            {
                title = renderNode.Title,
                tooltip = renderNode.Caption,
                userData = CoCoStateGraphSelection.FromNode(renderNode)
            };
            node.capabilities &= ~Capabilities.Deletable;
            node.capabilities &= ~Capabilities.Resizable;
            node.style.backgroundColor = renderNode.Color;
            node.SetPosition(renderNode.Position);

            var input = node.InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
            input.portName = "in";
            input.portColor = ResolvePortColor(renderNode.Kind);
            input.pickingMode = PickingMode.Ignore;

            var output = node.InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
            output.portName = "out";
            output.portColor = ResolvePortColor(renderNode.Kind);
            output.pickingMode = PickingMode.Ignore;

            node.inputContainer.Add(input);
            node.outputContainer.Add(output);
            node.extensionContainer.Add(MakeNodeLabel(renderNode.Subtitle, Color.white));
            node.extensionContainer.Add(MakeNodeLabel(renderNode.Caption, Color.gray));
            node.RefreshExpandedState();
            node.RefreshPorts();
            node.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    OnSelectionChanged?.Invoke(node.userData as CoCoStateGraphSelection);
                }
            });

            AddElement(node);
            _renderedElements.Add(node);
            _inputs[renderNode.Id] = input;
            _outputs[renderNode.Id] = output;
            _nodesById[renderNode.Id] = node;
        }

        private void AddRenderEdge(CoCoStateGraphRenderEdge renderEdge)
        {
            if (!_outputs.TryGetValue(renderEdge.SourceNodeId, out var output) ||
                !_inputs.TryGetValue(renderEdge.TargetNodeId, out var input))
            {
                return;
            }

            var edge = output.ConnectTo(input);
            edge.tooltip = renderEdge.Tooltip;
            edge.userData = CoCoStateGraphSelection.FromEdge(renderEdge);
            edge.capabilities &= ~Capabilities.Deletable;
            edge.edgeControl.inputColor = renderEdge.Color;
            edge.edgeControl.outputColor = renderEdge.Color;
            edge.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    OnSelectionChanged?.Invoke(edge.userData as CoCoStateGraphSelection);
                }
            });

            AddElement(edge);
            if (renderEdge.Kind == CoCoStateGraphEdgeKind.LayerState)
            {
                edge.BringToFront();
            }
            _renderedElements.Add(edge);
            _edges.Add(edge);
        }

        private void ApplySoloFilter()
        {
            if (!_soloEnabled || string.IsNullOrEmpty(_soloNodeId))
            {
                ResetSoloFilter();
                return;
            }

            var connectedNodeIds = new HashSet<string>
            {
                _soloNodeId
            };

            foreach (var edge in _edges)
            {
                var selection = edge.userData as CoCoStateGraphSelection;
                var renderEdge = selection?.Edge;
                bool connected = renderEdge != null &&
                                 (renderEdge.SourceNodeId == _soloNodeId ||
                                  renderEdge.TargetNodeId == _soloNodeId);
                edge.style.display = connected ? DisplayStyle.Flex : DisplayStyle.None;
                if (!connected || renderEdge == null) continue;

                connectedNodeIds.Add(renderEdge.SourceNodeId);
                connectedNodeIds.Add(renderEdge.TargetNodeId);
            }

            foreach (var pair in _nodesById)
            {
                bool connected = connectedNodeIds.Contains(pair.Key);
                bool selected = pair.Key == _soloNodeId;
                ApplyNodeHighlight(pair.Value, connected, selected);
                if (connected)
                {
                    pair.Value.capabilities |= Capabilities.Movable;
                }
                else
                {
                    pair.Value.capabilities &= ~Capabilities.Movable;
                }
            }
        }

        private void ResetSoloFilter()
        {
            foreach (var edge in _edges)
            {
                edge.style.display = DisplayStyle.Flex;
            }

            foreach (var node in _nodesById.Values)
            {
                ApplyNodeHighlight(node, false, false);
                node.capabilities |= Capabilities.Movable;
            }
        }

        private static void ApplyNodeHighlight(Node node, bool highlighted, bool selected)
        {
            float width = selected ? 4f : highlighted ? 2f : 0f;
            var color = selected
                ? EdgeColors.Reciprocal
                : new Color(0.55f, 0.85f, 1f);

            node.style.borderTopWidth = width;
            node.style.borderRightWidth = width;
            node.style.borderBottomWidth = width;
            node.style.borderLeftWidth = width;
            node.style.borderTopColor = color;
            node.style.borderRightColor = color;
            node.style.borderBottomColor = color;
            node.style.borderLeftColor = color;
            node.style.opacity = 1f;
        }

        private void AddDecorations()
        {
            _miniMap = new MiniMap
            {
                anchored = true
            };
            _miniMap.SetPosition(new Rect(10f, 30f, 210f, 140f));
            Add(_miniMap);

            _legend = new VisualElement();
            _legend.style.position = Position.Absolute;
            _legend.style.left = 12f;
            _legend.style.bottom = 12f;
            _legend.style.paddingLeft = 8f;
            _legend.style.paddingRight = 8f;
            _legend.style.paddingTop = 6f;
            _legend.style.paddingBottom = 6f;
            _legend.style.backgroundColor = new Color(0.05f, 0.05f, 0.05f, 0.86f);
            _legend.Add(MakeLegendRow("Transition", EdgeColors.Transition));
            _legend.Add(MakeLegendRow("Reciprocal", EdgeColors.Reciprocal));
            _legend.Add(MakeLegendRow("State Layer", EdgeColors.StateLayer));
            _legend.Add(MakeLegendRow("Layer State", EdgeColors.LayerState));
            _legend.Add(MakeLegendRow("Context Read", EdgeColors.ContextRead));
            _legend.Add(MakeLegendRow("Context Write", EdgeColors.ContextWrite));
            _legend.Add(MakeLegendRow("Operation", EdgeColors.Operation));
            Add(_legend);
        }

        private void ClearDecorations()
        {
            if (_miniMap != null)
            {
                _miniMap.RemoveFromHierarchy();
                _miniMap = null;
            }

            if (_legend != null)
            {
                _legend.RemoveFromHierarchy();
                _legend = null;
            }
        }

        private static Label MakeNodeLabel(string text, Color color)
        {
            var label = new Label(text);
            label.style.color = color;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static VisualElement MakeLegendRow(string text, Color color)
        {
            var row = new Label(text);
            row.style.color = color;
            return row;
        }

        private static Color ResolvePortColor(CoCoStateGraphNodeKind kind)
        {
            switch (kind)
            {
                case CoCoStateGraphNodeKind.Context:
                    return EdgeColors.ContextRead;
                case CoCoStateGraphNodeKind.Operation:
                    return EdgeColors.Operation;
                case CoCoStateGraphNodeKind.Controller:
                    return EdgeColors.Controller;
                case CoCoStateGraphNodeKind.Layer:
                    return EdgeColors.StateLayer;
                default:
                    return EdgeColors.Transition;
            }
        }
    }

    public static class EdgeColors
    {
        public static readonly Color Transition = new Color(1f, 0.62f, 0.18f);
        public static readonly Color Reciprocal = new Color(1f, 0.9f, 0.25f);
        public static readonly Color StateLayer = new Color(0.62f, 0.45f, 1f);
        public static readonly Color LayerState = new Color(0.63f, 0.58f, 0.45f);
        public static readonly Color LayerDefaultState = new Color(1f, 0.78f, 0.28f);
        public static readonly Color Controller = StateLayer;
        public static readonly Color ContextRead = new Color(0.58f, 0.58f, 0.58f);
        public static readonly Color ContextWrite = new Color(0.3f, 0.86f, 0.42f);
        public static readonly Color Operation = new Color(0.3f, 0.82f, 0.92f);
    }

    public enum CoCoStateGraphSelectionKind
    {
        None,
        Controller,
        Layer,
        State,
        Context,
        Operation,
        Edge
    }

    public sealed class CoCoStateGraphSelection
    {
        public CoCoStateGraphSelectionKind Kind;
        public string NodeId;
        public CoCoStateController Controller;
        public CoCoStateLayerNode Layer;
        public CoCoStateNode State;
        public Type OperationType;
        public CoCoStateGraphRenderEdge Edge;

        public static CoCoStateGraphSelection FromNode(CoCoStateGraphRenderNode node)
        {
            switch (node.Kind)
            {
                case CoCoStateGraphNodeKind.Controller:
                    return new CoCoStateGraphSelection
                    {
                        Kind = CoCoStateGraphSelectionKind.Controller,
                        NodeId = node.Id,
                        Controller = node.Controller
                    };
                case CoCoStateGraphNodeKind.Layer:
                    return new CoCoStateGraphSelection
                    {
                        Kind = CoCoStateGraphSelectionKind.Layer,
                        NodeId = node.Id,
                        Layer = node.Layer
                    };
                case CoCoStateGraphNodeKind.State:
                    return new CoCoStateGraphSelection
                    {
                        Kind = CoCoStateGraphSelectionKind.State,
                        NodeId = node.Id,
                        State = node.State
                    };
                case CoCoStateGraphNodeKind.Context:
                    return new CoCoStateGraphSelection
                    {
                        Kind = CoCoStateGraphSelectionKind.Context,
                        NodeId = node.Id,
                        Layer = node.Layer
                    };
                case CoCoStateGraphNodeKind.Operation:
                    return new CoCoStateGraphSelection
                    {
                        Kind = CoCoStateGraphSelectionKind.Operation,
                        NodeId = node.Id,
                        OperationType = node.OperationType
                    };
                default:
                    return new CoCoStateGraphSelection
                    {
                        Kind = CoCoStateGraphSelectionKind.None
                    };
            }
        }

        public static CoCoStateGraphSelection FromEdge(CoCoStateGraphRenderEdge edge)
        {
            return new CoCoStateGraphSelection
            {
                Kind = CoCoStateGraphSelectionKind.Edge,
                Edge = edge
            };
        }
    }
}
