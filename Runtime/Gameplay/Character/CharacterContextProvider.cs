using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Gameplay.Character
{
    public interface ICharacterContextSource
    {
        int Priority { get; }
        void WriteToContext(CharacterContext context);
    }

    public interface ICharacterContextSourceUpdateMode
    {
        bool IsProviderDriven { get; }
        void SetProviderDriven(bool providerDriven);
    }

    public abstract class CharacterContextProvider<TContext> :
        MonoBehaviour,
        ICoCoContextProvider<TContext>,
        ICoCoContextFrameResolver
        where TContext : CharacterContext
    {
        [Header("Context Sources")]
        [Tooltip("按 Priority 从低到高写入同一份 CharacterContext。空槽位会被跳过。")]
        [SerializeField] private List<MonoBehaviour> contextSources = new List<MonoBehaviour>();
        [SerializeField] private bool resolveSourcesOncePerFrame = true;

        private readonly List<ResolvedContextSource> _resolvedSources =
            new List<ResolvedContextSource>();
        private readonly HashSet<ICharacterContextSourceUpdateMode> _providerDrivenSources =
            new HashSet<ICharacterContextSourceUpdateMode>();
        private int _lastResolvedFrame = -1;

        public abstract TContext Context { get; }

        public void SetContextSources(IEnumerable<MonoBehaviour> sources)
        {
            ReleaseProviderDrivenSources();
            InvalidateResolvedFrame();
            contextSources.Clear();
            if (sources != null)
            {
                foreach (var source in sources)
                {
                    contextSources.Add(source);
                }
            }

            ResolveSources();
        }

        public void ResolveContextFrame(ICoCoContext context)
        {
            if (resolveSourcesOncePerFrame && _lastResolvedFrame == Time.frameCount)
            {
                return;
            }

            if (context is CharacterContext characterContext)
            {
                WriteSourcesToContext(characterContext);
                _lastResolvedFrame = Time.frameCount;
            }
        }

        protected virtual void WriteSourcesToContext(CharacterContext context)
        {
            ResolveSources();
            foreach (var source in _resolvedSources)
            {
                source.Source.WriteToContext(context);
            }
        }

        protected virtual void Awake()
        {
            ResolveSources();
        }

        protected virtual void OnDisable()
        {
            ReleaseProviderDrivenSources();
        }

        private void ResolveSources()
        {
            _resolvedSources.Clear();
            var activeProviderDrivenSources = new HashSet<ICharacterContextSourceUpdateMode>();
            for (int i = 0; i < contextSources.Count; i++)
            {
                var behaviour = contextSources[i];
                if (behaviour == null || !behaviour.isActiveAndEnabled) continue;

                if (behaviour is ICharacterContextSource source)
                {
                    _resolvedSources.Add(new ResolvedContextSource(source, i));
                    if (source is ICharacterContextSourceUpdateMode updateMode)
                    {
                        activeProviderDrivenSources.Add(updateMode);
                    }
                }
            }

            ApplyProviderDrivenSources(activeProviderDrivenSources);
            _resolvedSources.Sort(CompareResolvedSources);
        }

        private void ApplyProviderDrivenSources(
            HashSet<ICharacterContextSourceUpdateMode> activeProviderDrivenSources)
        {
            foreach (var source in _providerDrivenSources)
            {
                if (!activeProviderDrivenSources.Contains(source))
                {
                    source.SetProviderDriven(false);
                }
            }

            _providerDrivenSources.Clear();
            foreach (var source in activeProviderDrivenSources)
            {
                source.SetProviderDriven(true);
                _providerDrivenSources.Add(source);
            }
        }

        private void ReleaseProviderDrivenSources()
        {
            foreach (var source in _providerDrivenSources)
            {
                source.SetProviderDriven(false);
            }

            _providerDrivenSources.Clear();
            _resolvedSources.Clear();
            InvalidateResolvedFrame();
        }

        private static int CompareResolvedSources(
            ResolvedContextSource left,
            ResolvedContextSource right)
        {
            int priorityComparison = left.Source.Priority.CompareTo(right.Source.Priority);
            return priorityComparison != 0
                ? priorityComparison
                : left.DeclarationIndex.CompareTo(right.DeclarationIndex);
        }

        private void InvalidateResolvedFrame()
        {
            _lastResolvedFrame = -1;
        }

        private readonly struct ResolvedContextSource
        {
            public ResolvedContextSource(
                ICharacterContextSource source,
                int declarationIndex)
            {
                Source = source;
                DeclarationIndex = declarationIndex;
            }

            public ICharacterContextSource Source { get; }
            public int DeclarationIndex { get; }
        }
    }

    public class CharacterContextProvider : CharacterContextProvider<CharacterContext>
    {
        [SerializeField] private CharacterContext context = new CharacterContext();

        public override CharacterContext Context => context;
    }
}
