using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Editor.StateGraph
{
    /// <summary>Session-only visibility and edge grouping; never rewrites authored topology.</summary>
    internal sealed class CoCoStateGraphCanvasProjection
    {
        internal sealed class TransitionGroup
        {
            internal CoCoSerializedId128 SourceId { get; }
            internal CoCoSerializedId128 TargetId { get; }
            internal IReadOnlyList<CoCoSerializedId128> TransitionIds { get; }

            internal TransitionGroup(CoCoSerializedId128 source, CoCoSerializedId128 target,
                List<CoCoStateGraphTransitionRecord> transitions)
            {
                SourceId = source;
                TargetId = target;
                transitions.Sort((left, right) =>
                {
                    int priority = left.Priority.CompareTo(right.Priority);
                    if (priority != 0) return priority;
                    int high = left.TransitionId.High.CompareTo(right.TransitionId.High);
                    return high != 0 ? high : left.TransitionId.Low.CompareTo(right.TransitionId.Low);
                });
                TransitionIds = transitions.ConvertAll(edge => edge.TransitionId).AsReadOnly();
            }
        }

        private readonly Dictionary<CoCoSerializedId128, CoCoSerializedId128> _representatives =
            new Dictionary<CoCoSerializedId128, CoCoSerializedId128>();
        private readonly Dictionary<CoCoSerializedId128, string> _paths =
            new Dictionary<CoCoSerializedId128, string>();

        internal IReadOnlyList<CoCoSerializedId128> VisibleStateIds { get; private set; }
        internal IReadOnlyList<CoCoSerializedId128> OrdinaryTransitionIds { get; private set; }
        internal IReadOnlyList<TransitionGroup> TransitionGroups { get; private set; }

        internal CoCoSerializedId128 VisibleId(CoCoSerializedId128 stateId) =>
            _representatives.TryGetValue(stateId, out CoCoSerializedId128 visible) ? visible : default;

        internal bool IsVisible(CoCoSerializedId128 stateId) =>
            stateId.IsValid && VisibleId(stateId) == stateId;

        internal string StatePath(CoCoSerializedId128 stateId) =>
            _paths.TryGetValue(stateId, out string path) ? path : "Missing State";

        internal static CoCoStateGraphCanvasProjection Build(CoCoStateGraphLayerRecord layer,
            IReadOnlyCollection<CoCoStateId> collapsedStateIds)
        {
            var result = new CoCoStateGraphCanvasProjection();
            var visibleIds = new List<CoCoSerializedId128>();
            var ordinaryIds = new List<CoCoSerializedId128>();
            var groups = new List<TransitionGroup>();
            var states = new Dictionary<CoCoSerializedId128, CoCoStateGraphStateRecord>();
            var collapsed = new HashSet<CoCoSerializedId128>();
            foreach (CoCoStateId id in collapsedStateIds)
                collapsed.Add(new CoCoSerializedId128(id.High, id.Low));
            if (layer != null)
            {
                foreach (CoCoStateGraphStateRecord state in layer.AllStates)
                    if (state != null) states[state.StateId] = state;
                foreach (CoCoStateGraphStateRecord state in layer.AllStates)
                {
                    if (state == null) continue;
                    CoCoSerializedId128 representative = state.StateId;
                    var names = new List<string> { state.DisplayName };
                    var visited = new HashSet<CoCoSerializedId128> { state.StateId };
                    CoCoSerializedId128 parent = ReferenceEquals(state, layer.AnyState)
                        ? default : state.ParentStateId;
                    while (parent.IsValid && visited.Add(parent) &&
                           states.TryGetValue(parent, out CoCoStateGraphStateRecord ancestor))
                    {
                        if (collapsed.Contains(parent)) representative = parent;
                        names.Insert(0, ancestor.DisplayName);
                        parent = ancestor.ParentStateId;
                    }

                    result._representatives[state.StateId] = representative;
                    result._paths[state.StateId] = string.Join(" › ", names);
                    if (representative == state.StateId) visibleIds.Add(state.StateId);
                }

                var grouped = new Dictionary<(CoCoSerializedId128, CoCoSerializedId128),
                    List<CoCoStateGraphTransitionRecord>>();
                foreach (CoCoStateGraphTransitionRecord edge in layer.Transitions)
                {
                    if (edge == null) continue;
                    CoCoSerializedId128 source = result.VisibleId(edge.SourceStateId);
                    CoCoSerializedId128 target = result.VisibleId(edge.TargetStateId);
                    if (!source.IsValid || !target.IsValid) continue;
                    bool projected = source != edge.SourceStateId || target != edge.TargetStateId;
                    if (!projected)
                    {
                        ordinaryIds.Add(edge.TransitionId);
                        continue;
                    }

                    // Hidden internal edges must not become a fake composite self-loop.
                    if (source == target) continue;
                    var key = (source, target);
                    if (!grouped.TryGetValue(key, out List<CoCoStateGraphTransitionRecord> members))
                    {
                        members = new List<CoCoStateGraphTransitionRecord>();
                        grouped.Add(key, members);
                    }
                    members.Add(edge);
                }

                foreach (var pair in grouped)
                    groups.Add(new TransitionGroup(pair.Key.Item1, pair.Key.Item2, pair.Value));
            }

            result.VisibleStateIds = visibleIds.AsReadOnly();
            result.OrdinaryTransitionIds = ordinaryIds.AsReadOnly();
            result.TransitionGroups = groups.AsReadOnly();
            return result;
        }
    }
}
