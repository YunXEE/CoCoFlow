using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    public enum CoCoStateContextAccess
    {
        Read,
        Write
    }

    [Serializable]
    public readonly struct CoCoStateContextDependency
    {
        public CoCoStateContextDependency(
            Type contextType,
            string path,
            CoCoStateContextAccess access,
            string note)
        {
            ContextType = contextType;
            Path = path ?? string.Empty;
            Access = access;
            Note = note ?? string.Empty;
        }

        public Type ContextType { get; }
        public string Path { get; }
        public CoCoStateContextAccess Access { get; }
        public string Note { get; }
    }

    [Serializable]
    public readonly struct CoCoStateOperationDependency
    {
        public CoCoStateOperationDependency(Type componentType, string usage)
        {
            ComponentType = componentType;
            Usage = usage ?? string.Empty;
        }

        public Type ComponentType { get; }
        public string Usage { get; }
    }

    [Serializable]
    public readonly struct CoCoStateTransitionTarget
    {
        public CoCoStateTransitionTarget(Type stateType, string note)
        {
            StateType = stateType;
            Note = note ?? string.Empty;
        }

        public Type StateType { get; }
        public string Note { get; }
    }

    public sealed class CoCoStateDefinition
    {
        private readonly List<CoCoStateContextDependency> _contextDependencies =
            new List<CoCoStateContextDependency>();
        private readonly List<CoCoStateOperationDependency> _operationDependencies =
            new List<CoCoStateOperationDependency>();
        private readonly List<CoCoStateTransitionTarget> _transitionTargets =
            new List<CoCoStateTransitionTarget>();

        internal CoCoStateDefinition(Type stateType, string displayName)
        {
            StateType = stateType;
            DisplayName = string.IsNullOrEmpty(displayName) ? stateType?.Name ?? string.Empty : displayName;
        }

        public Type StateType { get; }
        public string DisplayName { get; }
        public bool HasDeclarations =>
            _contextDependencies.Count > 0 ||
            _operationDependencies.Count > 0 ||
            _transitionTargets.Count > 0;

        public IReadOnlyList<CoCoStateContextDependency> ContextDependencies => _contextDependencies;
        public IReadOnlyList<CoCoStateOperationDependency> OperationDependencies => _operationDependencies;
        public IReadOnlyList<CoCoStateTransitionTarget> TransitionTargets => _transitionTargets;

        internal void AddContextDependency(CoCoStateContextDependency dependency)
        {
            _contextDependencies.Add(dependency);
        }

        internal void AddOperationDependency(CoCoStateOperationDependency dependency)
        {
            _operationDependencies.Add(dependency);
        }

        internal void AddTransitionTarget(CoCoStateTransitionTarget target)
        {
            _transitionTargets.Add(target);
        }
    }

    public sealed class CoCoStateDefinitionBuilder
    {
        private readonly CoCoStateDefinition _definition;

        internal CoCoStateDefinitionBuilder(Type stateType, string displayName)
        {
            _definition = new CoCoStateDefinition(stateType, displayName);
        }

        public CoCoStateDefinitionBuilder ReadsContext<TContext>(string path, string note = null)
            where TContext : class, ICoCoContext
        {
            ValidateContextPath(path);
            _definition.AddContextDependency(new CoCoStateContextDependency(
                typeof(TContext),
                path,
                CoCoStateContextAccess.Read,
                note));
            return this;
        }

        public CoCoStateDefinitionBuilder WritesContext<TContext>(string path, string note = null)
            where TContext : class, ICoCoContext
        {
            ValidateContextPath(path);
            _definition.AddContextDependency(new CoCoStateContextDependency(
                typeof(TContext),
                path,
                CoCoStateContextAccess.Write,
                note));
            return this;
        }

        public CoCoStateDefinitionBuilder UsesOperation<TComponent>(string usage = null)
            where TComponent : Component
        {
            _definition.AddOperationDependency(new CoCoStateOperationDependency(
                typeof(TComponent),
                usage));
            return this;
        }

        public CoCoStateDefinitionBuilder CanTransitionTo<TState>(string note = null)
            where TState : CoCoStateBase
        {
            return CanTransitionTo(typeof(TState), note);
        }

        public CoCoStateDefinitionBuilder CanTransitionTo(Type stateType, string note = null)
        {
            if (stateType == null)
            {
                throw new ArgumentNullException(nameof(stateType));
            }

            if (!typeof(CoCoStateBase).IsAssignableFrom(stateType))
            {
                throw new ArgumentException(
                    $"{stateType.FullName} must inherit {nameof(CoCoStateBase)}.",
                    nameof(stateType));
            }

            _definition.AddTransitionTarget(new CoCoStateTransitionTarget(
                stateType,
                note));
            return this;
        }

        private static void ValidateContextPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("Context dependency path must be declared.", nameof(path));
            }
        }

        internal CoCoStateDefinition Build()
        {
            return _definition;
        }
    }
}
