using System;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.StateGraph
{
    public enum GraphElementKind
    {
        None = 0,
        Graph = 1,
        Layer = 2,
        State = 3,
        Transition = 4,
        Condition = 5,
        Manifest = 6,
        EventAdapterDeclaration = 7
    }

    public enum GraphField
    {
        None = 0,
        SchemaVersion = 1,
        ContentFingerprint = 2,
        Identifier = 3,
        ParentState = 4,
        InitialState = 5,
        InitialChildState = 6,
        Descriptor = 7,
        Config = 8,
        SourceState = 9,
        TargetState = 10,
        Priority = 11,
        Window = 12,
        Conditions = 14,
        Manifest = 15,
        AssetGuidStamp = 16,
        EventAdapterDeclarations = 17
    }

    public readonly struct GraphDiagnosticLocation : IEquatable<GraphDiagnosticLocation>
    {
        public GraphDiagnosticLocation(
            GraphElementKind elementKind,
            GraphField field,
            GraphId graphId,
            LayerId layerId,
            StateId stateId,
            TransitionId transitionId,
            int layerIndex,
            int stateIndex,
            int transitionIndex,
            int conditionIndex,
            int eventAdapterDeclarationIndex = -1)
        {
            ElementKind = elementKind;
            Field = field;
            GraphId = graphId;
            LayerId = layerId;
            StateId = stateId;
            TransitionId = transitionId;
            LayerIndex = layerIndex;
            StateIndex = stateIndex;
            TransitionIndex = transitionIndex;
            ConditionIndex = conditionIndex;
            EventAdapterDeclarationIndex = eventAdapterDeclarationIndex;
        }

        public GraphElementKind ElementKind { get; }
        public GraphField Field { get; }
        public GraphId GraphId { get; }
        public LayerId LayerId { get; }
        public StateId StateId { get; }
        public TransitionId TransitionId { get; }
        public int LayerIndex { get; }
        public int StateIndex { get; }
        public int TransitionIndex { get; }
        public int ConditionIndex { get; }
        public int EventAdapterDeclarationIndex { get; }

        public bool Equals(GraphDiagnosticLocation other)
        {
            return ElementKind == other.ElementKind &&
                   Field == other.Field &&
                   GraphId == other.GraphId &&
                   LayerId == other.LayerId &&
                   StateId == other.StateId &&
                   TransitionId == other.TransitionId &&
                   LayerIndex == other.LayerIndex &&
                   StateIndex == other.StateIndex &&
                   TransitionIndex == other.TransitionIndex &&
                   ConditionIndex == other.ConditionIndex &&
                   EventAdapterDeclarationIndex == other.EventAdapterDeclarationIndex;
        }

        public override bool Equals(object obj) =>
            obj is GraphDiagnosticLocation other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (int)ElementKind;
                hashCode = (hashCode * 397) ^ (int)Field;
                hashCode = (hashCode * 397) ^ GraphId.GetHashCode();
                hashCode = (hashCode * 397) ^ LayerId.GetHashCode();
                hashCode = (hashCode * 397) ^ StateId.GetHashCode();
                hashCode = (hashCode * 397) ^ TransitionId.GetHashCode();
                hashCode = (hashCode * 397) ^ LayerIndex;
                hashCode = (hashCode * 397) ^ StateIndex;
                hashCode = (hashCode * 397) ^ TransitionIndex;
                hashCode = (hashCode * 397) ^ ConditionIndex;
                hashCode = (hashCode * 397) ^ EventAdapterDeclarationIndex;
                return hashCode;
            }
        }

        public static bool operator ==(
            GraphDiagnosticLocation left,
            GraphDiagnosticLocation right) => left.Equals(right);

        public static bool operator !=(
            GraphDiagnosticLocation left,
            GraphDiagnosticLocation right) => !left.Equals(right);
    }

    public readonly struct GraphDiagnostic : IEquatable<GraphDiagnostic>
    {
        public GraphDiagnostic(
            CoCoDiagnostic diagnostic,
            GraphDiagnosticLocation location)
        {
            if (diagnostic.IsNone)
            {
                throw new ArgumentException("A graph diagnostic cannot wrap None.", nameof(diagnostic));
            }

            Diagnostic = diagnostic;
            Location = location;
        }

        public CoCoDiagnostic Diagnostic { get; }
        public GraphDiagnosticLocation Location { get; }
        public bool IsError => Diagnostic.IsError;

        public bool Equals(GraphDiagnostic other) =>
            Diagnostic == other.Diagnostic && Location == other.Location;

        public override bool Equals(object obj) => obj is GraphDiagnostic other && Equals(other);
        public override int GetHashCode() => unchecked((Diagnostic.GetHashCode() * 397) ^ Location.GetHashCode());

        public static bool operator ==(GraphDiagnostic left, GraphDiagnostic right) =>
            left.Equals(right);

        public static bool operator !=(GraphDiagnostic left, GraphDiagnostic right) =>
            !left.Equals(right);
    }
}
