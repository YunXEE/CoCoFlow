using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.StateGraph;
using UnityEngine;

namespace CoCoFlow.Editor.StateGraphHost
{
    using StateGraphHost = global::CoCoFlow.Runtime.StateGraph.StateGraphHost;

    internal static class StateGraphHostBindingCandidates
    {
        internal static void FindIntentSources(
            StateGraphHost host,
            IReadOnlyList<MonoBehaviour> assigned,
            List<MonoBehaviour> results)
        {
            Find(host, assigned, results, IsIntentSource);
        }

        internal static void FindOperators(
            StateGraphHost host,
            IReadOnlyList<MonoBehaviour> assigned,
            List<MonoBehaviour> results)
        {
            Find(
                host,
                assigned,
                results,
                component => component is IOperator);
        }

        internal static bool IsIntentSource(MonoBehaviour component)
        {
            if (component == null)
            {
                return false;
            }

            Type[] interfaces = component.GetType().GetInterfaces();
            for (int index = 0; index < interfaces.Length; index++)
            {
                Type contract = interfaces[index];
                if (contract.IsGenericType &&
                    contract.GetGenericTypeDefinition() == typeof(IIntentFrameSource<>))
                {
                    return true;
                }
            }

            return false;
        }

        private static void Find(
            StateGraphHost host,
            IReadOnlyList<MonoBehaviour> assigned,
            List<MonoBehaviour> results,
            Func<MonoBehaviour, bool> matches)
        {
            results.Clear();
            if (host == null || matches == null)
            {
                return;
            }

            MonoBehaviour[] components = host.GetComponentsInChildren<MonoBehaviour>(true);
            for (int index = 0; index < components.Length; index++)
            {
                MonoBehaviour component = components[index];
                if (component == null ||
                    !HostComponentBoundary.For(host).Contains(component) ||
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
    }
}
