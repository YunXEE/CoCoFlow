using System.Collections.Generic;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.StateGraphHost;

namespace CoCoFlow.Editor.StateGraph
{
    /// <summary>Session-only visibility and edge grouping; never rewrites authored topology.</summary>
    internal sealed class StateGraphCanvasProjection
    {
        internal sealed class TransitionGroup
        {
            internal SerializedId128 SourceId { get; }
            internal SerializedId128 TargetId { get; }
            internal IReadOnlyList<SerializedId128> TransitionIds { get; }

            internal TransitionGroup(SerializedId128 source, SerializedId128 target,
                List<StateGraphTransitionRecord> transitions)
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

        private readonly Dictionary<SerializedId128, SerializedId128> _representatives =
            new Dictionary<SerializedId128, SerializedId128>();
        private readonly Dictionary<SerializedId128, string> _paths =
            new Dictionary<SerializedId128, string>();

        internal IReadOnlyList<SerializedId128> VisibleStateIds { get; private set; }
        internal IReadOnlyList<SerializedId128> OrdinaryTransitionIds { get; private set; }
        internal IReadOnlyList<TransitionGroup> TransitionGroups { get; private set; }

        internal SerializedId128 VisibleId(SerializedId128 stateId) =>
            _representatives.TryGetValue(stateId, out SerializedId128 visible) ? visible : default;

        internal bool IsVisible(SerializedId128 stateId) =>
            stateId.IsValid && VisibleId(stateId) == stateId;

        internal string StatePath(SerializedId128 stateId) =>
            _paths.TryGetValue(stateId, out string path) ? path : "Missing State";

        internal static StateGraphCanvasProjection Build(StateGraphLayerRecord layer,
            IReadOnlyCollection<StateId> collapsedStateIds)
        {
            var result = new StateGraphCanvasProjection();
            var visibleIds = new List<SerializedId128>();
            var ordinaryIds = new List<SerializedId128>();
            var groups = new List<TransitionGroup>();
            var states = new Dictionary<SerializedId128, StateGraphStateRecord>();
            var collapsed = new HashSet<SerializedId128>();
            foreach (StateId id in collapsedStateIds)
                collapsed.Add(new SerializedId128(id.High, id.Low));
            if (layer != null)
            {
                foreach (StateGraphStateRecord state in layer.AllStates)
                    if (state != null) states[state.StateId] = state;
                foreach (StateGraphStateRecord state in layer.AllStates)
                {
                    if (state == null) continue;
                    SerializedId128 representative = state.StateId;
                    var names = new List<string> { state.DisplayName };
                    var visited = new HashSet<SerializedId128> { state.StateId };
                    SerializedId128 parent = ReferenceEquals(state, layer.AnyState)
                        ? default : state.ParentStateId;
                    while (parent.IsValid && visited.Add(parent) &&
                           states.TryGetValue(parent, out StateGraphStateRecord ancestor))
                    {
                        if (collapsed.Contains(parent)) representative = parent;
                        names.Insert(0, ancestor.DisplayName);
                        parent = ancestor.ParentStateId;
                    }

                    result._representatives[state.StateId] = representative;
                    result._paths[state.StateId] = string.Join(" › ", names);
                    if (representative == state.StateId) visibleIds.Add(state.StateId);
                }

                var grouped = new Dictionary<(SerializedId128, SerializedId128),
                    List<StateGraphTransitionRecord>>();
                foreach (StateGraphTransitionRecord edge in layer.Transitions)
                {
                    if (edge == null) continue;
                    SerializedId128 source = result.VisibleId(edge.SourceStateId);
                    SerializedId128 target = result.VisibleId(edge.TargetStateId);
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
                    if (!grouped.TryGetValue(key, out List<StateGraphTransitionRecord> members))
                    {
                        members = new List<StateGraphTransitionRecord>();
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
