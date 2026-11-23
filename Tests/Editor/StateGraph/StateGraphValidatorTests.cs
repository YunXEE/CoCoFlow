using System;
using System.Linq;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using NUnit.Framework;

namespace CoCoFlow.Tests.Editor.StateGraph
{
    public sealed class StateGraphValidatorTests
    {
        private GraphDescriptorCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            catalog = StateGraphTestFactory.CreateCatalog(false);
        }

        [Test]
        public void AnyErrorSuppressesCompiledGraphAndPreservesTransitionLocation()
        {
            StateId missingTarget = StateGraphTestFactory.CreateStateId(999UL);
            var transition = new TransitionSource(
                StateGraphTestFactory.FirstTransitionId,
                StateGraphTestFactory.RootStateId,
                missingTarget,
                0,
                TransitionWindow.Always,
                Array.Empty<ConditionSource>());
            var layer = new StateLayerSource(
                StateGraphTestFactory.LayerId,
                StateGraphTestFactory.RootStateId,
                new[]
                {
                    StateGraphTestFactory.State(
                        StateGraphTestFactory.RootStateId,
                        default,
                        default,
                        1)
                },
                new[] { transition });

            StateGraphCompileResult result = Compile(
                StateGraphTestFactory.Source(layer, 3001UL));

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.HasErrors);
            Assert.IsNull(result.Graph);
            GraphDiagnostic diagnostic = RequireDiagnostic(
                result,
                CoCoDiagnosticCode.MissingTopologyElement,
                GraphField.TargetState);
            Assert.AreEqual(GraphElementKind.Transition, diagnostic.Location.ElementKind);
            Assert.AreEqual(StateGraphTestFactory.GraphId, diagnostic.Location.GraphId);
            Assert.AreEqual(StateGraphTestFactory.LayerId, diagnostic.Location.LayerId);
            Assert.AreEqual(StateGraphTestFactory.FirstTransitionId, diagnostic.Location.TransitionId);
            Assert.AreEqual(0, diagnostic.Location.LayerIndex);
            Assert.AreEqual(0, diagnostic.Location.TransitionIndex);
        }

        [Test]
        public void UnreachableStateIsWarningAndDoesNotSuppressCompiledGraph()
        {
            StateId unreachableId = StateGraphTestFactory.SecondChildStateId;
            var layer = new StateLayerSource(
                StateGraphTestFactory.LayerId,
                StateGraphTestFactory.RootStateId,
                new[]
                {
                    StateGraphTestFactory.State(
                        StateGraphTestFactory.RootStateId,
                        default,
                        default,
                        1),
                    StateGraphTestFactory.State(unreachableId, default, default, 2)
                },
                Array.Empty<TransitionSource>());

            StateGraphCompileResult result = Compile(
                StateGraphTestFactory.Source(layer, 3002UL));

            Assert.IsTrue(result.Succeeded);
            Assert.IsFalse(result.HasErrors);
            Assert.IsNotNull(result.Graph);
            GraphDiagnostic warning = RequireDiagnostic(
                result,
                CoCoDiagnosticCode.UnreachableState,
                GraphField.None);
            Assert.IsTrue(warning.Diagnostic.IsWarning);
            Assert.IsFalse(warning.IsError);
            Assert.AreEqual(GraphElementKind.State, warning.Location.ElementKind);
            Assert.AreEqual(StateGraphTestFactory.GraphId, warning.Location.GraphId);
            Assert.AreEqual(unreachableId, warning.Location.StateId);
            Assert.AreEqual(1, warning.Location.StateIndex);
        }

        [Test]
        public void ParentHierarchyCycleIsRejectedAtParentField()
        {
            StateId first = StateGraphTestFactory.RootStateId;
            StateId second = StateGraphTestFactory.FirstChildStateId;
            var layer = new StateLayerSource(
                StateGraphTestFactory.LayerId,
                first,
                new[]
                {
                    StateGraphTestFactory.State(first, second, second, 1),
                    StateGraphTestFactory.State(second, first, first, 2)
                },
                Array.Empty<TransitionSource>());

            StateGraphCompileResult result = Compile(
                StateGraphTestFactory.Source(layer, 3003UL));

            Assert.IsNull(result.Graph);
            Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
                diagnostic.Diagnostic.Code == CoCoDiagnosticCode.ParentStateCycle &&
                diagnostic.Location.ElementKind == GraphElementKind.State &&
                diagnostic.Location.Field == GraphField.ParentState));
        }

        [Test]
        public void CompositeWithoutDirectInitialChildIsRejectedAtInitialChildField()
        {
            var layer = new StateLayerSource(
                StateGraphTestFactory.LayerId,
                StateGraphTestFactory.RootStateId,
                new[]
                {
                    StateGraphTestFactory.State(
                        StateGraphTestFactory.RootStateId,
                        default,
                        default,
                        1),
                    StateGraphTestFactory.State(
                        StateGraphTestFactory.FirstChildStateId,
                        StateGraphTestFactory.RootStateId,
                        default,
                        2)
                },
                Array.Empty<TransitionSource>());

            StateGraphCompileResult result = Compile(
                StateGraphTestFactory.Source(layer, 3004UL));

            Assert.IsNull(result.Graph);
            GraphDiagnostic diagnostic = RequireDiagnostic(
                result,
                CoCoDiagnosticCode.InvalidInitialState,
                GraphField.InitialChildState);
            Assert.AreEqual(StateGraphTestFactory.RootStateId, diagnostic.Location.StateId);
        }

        [Test]
        public void CrossLayerTransitionIsRejectedAtEndpointField()
        {
            LayerId secondLayerId = StateGraphTestFactory.CreateLayerId(2UL);
            StateId secondLayerStateId = StateGraphTestFactory.CreateStateId(40UL);
            var crossLayerTransition = new TransitionSource(
                StateGraphTestFactory.FirstTransitionId,
                StateGraphTestFactory.RootStateId,
                secondLayerStateId,
                0,
                TransitionWindow.Always,
                Array.Empty<ConditionSource>());
            var firstLayer = new StateLayerSource(
                StateGraphTestFactory.LayerId,
                StateGraphTestFactory.RootStateId,
                new[]
                {
                    StateGraphTestFactory.State(
                        StateGraphTestFactory.RootStateId,
                        default,
                        default,
                        1)
                },
                new[] { crossLayerTransition });
            var secondLayer = new StateLayerSource(
                secondLayerId,
                secondLayerStateId,
                new[]
                {
                    StateGraphTestFactory.State(secondLayerStateId, default, default, 2)
                },
                Array.Empty<TransitionSource>());
            var source = new StateGraphSource(
                StateGraphCompiler.CurrentSchemaVersion,
                3005UL,
                StateGraphTestFactory.GraphId,
                new[] { firstLayer, secondLayer },
                Array.Empty<EventToIntentDeclarationSource>());

            StateGraphCompileResult result = Compile(source);

            Assert.IsNull(result.Graph);
            GraphDiagnostic diagnostic = RequireDiagnostic(
                result,
                CoCoDiagnosticCode.CrossLayerReference,
                GraphField.TargetState);
            Assert.AreEqual(GraphElementKind.Transition, diagnostic.Location.ElementKind);
            Assert.AreEqual(0, diagnostic.Location.LayerIndex);
        }

        [Test]
        public void DuplicateStateIdIsRejectedAtSecondStateIdentity()
        {
            var duplicate = StateGraphTestFactory.State(
                StateGraphTestFactory.RootStateId,
                default,
                default,
                2);
            var layer = new StateLayerSource(
                StateGraphTestFactory.LayerId,
                StateGraphTestFactory.RootStateId,
                new[]
                {
                    StateGraphTestFactory.State(
                        StateGraphTestFactory.RootStateId,
                        default,
                        default,
                        1),
                    duplicate
                },
                Array.Empty<TransitionSource>());

            StateGraphCompileResult result = Compile(
                StateGraphTestFactory.Source(layer, 3006UL));

            Assert.IsNull(result.Graph);
            GraphDiagnostic diagnostic = RequireDiagnostic(
                result,
                CoCoDiagnosticCode.DuplicateIdentifier,
                GraphField.Identifier);
            Assert.AreEqual(GraphElementKind.State, diagnostic.Location.ElementKind);
            Assert.AreEqual(1, diagnostic.Location.StateIndex);
        }

        [Test]
        public void InvalidGraphHeaderFieldsAreAllLocatedAndSuppressOutput()
        {
            StateGraphSource valid = StateGraphTestFactory.CreateTerminalSource();
            var source = new StateGraphSource(
                StateGraphCompiler.CurrentSchemaVersion + 1U,
                0UL,
                default,
                valid.Layers,
                Array.Empty<EventToIntentDeclarationSource>());

            StateGraphCompileResult result = Compile(source);

            Assert.IsNull(result.Graph);
            Assert.AreEqual(0UL, result.ContentFingerprint);
            Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
                diagnostic.Diagnostic.Code == CoCoDiagnosticCode.UnsupportedSchemaVersion &&
                diagnostic.Location.Field == GraphField.SchemaVersion));
            Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
                diagnostic.Location.Field == GraphField.ContentFingerprint));
            Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
                diagnostic.Diagnostic.Code == CoCoDiagnosticCode.InvalidIdentifier &&
                diagnostic.Location.Field == GraphField.Identifier));
        }

        [Test]
        public void DescriptorConfigTypeMismatchIsRejectedAtConfigField()
        {
            var invalidState = new StateSource(
                StateGraphTestFactory.RootStateId,
                default,
                default,
                StateGraphTestFactory.StateDescriptorId,
                StateGraphTestFactory.ConditionConfig(5));
            var layer = new StateLayerSource(
                StateGraphTestFactory.LayerId,
                invalidState.StateId,
                new[] { invalidState },
                Array.Empty<TransitionSource>());

            StateGraphCompileResult result = Compile(
                StateGraphTestFactory.Source(layer, 3007UL));

            Assert.IsNull(result.Graph);
            GraphDiagnostic diagnostic = RequireDiagnostic(
                result,
                CoCoDiagnosticCode.DescriptorTypeMismatch,
                GraphField.Config);
            Assert.AreEqual(GraphElementKind.State, diagnostic.Location.ElementKind);
        }

        [Test]
        public void InvalidTransitionWindowIsReported()
        {
            var transition = new TransitionSource(
                StateGraphTestFactory.FirstTransitionId,
                StateGraphTestFactory.RootStateId,
                StateGraphTestFactory.RootStateId,
                0,
                default,
                Array.Empty<ConditionSource>());
            var layer = new StateLayerSource(
                StateGraphTestFactory.LayerId,
                StateGraphTestFactory.RootStateId,
                new[]
                {
                    StateGraphTestFactory.State(
                        StateGraphTestFactory.RootStateId,
                        default,
                        default,
                        1)
                },
                new[] { transition });

            StateGraphCompileResult result = Compile(
                StateGraphTestFactory.Source(layer, 3008UL));

            Assert.IsNull(result.Graph);
            RequireDiagnostic(
                result,
                CoCoDiagnosticCode.InvalidTransitionWindow,
                GraphField.Window);
        }

        [TestCase(TransitionWindowMode.LocalSeconds, 0d, 1d, true)]
        [TestCase(TransitionWindowMode.LocalSeconds, 0d, double.MaxValue, true)]
        [TestCase(TransitionWindowMode.LocalSeconds, -1d, 1d, false)]
        [TestCase(TransitionWindowMode.LocalSeconds, 1d, 1d, false)]
        [TestCase(TransitionWindowMode.LocalSeconds, double.NaN, 1d, false)]
        [TestCase(TransitionWindowMode.LocalSeconds, 0d, double.PositiveInfinity, false)]
        [TestCase(TransitionWindowMode.LocalSeconds, double.NegativeInfinity, 1d, false)]
        [TestCase(TransitionWindowMode.ActionProgress, 0d, 1d, true)]
        [TestCase(TransitionWindowMode.ActionProgress, 0.25d, 0.75d, true)]
        [TestCase(TransitionWindowMode.ActionProgress, 0d, 1.000001d, false)]
        [TestCase(TransitionWindowMode.ActionProgress, 0.5d, 0.5d, false)]
        [TestCase(TransitionWindowMode.None, 0d, 1d, false)]
        public void TransitionWindowCreationEnforcesTheFrozenIntervalContract(
            TransitionWindowMode mode,
            double startInclusive,
            double endExclusive,
            bool expectedValid)
        {
            bool created = TransitionWindow.TryCreate(
                mode,
                startInclusive,
                endExclusive,
                out TransitionWindow window);

            Assert.AreEqual(expectedValid, created);
            Assert.AreEqual(expectedValid, window.IsValid);
            if (expectedValid)
            {
                Assert.AreEqual(mode, window.Mode);
                Assert.AreEqual(startInclusive, window.StartInclusive);
                Assert.AreEqual(endExclusive, window.EndExclusive);
            }
        }

        [Test]
        public void AlwaysWindowCanonicalizesItsInterval()
        {
            Assert.IsTrue(TransitionWindow.TryCreate(
                TransitionWindowMode.Always,
                double.NaN,
                double.PositiveInfinity,
                out TransitionWindow window));
            Assert.AreEqual(TransitionWindow.Always, window);
            Assert.AreEqual(0d, window.StartInclusive);
            Assert.AreEqual(0d, window.EndExclusive);
        }

        [Test]
        public void ActionProgressKeepsItsSerializedNumericValue()
        {
            Assert.AreEqual(3, (int)TransitionWindowMode.ActionProgress);
        }

        [Test]
        public void CompositeTransitionEndpointsAreRejectedAtTheirExactFields()
        {
            var transition = new TransitionSource(
                StateGraphTestFactory.FirstTransitionId,
                StateGraphTestFactory.RootStateId,
                StateGraphTestFactory.RootStateId,
                0,
                TransitionWindow.Always,
                Array.Empty<ConditionSource>());
            var layer = new StateLayerSource(
                StateGraphTestFactory.LayerId,
                StateGraphTestFactory.RootStateId,
                new[]
                {
                    StateGraphTestFactory.State(
                        StateGraphTestFactory.RootStateId,
                        default,
                        StateGraphTestFactory.FirstChildStateId,
                        1),
                    StateGraphTestFactory.State(
                        StateGraphTestFactory.FirstChildStateId,
                        StateGraphTestFactory.RootStateId,
                        default,
                        2)
                },
                new[] { transition });

            StateGraphCompileResult result = Compile(
                StateGraphTestFactory.Source(layer, 3101UL));

            Assert.IsFalse(result.Succeeded);
            RequireDiagnostic(
                result,
                CoCoDiagnosticCode.NonLeafTransitionEndpoint,
                GraphField.SourceState);
            RequireDiagnostic(
                result,
                CoCoDiagnosticCode.NonLeafTransitionEndpoint,
                GraphField.TargetState);
        }

        [Test]
        public void DuplicateOutgoingPrioritiesFromOneLeafAreRejected()
        {
            StateId stateId = StateGraphTestFactory.RootStateId;
            var layer = new StateLayerSource(
                StateGraphTestFactory.LayerId,
                stateId,
                new[] { StateGraphTestFactory.State(stateId, default, default, 1) },
                new[]
                {
                    Transition(
                        StateGraphTestFactory.FirstTransitionId,
                        stateId,
                        stateId,
                        7),
                    Transition(
                        StateGraphTestFactory.SecondTransitionId,
                        stateId,
                        stateId,
                        7)
                });

            StateGraphCompileResult result = Compile(
                StateGraphTestFactory.Source(layer, 3102UL));

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(
                2,
                result.Diagnostics.Count(diagnostic =>
                    diagnostic.Diagnostic.Code == CoCoDiagnosticCode.DuplicateTransitionPriority &&
                    diagnostic.Location.Field == GraphField.Priority));
        }

        [Test]
        public void EqualPrioritiesFromDifferentSourceLeavesAreAllowed()
        {
            StateId firstStateId = StateGraphTestFactory.RootStateId;
            StateId secondStateId = StateGraphTestFactory.SecondChildStateId;
            var layer = new StateLayerSource(
                StateGraphTestFactory.LayerId,
                firstStateId,
                new[]
                {
                    StateGraphTestFactory.State(firstStateId, default, default, 1),
                    StateGraphTestFactory.State(secondStateId, default, default, 2)
                },
                new[]
                {
                    Transition(
                        StateGraphTestFactory.FirstTransitionId,
                        firstStateId,
                        firstStateId,
                        7),
                    Transition(
                        StateGraphTestFactory.SecondTransitionId,
                        secondStateId,
                        secondStateId,
                        7)
                });

            StateGraphCompileResult result = Compile(
                StateGraphTestFactory.Source(layer, 3103UL));

            Assert.IsTrue(result.Succeeded);
            Assert.IsFalse(result.Diagnostics.Any(diagnostic =>
                diagnostic.Diagnostic.Code == CoCoDiagnosticCode.DuplicateTransitionPriority));
        }

        [Test]
        public void ActionProgressWindowRequiresAnOptedInSourceState()
        {
            Assert.IsTrue(TransitionWindow.TryCreate(
                TransitionWindowMode.ActionProgress,
                0.25d,
                0.75d,
                out TransitionWindow window));

            StateGraphCompileResult missingProvider = CompileSingleSelfLoop(window);
            Assert.IsFalse(missingProvider.Succeeded);
            RequireDiagnostic(
                missingProvider,
                CoCoDiagnosticCode.MissingActionProgressProvider,
                GraphField.Window);

            catalog = StateGraphTestFactory.CreateCatalog(
                false,
                providesActionProgress: true);
            StateGraphCompileResult providerDeclared = CompileSingleSelfLoop(window);
            Assert.IsTrue(providerDeclared.Succeeded);
            Assert.IsFalse(providerDeclared.Diagnostics.Any(diagnostic =>
                diagnostic.Diagnostic.Code == CoCoDiagnosticCode.MissingActionProgressProvider));
        }

        [Test]
        public void ParentAndChildOperationOverlapWarnsButStillCompiles()
        {
            catalog = StateGraphTestFactory.CreateCatalog(true);

            StateGraphCompileResult result = Compile(
                StateGraphTestFactory.CreateHierarchicalSource(3104UL));

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(
                2,
                result.Diagnostics.Count(diagnostic =>
                    diagnostic.Diagnostic.Code == CoCoDiagnosticCode.ActivePathOperationOverlap &&
                    diagnostic.Diagnostic.IsWarning &&
                    diagnostic.Location.Field == GraphField.Descriptor));
        }

        [Test]
        public void SameOperationAcrossLayersDoesNotWarn()
        {
            catalog = StateGraphTestFactory.CreateCatalog(true);
            LayerId secondLayerId = StateGraphTestFactory.CreateLayerId(2UL);
            StateId secondStateId = StateGraphTestFactory.CreateStateId(40UL);
            var source = new StateGraphSource(
                StateGraphCompiler.CurrentSchemaVersion,
                3105UL,
                StateGraphTestFactory.GraphId,
                new[]
                {
                    new StateLayerSource(
                        StateGraphTestFactory.LayerId,
                        StateGraphTestFactory.RootStateId,
                        new[]
                        {
                            StateGraphTestFactory.State(
                                StateGraphTestFactory.RootStateId,
                                default,
                                default,
                                1)
                        },
                        Array.Empty<TransitionSource>()),
                    new StateLayerSource(
                        secondLayerId,
                        secondStateId,
                        new[]
                        {
                            StateGraphTestFactory.State(secondStateId, default, default, 2)
                        },
                        Array.Empty<TransitionSource>())
                },
                Array.Empty<EventToIntentDeclarationSource>());

            StateGraphCompileResult result = Compile(source);

            Assert.IsTrue(result.Succeeded);
            Assert.IsFalse(result.Diagnostics.Any(diagnostic =>
                diagnostic.Diagnostic.Code == CoCoDiagnosticCode.ActivePathOperationOverlap));
        }

        [Test]
        public void MultiErrorDiagnosticOrderIsDeterministicAcrossRepeatedCompile()
        {
            var source = new StateGraphSource(
                StateGraphCompiler.CurrentSchemaVersion + 1U,
                0UL,
                default,
                System.Array.Empty<StateLayerSource>(),
                Array.Empty<EventToIntentDeclarationSource>());

            StateGraphCompileResult first = new StateGraphCompiler().Compile(source, catalog);
            StateGraphCompileResult second = new StateGraphCompiler().Compile(source, catalog);

            Assert.Greater(first.Diagnostics.Count, 1);
            CollectionAssert.AreEqual(first.Diagnostics, second.Diagnostics);
        }

        private StateGraphCompileResult Compile(StateGraphSource source) =>
            new StateGraphCompiler().Compile(source, catalog);

        private StateGraphCompileResult CompileSingleSelfLoop(TransitionWindow window)
        {
            var transition = new TransitionSource(
                StateGraphTestFactory.FirstTransitionId,
                StateGraphTestFactory.RootStateId,
                StateGraphTestFactory.RootStateId,
                0,
                window,
                Array.Empty<ConditionSource>());
            var layer = new StateLayerSource(
                StateGraphTestFactory.LayerId,
                StateGraphTestFactory.RootStateId,
                new[]
                {
                    StateGraphTestFactory.State(
                        StateGraphTestFactory.RootStateId,
                        default,
                        default,
                        1)
                },
                new[] { transition });
            return Compile(StateGraphTestFactory.Source(layer, 3100UL));
        }

        private static TransitionSource Transition(
            TransitionId transitionId,
            StateId sourceStateId,
            StateId targetStateId,
            int priority) =>
            new TransitionSource(
                transitionId,
                sourceStateId,
                targetStateId,
                priority,
                TransitionWindow.Always,
                Array.Empty<ConditionSource>());

        private static GraphDiagnostic RequireDiagnostic(
            StateGraphCompileResult result,
            CoCoDiagnosticCode code,
            GraphField field)
        {
            GraphDiagnostic[] matches = result.Diagnostics.Where(diagnostic =>
                diagnostic.Diagnostic.Code == code && diagnostic.Location.Field == field).ToArray();
            Assert.AreEqual(
                1,
                matches.Length,
                $"Expected exactly one {code} diagnostic at {field}, but found {matches.Length}.");
            return matches[0];
        }
    }
}
