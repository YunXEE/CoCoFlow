using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Pooling;
using CoCoFlow.Runtime.Pooling.Temporal;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CoCoFlow.Runtime.Modules.Map.Temporal.Tests
{
    public sealed class CoCoMapTemporalBindingPlayModeTests
    {
        [SetUp]
        public void SetUp()
        {
            ResetStateGraphProjectBindings();
        }

        [TearDown]
        public void TearDown()
        {
            ResetStateGraphProjectBindings();
        }

        [UnityTest]
        public IEnumerator MapPoolProjectDecoratorDelegatesFullLifecycleInOrder() =>
            UniTask.ToCoroutine(RunDecoratorLifecycleAsync);

        private static async UniTask RunDecoratorLifecycleAsync()
        {
            DecoratorFixture fixture = CreateFixture();
            try
            {
                Assert.That(
                    fixture.Host.TryStart(
                        out CoCoDiagnostic start),
                    Is.True,
                    start.Message);
                Assert.That(
                    fixture.Host.TryStep(
                        0.1d,
                        out CoCoDiagnostic firstStep),
                    Is.True,
                    firstStep.Message);
                Assert.That(
                    fixture.Host.TryStep(
                        0.1d,
                        out CoCoDiagnostic secondStep),
                    Is.True,
                    secondStep.Message);

                Assert.That(fixture.Host.TemporalState.Count, Is.GreaterThan(1));
                Assert.That(
                    ReadPoolHistoryCount(fixture.PoolTemporalBinding),
                    Is.EqualTo(fixture.Host.TemporalState.Count),
                    "Map must delegate every published forward capture to the optional Pool participant.");

                RegionRuntimeSnapshot beforePreview =
                    fixture.Region.CaptureSnapshot();
                string demandBefore = DemandSignature(beforePreview);
                int transitionsBefore = fixture.Sink.RequestCount;

                Assert.That(
                    fixture.Host.TryBeginTemporalPreview(
                        out CoCoDiagnostic begin),
                    Is.True,
                    begin.Message);
                Assert.That(
                    fixture.Host.TryPreviewTemporal(
                        1,
                        out CoCoDiagnostic preview),
                    Is.True,
                    preview.Message);

                Assert.That(fixture.ProjectProbe.ApplyCount, Is.EqualTo(1));
                Assert.That(
                    fixture.ProjectProbe.ApplyKinds[0],
                    Is.EqualTo(CoCoContextRestoreApplyKind.Preview));
                Assert.That(
                    fixture.ProjectProbe.MapProjectionPreparedDuringApply[0],
                    Is.True,
                    "Map projection preparation must precede the project Restore callback.");
                Assert.That(
                    fixture.ProjectProbe.PoolProjectionPreparedDuringApply[0],
                    Is.True,
                    "Pool projection preparation must precede the project Restore callback.");
                Assert.That(
                    fixture.ProjectProbe.MapAvailabilityRetainedDuringApply[0],
                    Is.True,
                    "The project callback must run only after Map's retained availability barrier.");
                Assert.That(
                    IsMapProjectionPrepared(fixture.MapTemporalBinding),
                    Is.False,
                    "Map projection completion must run after the downstream callback.");
                Assert.That(
                    IsPoolProjectionPrepared(fixture.PoolTemporalBinding),
                    Is.False,
                    "Pool projection completion must run after the downstream callback.");

                Assert.That(
                    fixture.Host.TryCancelTemporalPreview(
                        out CoCoDiagnostic cancel),
                    Is.True,
                    cancel.Message);
                Assert.That(fixture.ProjectProbe.ApplyCount, Is.EqualTo(2));
                Assert.That(
                    fixture.ProjectProbe.ApplyKinds[1],
                    Is.EqualTo(CoCoContextRestoreApplyKind.Cancel));
                Assert.That(
                    fixture.ProjectProbe.MapProjectionPreparedDuringApply[1],
                    Is.True);
                Assert.That(
                    fixture.ProjectProbe.PoolProjectionPreparedDuringApply[1],
                    Is.True);
                Assert.That(
                    fixture.ProjectProbe.MapAvailabilityRetainedDuringApply[1],
                    Is.True);

                Assert.That(
                    DemandSignature(fixture.Region.CaptureSnapshot()),
                    Is.EqualTo(demandBefore),
                    "Preview and Cancel must not mutate Map Demand ownership.");
                Assert.That(
                    fixture.Sink.RequestCount,
                    Is.EqualTo(transitionsBefore),
                    "Preview must not dispatch Map loading or tier transitions.");
            }
            finally
            {
                await CleanupFixtureAsync(fixture);
            }
        }

        private static DecoratorFixture CreateFixture()
        {
            ReflectedHostScenario scenario = CreateStateGraphScenario(3);

            var systemsObject =
                new GameObject("Pre10 Map Temporal Decorator Systems");
            systemsObject.SetActive(false);
            systemsObject.transform.SetParent(
                scenario.Root.transform,
                false);
            CoCoContentHost contentHost =
                systemsObject.AddComponent<CoCoContentHost>();
            CoCoPoolHost poolHost =
                systemsObject.AddComponent<CoCoPoolHost>();
            SetField(poolHost, "contentHost", contentHost);
            systemsObject.SetActive(true);
            Assert.That(
                contentHost.IsInitialized,
                Is.True,
                contentHost.LastDiagnostic.Message);
            Assert.That(
                poolHost.IsInitialized,
                Is.True,
                poolHost.LastDiagnostic.Message);

            RegionMainThreadGuard.CaptureCurrentThread();
            Assert.That(
                RegionRuntime.TryCreate(
                    contentHost.Runtime,
                    out RegionRuntime region,
                    out CoCoDiagnostic regionDiagnostic),
                Is.True,
                regionDiagnostic.Message);
            Assert.That(
                RegionId.TryCreate(
                    "tests.map.temporal.decorator",
                    out RegionId regionId),
                Is.True);
            Assert.That(
                RegionChunkId.TryCreate(
                    "wilderness",
                    out RegionChunkId chunkId),
                Is.True);
            Assert.That(
                RegionCoverage.TryCreateChunks(
                    new[] { chunkId },
                    out RegionCoverage coverage),
                Is.True);
            var sink = new ImmediateTransitionSink(region);
            sink.Configure(regionId, chunkId);
            Assert.That(
                region.TryAttachTransitionSink(
                    sink,
                    out CoCoDiagnostic sinkDiagnostic),
                Is.True,
                sinkDiagnostic.Message);

            Assert.That(
                RegionDemandOwnerId.TryCreate(
                    "tests.map.temporal.decorator.gameplay",
                    out RegionDemandOwnerId ownerId),
                Is.True);
            Assert.That(
                region.TryCreateDemandScope(
                    ownerId,
                    out RegionDemandScope gameplayScope,
                    out CoCoDiagnostic scopeDiagnostic),
                Is.True,
                scopeDiagnostic.Message);
            Assert.That(
                gameplayScope.TryDemand(
                    regionId,
                    FullCapabilities(),
                    coverage,
                    out RegionDemandLease gameplayLease,
                    out _,
                    out CoCoDiagnostic demandDiagnostic),
                Is.True,
                demandDiagnostic.Message);

            var mapHostObject =
                new GameObject("Pre10 Injected Map Host");
            mapHostObject.SetActive(false);
            mapHostObject.transform.SetParent(
                scenario.Root.transform,
                false);
            CoCoMapHost mapHost =
                mapHostObject.AddComponent<CoCoMapHost>();
            SetPrivateProperty(mapHost, "Runtime", region);
            Assert.That(mapHost.IsInitialized, Is.True);

            DecoratorProjectRestoreProbe projectProbe =
                scenario.Root.AddComponent<DecoratorProjectRestoreProbe>();
            CoCoPoolTemporalBinding poolTemporal =
                scenario.Root.AddComponent<CoCoPoolTemporalBinding>();
            SetField(poolTemporal, "stateGraphHost", scenario.Host);
            SetField(poolTemporal, "poolHost", poolHost);
            SetField(
                poolTemporal,
                "downstreamRestoreBinding",
                projectProbe);

            CoCoMapTemporalBinding mapTemporal =
                scenario.Root.AddComponent<CoCoMapTemporalBinding>();
            SetField(mapTemporal, "stateGraphHost", scenario.Host);
            SetField(mapTemporal, "mapHost", mapHost);
            SetField(
                mapTemporal,
                "downstreamRestoreBinding",
                poolTemporal);
            SetField(
                scenario.Host,
                "contextRestoreBinding",
                mapTemporal);

            projectProbe.Configure(
                region,
                mapTemporal,
                poolTemporal,
                regionId);
            return new DecoratorFixture(
                scenario,
                contentHost,
                poolHost,
                region,
                sink,
                gameplayScope,
                gameplayLease,
                mapTemporal,
                poolTemporal,
                projectProbe);
        }

        private static async UniTask CleanupFixtureAsync(
            DecoratorFixture fixture)
        {
            if (fixture == null) return;

            fixture.Host.TryStop(out _);
            fixture.GameplayScope.Dispose();
            await fixture.Region.ShutdownAsync();
            await fixture.PoolHost.ShutdownAsync();
            await fixture.ContentHost.ShutdownAsync();

            if (fixture.Scenario.Root != null)
            {
                Object.DestroyImmediate(fixture.Scenario.Root);
            }

            if (fixture.Scenario.Asset != null)
            {
                Object.DestroyImmediate(fixture.Scenario.Asset);
            }
        }

        private static ReflectedHostScenario CreateStateGraphScenario(
            int historyCapacity)
        {
            Type harnessType = Type.GetType(
                "CoCoFlow.Tests.Runtime.StateGraphHost.TemporalHostTestHarness, CoCoFlow.Tests.Runtime.StateGraphHost",
                true);
            MethodInfo create = harnessType.GetMethod(
                "Create",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(create, Is.Not.Null);
            object scenario = create.Invoke(
                null,
                new object[] { historyCapacity, false, true });
            Assert.That(scenario, Is.Not.Null);

            return new ReflectedHostScenario(
                (GameObject)GetProperty(scenario, "GameObject"),
                (CoCoStateGraphHost)GetProperty(scenario, "Host"),
                (Object)GetProperty(scenario, "Asset"));
        }

        private static void ResetStateGraphProjectBindings()
        {
            Type bridgeType = Type.GetType(
                "CoCoFlow.Tests.Runtime.StateGraphHost.StateGraphHostPoolingTestBridge, CoCoFlow.Tests.Runtime.StateGraphHost",
                false);
            MethodInfo reset = bridgeType?.GetMethod(
                "ResetProjectBindings",
                BindingFlags.Static | BindingFlags.NonPublic);
            reset?.Invoke(null, null);
        }

        private static object GetProperty(
            object target,
            string propertyName)
        {
            PropertyInfo property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            Assert.That(
                property,
                Is.Not.Null,
                target.GetType().FullName + "." + propertyName);
            return property.GetValue(target);
        }

        private static void SetField(
            object target,
            string fieldName,
            object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(
                field,
                Is.Not.Null,
                target.GetType().FullName + "." + fieldName);
            field.SetValue(target, value);
        }

        private static void SetPrivateProperty(
            object target,
            string propertyName,
            object value)
        {
            PropertyInfo property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            MethodInfo setter = property?.GetSetMethod(true);
            Assert.That(
                setter,
                Is.Not.Null,
                target.GetType().FullName + "." + propertyName);
            setter.Invoke(target, new[] { value });
        }

        private static object ReadPrivateField(
            object target,
            string fieldName)
        {
            Assert.That(target, Is.Not.Null);
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(
                field,
                Is.Not.Null,
                target.GetType().FullName + "." + fieldName);
            return field.GetValue(target);
        }

        private static bool IsMapProjectionPrepared(
            CoCoMapTemporalBinding binding)
        {
            object runtime = ReadPrivateField(binding, "runtime");
            return (bool)ReadPrivateField(runtime, "projectionPrepared");
        }

        private static bool IsPoolProjectionPrepared(
            CoCoPoolTemporalBinding binding)
        {
            object runtime = ReadPrivateField(binding, "_runtime");
            return (bool)ReadPrivateField(runtime, "_projectionPrepared");
        }

        private static int ReadPoolHistoryCount(
            CoCoPoolTemporalBinding binding)
        {
            object runtime = ReadPrivateField(binding, "_runtime");
            object history = ReadPrivateField(runtime, "_history");
            PropertyInfo count = history.GetType().GetProperty(
                "Count",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(count, Is.Not.Null);
            return (int)count.GetValue(history);
        }

        private static RegionCapabilitySet FullCapabilities()
        {
            Assert.That(
                RegionCapabilitySet.TryCreate(
                    new[]
                    {
                        RegionCapabilityId.Represented,
                        RegionCapabilityId.Background,
                        RegionCapabilityId.Enterable,
                        RegionCapabilityId.Full
                    },
                    out RegionCapabilitySet capabilities),
                Is.True);
            return capabilities;
        }

        private static string DemandSignature(
            RegionRuntimeSnapshot snapshot)
        {
            var builder = new StringBuilder();
            for (int demandIndex = 0;
                 demandIndex < snapshot.Demands.Count;
                 demandIndex++)
            {
                RegionDemandRuntimeSnapshot demand =
                    snapshot.Demands[demandIndex];
                builder.Append(demand.OwnerId.Value)
                    .Append(':')
                    .Append(demand.Revision.Value)
                    .Append(':');
                for (int capabilityIndex = 0;
                     capabilityIndex < demand.Capabilities.Count;
                     capabilityIndex++)
                {
                    builder.Append(
                            demand.Capabilities
                                .Capabilities[capabilityIndex].Value)
                        .Append(',');
                }

                builder.Append('|');
            }

            return builder.ToString();
        }

        private sealed class DecoratorFixture
        {
            internal DecoratorFixture(
                ReflectedHostScenario scenario,
                CoCoContentHost contentHost,
                CoCoPoolHost poolHost,
                RegionRuntime region,
                ImmediateTransitionSink sink,
                RegionDemandScope gameplayScope,
                RegionDemandLease gameplayLease,
                CoCoMapTemporalBinding mapTemporalBinding,
                CoCoPoolTemporalBinding poolTemporalBinding,
                DecoratorProjectRestoreProbe projectProbe)
            {
                Scenario = scenario;
                ContentHost = contentHost;
                PoolHost = poolHost;
                Region = region;
                Sink = sink;
                GameplayScope = gameplayScope;
                GameplayLease = gameplayLease;
                MapTemporalBinding = mapTemporalBinding;
                PoolTemporalBinding = poolTemporalBinding;
                ProjectProbe = projectProbe;
            }

            internal ReflectedHostScenario Scenario { get; }
            internal CoCoStateGraphHost Host => Scenario.Host;
            internal CoCoContentHost ContentHost { get; }
            internal CoCoPoolHost PoolHost { get; }
            internal RegionRuntime Region { get; }
            internal ImmediateTransitionSink Sink { get; }
            internal RegionDemandScope GameplayScope { get; }
            internal RegionDemandLease GameplayLease { get; }
            internal CoCoMapTemporalBinding MapTemporalBinding { get; }
            internal CoCoPoolTemporalBinding PoolTemporalBinding { get; }
            internal DecoratorProjectRestoreProbe ProjectProbe { get; }
        }

        private sealed class ReflectedHostScenario
        {
            internal ReflectedHostScenario(
                GameObject root,
                CoCoStateGraphHost host,
                Object asset)
            {
                Root = root;
                Host = host;
                Asset = asset;
            }

            internal GameObject Root { get; }
            internal CoCoStateGraphHost Host { get; }
            internal Object Asset { get; }
        }

        private sealed class ImmediateTransitionSink :
            IRegionDemandTransitionSink
        {
            private readonly RegionRuntime runtime;
            private RegionId regionId;
            private readonly HashSet<RegionChunkId> knownChunks =
                new HashSet<RegionChunkId>();

            internal ImmediateTransitionSink(RegionRuntime runtime)
            {
                this.runtime = runtime;
            }

            internal int RequestCount { get; private set; }
            public bool IsInvokingParticipantCallback => false;

            internal void Configure(
                RegionId configuredRegionId,
                params RegionChunkId[] chunks)
            {
                regionId = configuredRegionId;
                knownChunks.Clear();
                for (int index = 0; index < chunks.Length; index++)
                {
                    knownChunks.Add(chunks[index]);
                }
            }

            public bool TryValidateDemand(
                RegionId requestedRegionId,
                RegionCapabilitySet capabilities,
                RegionCoverage coverage,
                out CoCoDiagnostic diagnostic)
            {
                if (requestedRegionId != regionId)
                {
                    diagnostic = RegionErrors.DemandConflict(
                        "Unknown Region.");
                    return false;
                }

                if (!coverage.CoversAll)
                {
                    for (int index = 0;
                         index < coverage.Chunks.Count;
                         index++)
                    {
                        if (!knownChunks.Contains(coverage.Chunks[index]))
                        {
                            diagnostic = RegionErrors.InvalidCoverage(
                                "Unknown Chunk.");
                            return false;
                        }
                    }
                }

                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            public void RequestTransition(
                RegionDemandResolution resolution)
            {
                RequestCount++;
                runtime.PublishTransitionProgress(
                    resolution.RegionId,
                    resolution.DesiredGeneration,
                    knownChunks,
                    0,
                    0,
                    false,
                    false,
                    false,
                    CoCoDiagnostic.None);
                runtime.PublishTransitionReady(
                    resolution.RegionId,
                    resolution.DesiredGeneration);
            }

            public bool TryAcceptRetry(
                RegionId requestedRegionId,
                RegionDemandResolution resolution,
                out CoCoDiagnostic diagnostic)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            public void StartAcceptedRetry(
                RegionId requestedRegionId,
                RegionDemandResolution resolution)
            {
                RequestTransition(resolution);
            }

            public UniTask<CoCoDiagnostic> ShutdownAsync() =>
                UniTask.FromResult(CoCoDiagnostic.None);

            public void ForceShutdown()
            {
            }
        }
    }

    internal sealed class DecoratorProjectRestoreProbe :
        MonoBehaviour,
        ICoCoContextRestoreBinding
    {
        private RegionRuntime region;
        private CoCoMapTemporalBinding mapBinding;
        private CoCoPoolTemporalBinding poolBinding;
        private RegionId regionId;

        internal int ApplyCount { get; private set; }
        internal List<CoCoContextRestoreApplyKind> ApplyKinds { get; } =
            new List<CoCoContextRestoreApplyKind>();
        internal List<bool> MapProjectionPreparedDuringApply { get; } =
            new List<bool>();
        internal List<bool> PoolProjectionPreparedDuringApply { get; } =
            new List<bool>();
        internal List<bool> MapAvailabilityRetainedDuringApply { get; } =
            new List<bool>();

        internal void Configure(
            RegionRuntime configuredRegion,
            CoCoMapTemporalBinding configuredMapBinding,
            CoCoPoolTemporalBinding configuredPoolBinding,
            RegionId configuredRegionId)
        {
            region = configuredRegion;
            mapBinding = configuredMapBinding;
            poolBinding = configuredPoolBinding;
            regionId = configuredRegionId;
        }

        public bool TryApply(
            in CoCoContextRestoreBindingContext context,
            out CoCoDiagnostic diagnostic)
        {
            ApplyCount++;
            ApplyKinds.Add(context.ApplyKind);
            MapProjectionPreparedDuringApply.Add(
                ReadPreparedFlag(
                    mapBinding,
                    "runtime",
                    "projectionPrepared"));
            PoolProjectionPreparedDuringApply.Add(
                ReadPreparedFlag(
                    poolBinding,
                    "_runtime",
                    "_projectionPrepared"));
            MapAvailabilityRetainedDuringApply.Add(
                IsAvailabilityRetained());
            diagnostic = CoCoDiagnostic.None;
            return context.IsValid;
        }

        private bool IsAvailabilityRetained()
        {
            if (region == null) return false;

            RegionRuntimeSnapshot snapshot = region.CaptureSnapshot();
            bool hasRetention = false;
            for (int index = 0; index < snapshot.Demands.Count; index++)
            {
                if (snapshot.Demands[index].OwnerId.Value.StartsWith(
                        "cocoflow.map.temporal.",
                        StringComparison.Ordinal))
                {
                    hasRetention = true;
                    break;
                }
            }

            for (int index = 0; index < snapshot.Regions.Count; index++)
            {
                RegionRuntimeRegionSnapshot regionSnapshot =
                    snapshot.Regions[index];
                if (regionSnapshot.RegionId == regionId)
                {
                    return hasRetention &&
                           regionSnapshot.CommittedCapabilities
                               .Contains(RegionCapabilityId.Full);
                }
            }

            return false;
        }

        private static bool ReadPreparedFlag(
            object binding,
            string runtimeFieldName,
            string preparedFieldName)
        {
            if (binding == null) return false;

            FieldInfo runtimeField = binding.GetType().GetField(
                runtimeFieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            object runtime = runtimeField?.GetValue(binding);
            if (runtime == null) return false;

            FieldInfo preparedField = runtime.GetType().GetField(
                preparedFieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            return preparedField != null &&
                   (bool)preparedField.GetValue(runtime);
        }
    }
}
