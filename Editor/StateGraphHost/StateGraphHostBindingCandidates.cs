using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.StateGraph;
using UnityEngine;

namespace CoCoFlow.Editor.StateGraphHost
{
    using StateGraphHost = global::CoCoFlow.Runtime.StateGraph.StateGraphHost;

    internal static class StateGraphHostBindingCandidates
    {
        #region Public API

        internal static void FindIntentSources(
            StateGraphHost host,
            IReadOnlyList<MonoBehaviour> assigned,
            List<MonoBehaviour> results)
        {
            Find(host, assigned, results, StateGraphHostBindingRules.IsIntentFrameSource);
        }

        internal static void FindOperators(
            StateGraphHost host,
            IReadOnlyList<MonoBehaviour> assigned,
            List<MonoBehaviour> results)
        {
            Find(host, assigned, results, StateGraphHostBindingRules.IsOperator);
        }

        internal static void FindActorContexts(
            StateGraphHost host,
            MonoBehaviour assigned,
            List<MonoBehaviour> results)
        {
            Find(host, null, results,
                component => component != assigned &&
                    StateGraphHostBindingRules.IsActorContextBinding(component));
        }

        #endregion

        #region Internal Logic

        private static void Find(
            StateGraphHost host,
            IReadOnlyList<MonoBehaviour> assigned,
            List<MonoBehaviour> results,
            Func<MonoBehaviour, bool> matches)
        {
            results.Clear();
            if (host == null)
            {
                return;
            }

            var boundary = HostComponentBoundary.For(host);
            MonoBehaviour[] components = host.GetComponentsInChildren<MonoBehaviour>(true);
            for (int index = 0; index < components.Length; index++)
            {
                MonoBehaviour component = components[index];
                if (component == null ||
                    !boundary.Contains(component) ||
                    Contains(assigned, component) ||
                    !matches(component))
                {
                    continue;
                }

                results.Add(component);
            }
        }

        private static bool Contains(
            IReadOnlyList<MonoBehaviour> assigned,
            MonoBehaviour candidate)
        {
            if (assigned == null)
            {
                return false;
            }

            for (int index = 0; index < assigned.Count; index++)
            {
                if (ReferenceEquals(assigned[index], candidate))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}
