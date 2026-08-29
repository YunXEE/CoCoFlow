using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Modules.Map
{
    public sealed class RegionProfileCompilationCache
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, RegionCompiledPlan> plans =
            new Dictionary<string, RegionCompiledPlan>(StringComparer.Ordinal);

        public int Count
        {
            get
            {
                lock (gate)
                {
                    return plans.Count;
                }
            }
        }

        public RegionCompileResult Compile(
            RegionBindingCompiler compiler,
            CoCoRegionBinding binding,
            RegionParticipantCatalog catalog,
            IRegionAddressableSceneResolver addressableSceneResolver = null)
        {
            if (compiler == null) throw new ArgumentNullException(nameof(compiler));

            RegionCompileResult result = compiler.Compile(
                binding,
                catalog,
                addressableSceneResolver);
            if (!result.Succeeded) return result;

            lock (gate)
            {
                if (plans.TryGetValue(
                        result.Plan.Fingerprint,
                        out RegionCompiledPlan cached))
                {
                    return new RegionCompileResult(
                        cached,
                        new List<RegionCompileDiagnostic>(result.Diagnostics));
                }

                plans.Add(result.Plan.Fingerprint, result.Plan);
                return result;
            }
        }

        public bool TryGet(
            string fingerprint,
            out RegionCompiledPlan plan)
        {
            if (string.IsNullOrEmpty(fingerprint))
            {
                plan = null;
                return false;
            }

            lock (gate)
            {
                return plans.TryGetValue(fingerprint, out plan);
            }
        }

        public void Clear()
        {
            lock (gate)
            {
                plans.Clear();
            }
        }
    }
}
