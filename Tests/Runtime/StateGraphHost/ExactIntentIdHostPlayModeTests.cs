using System;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    public sealed class ExactIntentIdHostPlayModeTests
    {
        [Test]
        public void LowLevelBindingsKeepSameTypedIdsDistinctAndStateReadsOnlyItsDeclaredId()
        {
            CoCoStateGraphProjectBindings.ResetForTests();
            var provider = new ExactProvider();
            var asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            var actor = new GameObject("ExactIntentIds");
            try
            {
                Assert.That(CoCoStateGraphProjectBindings.TryInstall(provider, out CoCoDiagnostic install), Is.True, install.Message);
                asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
                var layer = new CoCoStateGraphLayerRecord(new CoCoSerializedId128(606, 1), "Exact");
                for (int i = 0; i < 2; i++)
                    layer.States.Add(new CoCoStateGraphStateRecord(new CoCoSerializedId128(607, (ulong)(i + 1)),
                        default, "State" + i, new CoCoSerializedId128(608, (ulong)(i + 1)), new EmptyStateConfig()));
                layer.InitialStateId = layer.States[0].StateId;
                asset.Layers.Add(layer);
                var host = actor.AddComponent<CoCoStateGraphHost>();
                var first = actor.AddComponent<AuthorMoveSource>(); first.Value = 11f;
                var second = actor.AddComponent<AuthorMoveSource>(); second.Value = 23f;
                Set(host, "autoStart", false); Set(host, "driver", (CoCoStateGraphDriver)2);
                Set(host, "stateGraphAsset", asset); Set(host, "intentSources", new MonoBehaviour[] { first, second });
                Set(host, "operators", Array.Empty<MonoBehaviour>());
                Assert.That(host.TryStart(out CoCoDiagnostic start), Is.True, start.Message);
                Assert.That(host.TryStep(0.02d, out CoCoDiagnostic step), Is.True, step.Message);
                Assert.That(AuthorExactLogic.TypedValue, Is.EqualTo(23f));
                Assert.That(AuthorExactLogic.LegacyValue, Is.EqualTo(23f));
                Assert.That(AuthorExactLogic.OtherStateHandleRead, Is.False);
                Assert.That(first.Samples, Is.EqualTo(1)); Assert.That(second.Samples, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(actor); Object.DestroyImmediate(asset);
                CoCoStateGraphProjectBindings.ResetForTests();
            }
        }

        private static void Set(CoCoStateGraphHost host, string name, object value) =>
            typeof(CoCoStateGraphHost).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(host, value);

        private sealed class ExactProvider : ICoCoStateGraphProjectBindingProvider
        {
            private readonly CoCoIntentId[] _intents = new CoCoIntentId[2];
            private readonly CoCoStateDescriptorId[] _descriptors = new CoCoStateDescriptorId[2];
            public CoCoGraphDescriptorCatalog Catalog { get; }
            public ExactProvider()
            {
                var builder = new CoCoGraphDescriptorCatalogBuilder();
                for (int i = 0; i < 2; i++)
                {
                    CoCoIntentId.TryCreate(609, (ulong)(i + 1), out _intents[i]);
                    CoCoStateDescriptorId.TryCreate(608, (ulong)(i + 1), out _descriptors[i]);
                    Require(builder.TryRegisterIntent(_intents[i], 1,
                        new CoCoIntentReducerFactoryToken<AuthorMoveIntent, AuthorMoveReducer, AuthorMoveFactory>(6), out CoCoDiagnostic intent), intent);
                }
                for (int i = 0; i < 2; i++)
                {
                    CoCoStateBlockId block = StandardGraphState.BlockFor(_descriptors[i]);
                    Require(builder.TryRegisterStateBlock(block, CoCoStateBlockOwner.Graph, out CoCoDiagnostic blockResult), blockResult);
                    Require(builder.TryRegisterStateSlot(block, StandardGraphState.SlotFor(_descriptors[i]),
                        CoCoContextProjection.Temporal, CoCoContextRestorePolicy.Stored, default(CoCoGraphStateRecord<int>),
                        StatelessMemory.Fingerprint, default, null, out CoCoDiagnostic slot), slot);
                    Require(builder.TryRegisterState<AuthorExactLogic, EmptyStateConfig, EmptyConfigSchema, StatelessMemory>(
                        _descriptors[i], 1, new EmptyStateConfig.Freezer(),
                        new CoCoStateRuntimeRegistration<AuthorExactLogic, EmptyConfigSchema, StatelessMemory>(EmptySchemas.State),
                        new[] { _intents[1 - i] }, null, new[] { block }, out CoCoDiagnostic state), state);
                }
                Require(builder.TryFreeze(out CoCoGraphDescriptorCatalog catalog, out CoCoDiagnostic frozen), frozen);
                Catalog = catalog;
            }
            public bool TryConfigure(CoCoStateGraphHostBindingBuilder builder, out CoCoDiagnostic diagnostic)
            {
                var handles = new CoCoIntentHandle<AuthorMoveIntent>[2];
                for (int i = 0; i < 2; i++)
                    if (!builder.TryRegisterIntent<AuthorMoveIntent, AuthorMoveReducer, AuthorMoveFactory>(
                            _intents[i], new AuthorMoveFactory(), 6, out handles[i], out diagnostic)) return false;
                if (!builder.TryBeginIntentBindings(out diagnostic)) return false;
                for (int i = 0; i < 2; i++)
                {
                    CoCoIntentSourceRequirement<AuthorMoveIntent>.TryCreate(handles[i], 1, out var requirement);
                    if (!builder.TryBindIntentSource(i, requirement, out diagnostic)) return false;
                    var factory = new CoCoStateRuntimeFactory<AuthorExactLogic, StatelessMemory>(
                        context => new AuthorExactLogic(handles[0]), () => new StatelessMemory(),
                        (source, destination) => { }, memory => { }, memory => StatelessMemory.Fingerprint);
                    if (!builder.TryBindState(_descriptors[i], factory, out diagnostic)) return false;
                }
                foreach (CoCoContextStateBlockRequirement block in builder.Graph.ContextStateRequirements.Blocks)
                {
                    int i = block.BlockId == StandardGraphState.BlockFor(_descriptors[0]) ? 0 : 1;
                    CoCoLayerId.TryCreate(606, 1, out CoCoLayerId layer);
                    CoCoStateId.TryCreate(607, (ulong)(i + 1), out CoCoStateId state);
                    CoCoActivationId.TryCreate(1, out CoCoActivationId activation);
                    CoCoGraphStateRecord<int> record;
                    if (i == 0) CoCoGraphStateRecord<int>.TryCreate(layer, state, true, activation, 0d, 0d, true,
                        StatelessMemory.Fingerprint, 0, out record);
                    else CoCoGraphStateRecord<int>.TryCreateInactive(layer, state, StatelessMemory.Fingerprint, 0, out record);
                    if (!builder.TryBindGraphStateSlot<StatelessMemory, int, StatelessMemoryBinding>(layer, state,
                            StandardGraphState.BlockFor(_descriptors[i]), StandardGraphState.SlotFor(_descriptors[i]),
                            record, StatelessMemory.Fingerprint, new StatelessMemoryBinding(), out diagnostic)) return false;
                }
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
            private static void Require(bool success, CoCoDiagnostic diagnostic)
            {
                if (!success) throw new InvalidOperationException(diagnostic.Message);
            }
        }
    }
}
