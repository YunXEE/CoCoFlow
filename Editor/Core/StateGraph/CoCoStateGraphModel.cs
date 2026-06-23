using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Editor.Core.StateGraph
{
    public sealed class CoCoStateGraphModel
    {
        public CoCoStateController RootController;
        public readonly List<CoCoStateLayerNode> Layers = new List<CoCoStateLayerNode>();
        public readonly List<CoCoStateNode> States = new List<CoCoStateNode>();
        public readonly List<CoCoStateTransitionEdge> Transitions = new List<CoCoStateTransitionEdge>();
        public readonly List<CoCoStateLayerEdge> LayerEdges = new List<CoCoStateLayerEdge>();
        public readonly List<CoCoStateLayerStateEdge> LayerStateEdges = new List<CoCoStateLayerStateEdge>();
        public readonly List<string> Warnings = new List<string>();
    }

    public sealed class CoCoStateLayerNode
    {
        public string Id;
        public string ParentId;
        public string Name;
        public int Depth;
        public int Order;
        public bool Updates;
        public bool FixedUpdates;
        public CoCoStateController Controller;
        public CoCoStateLayer Layer;
        public MonoBehaviour ContextProvider;
        public Type ContextType;
        public CoCoStateBase DefaultState;
        public CoCoStateBase CurrentState;
        public readonly List<CoCoContextFieldNode> ContextFields = new List<CoCoContextFieldNode>();
        public readonly List<CoCoContextSourceNode> ContextSources = new List<CoCoContextSourceNode>();
    }

    public sealed class CoCoStateNode
    {
        public string Id;
        public string LayerId;
        public string Name;
        public int Depth;
        public CoCoStateBase State;
        public CoCoStateDefinition Definition;
        public bool IsDefault;
        public bool IsCurrent;
        public bool HasDefinition;
    }

    public sealed class CoCoStateTransitionEdge
    {
        public string SourceStateId;
        public string TargetStateId;
        public string TargetStateName;
        public Type TargetStateType;
        public string Note;
        public bool TargetResolved;
    }

    public sealed class CoCoStateLayerEdge
    {
        public string ParentLayerId;
        public string ChildLayerId;
        public string Name;
        public int Order;
        public bool Updates;
        public bool FixedUpdates;
    }

    public sealed class CoCoStateLayerStateEdge
    {
        public string LayerId;
        public string StateId;
        public string LayerName;
        public string StateName;
        public bool IsDefault;
    }

    public sealed class CoCoContextFieldNode
    {
        public string Path;
        public string TypeName;
        public string DeclaringTypeName;
        public bool IsExtension;
    }

    public sealed class CoCoContextSourceNode
    {
        public string Name;
        public string TypeName;
        public int? Priority;
    }
}
