using System;
using CoCoFlow.Runtime.Core;

// 编译与资产校验保留诊断的层、节点、出边及字段位置，图编辑器据此定位错误。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>区分诊断所指的图、层、节点、转换及其他声明。</summary>
    public enum GraphElementKind
    {
        None = 0,
        Graph = 1,
        Layer = 2,
        State = 3,
        Transition = 4,
        Manifest = 6,
        EventAdapterDeclaration = 7
    }

    /// <summary>标识诊断涉及的具体字段，供图编辑器定位。</summary>
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
        Manifest = 15,
        AssetGuidStamp = 16,
        EventAdapterDeclarations = 17
    }

    /// <summary>记录图诊断的稳定元素标识与源列表索引。</summary>
    public readonly struct GraphDiagnosticLocation : IEquatable<GraphDiagnosticLocation>
    {
        /// <summary>记录错误涉及的图元素及来源索引，供编辑器定位并跳转。</summary>
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
        public int EventAdapterDeclarationIndex { get; }

        /// <summary>按<c>ElementKind</c>、<c>Field</c>、<c>GraphId</c>、<c>LayerId</c>、<c>StateId</c>比较两个值，全部相等时返回 true。</summary>
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
                   EventAdapterDeclarationIndex == other.EventAdapterDeclarationIndex;
        }

        /// <summary>仅当对象属于同类型并且<c>ElementKind</c>、<c>Field</c>、<c>GraphId</c>、<c>LayerId</c>、<c>StateId</c>相等时返回 true。</summary>
        public override bool Equals(object obj) =>
            obj is GraphDiagnosticLocation other && Equals(other);

        /// <summary>为哈希集合计算与<c>ElementKind</c>、<c>Field</c>、<c>GraphId</c>、<c>LayerId</c>、<c>StateId</c>的相等判定一致的散列值。</summary>
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
                hashCode = (hashCode * 397) ^ EventAdapterDeclarationIndex;
                return hashCode;
            }
        }

        /// <summary>比较两端的<c>ElementKind</c>、<c>Field</c>、<c>GraphId</c>、<c>LayerId</c>、<c>StateId</c>，相等时返回 true。</summary>
        public static bool operator ==(
            GraphDiagnosticLocation left,
            GraphDiagnosticLocation right) => left.Equals(right);

        /// <summary>比较两端的<c>ElementKind</c>、<c>Field</c>、<c>GraphId</c>、<c>LayerId</c>、<c>StateId</c>，存在差异时返回 true。</summary>
        public static bool operator !=(
            GraphDiagnosticLocation left,
            GraphDiagnosticLocation right) => !left.Equals(right);
    }

    /// <summary>将 Core 诊断关联到图中的位置，供编译结果和编辑器导航使用。</summary>
    public readonly struct GraphDiagnostic : IEquatable<GraphDiagnostic>
    {
        /// <summary>将诊断原因与图元素位置一并保存，供编译结果和编辑器展示。</summary>
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

        /// <summary>按<c>Diagnostic</c>、<c>Location</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(GraphDiagnostic other) =>
            Diagnostic == other.Diagnostic && Location == other.Location;

        /// <summary>仅当对象属于同类型并且<c>Diagnostic</c>、<c>Location</c>相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is GraphDiagnostic other && Equals(other);
        /// <summary>为哈希集合计算与<c>Diagnostic</c>、<c>Location</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode() => unchecked((Diagnostic.GetHashCode() * 397) ^ Location.GetHashCode());

        /// <summary>比较两端的<c>Diagnostic</c>、<c>Location</c>，相等时返回 true。</summary>
        public static bool operator ==(GraphDiagnostic left, GraphDiagnostic right) =>
            left.Equals(right);

        /// <summary>比较两端的<c>Diagnostic</c>、<c>Location</c>，存在差异时返回 true。</summary>
        public static bool operator !=(GraphDiagnostic left, GraphDiagnostic right) =>
            !left.Equals(right);
    }
}
