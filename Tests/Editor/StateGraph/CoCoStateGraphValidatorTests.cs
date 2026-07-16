using System;
using System.Linq;
using NUnit.Framework;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    public sealed class CoCoStateGraphValidatorTests
    {
        private CoCoGraphDescriptorCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            catalog = CoCoStateGraphTestFactory.CreateCatalog(false);
        }

        [Test]
        public void AnyErrorSuppressesCompiledGraphAndPreservesTransitionLocation()
        {
            CoCoStateId missingTarget = CoCoStateGraphTestFactory.CreateStateId(999UL);
            var transition = new CoCoTransitionSource(
                CoCoStateGraphTestFactory.FirstTransitionId,
                CoCoStateGraphTestFactory.RootStateId,
                missingTarget,
                0,
                CoCoTransitionWindow.Always,
                CoCoTransitionInterruptPolicy.RequireSourceCompletion,
                Array.Empty<CoCoConditionSource>());
            var layer = new CoCoStateLayerSource(
                CoCoStateGraphTestFactory.LayerId,
                CoCoStateGraphTestFactory.RootStateId,
                new[]
                {
                    CoCoStateGraphTestFactory.State(
                        CoCoStateGraphTestFactory.RootStateId,
                        default,
                        default,
                        1)
                },
                new[] { transition });

            CoCoStateGraphCompileResult result = Compile(
                CoCoStateGraphTestFactory.Source(layer, 3001UL));

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.HasErrors);
            Assert.IsNull(result.Graph);
            CoCoGraphDiagnostic diagnostic = RequireDiagnostic(
                result,
                CoCoDiagnosticCode.MissingTopologyElement,
                CoCoGraphField.TargetState);
            Assert.AreEqual(CoCoGraphElementKind.Transition, diagnostic.Location.ElementKind);
            Assert.AreEqual(CoCoStateGraphTestFactory.GraphId, diagnostic.Location.GraphId);
            Assert.AreEqual(CoCoStateGraphTestFactory.LayerId, diagnostic.Location.LayerId);
            Assert.AreEqual(CoCoStateGraphTestFactory.FirstTransitionId, diagnostic.Location.TransitionId);
            Assert.AreEqual(0, diagnostic.Location.LayerIndex);
            Assert.AreEqual(0, diagnostic.Location.TransitionIndex);
        }

        [Test]
        public void UnreachableStateIsWarningAndDoesNotSuppressCompiledGraph()
        {
            CoCoStateId unreachableId = CoCoStateGraphTestFactory.SecondChildStateId;
            var layer = new CoCoStateLayerSource(
                CoCoStateGraphTestFactory.LayerId,
                CoCoStateGraphTestFactory.RootStateId,
                new[]
                {
                    CoCoStateGraphTestFactory.State(
                        CoCoStateGraphTestFactory.RootStateId,
                        default,
                        default,
                        1),
                    CoCoStateGraphTestFactory.State(unreachableId, default, default, 2)
                },
                Array.Empty<CoCoTransitionSource>());

            CoCoStateGraphCompileResult result = Compile(
                CoCoStateGraphTestFactory.Source(layer, 3002UL));

            Assert.IsTrue(result.Succeeded);
            Assert.IsFalse(result.HasErrors);
            Assert.IsNotNull(result.Graph);
            CoCoGraphDiagnostic warning = RequireDiagnostic(
                result,
                CoCoDiagnosticCode.UnreachableState,
                CoCoGraphField.None);
            Assert.IsTrue(warning.Diagnostic.IsWarning);
            Assert.IsFalse(warning.IsError);
            Assert.AreEqual(CoCoGraphElementKind.State, warning.Location.ElementKind);
            Assert.AreEqual(CoCoStateGraphTestFactory.GraphId, warning.Location.GraphId);
            Assert.AreEqual(unreachableId, warning.Location.StateId);
            Assert.AreEqual(1, warning.Location.StateIndex);
        }

        [Test]
        public void ParentHierarchyCycleIsRejectedAtParentField()
        {
            CoCoStateId first = CoCoStateGraphTestFactory.RootStateId;
            CoCoStateId second = CoCoStateGraphTestFactory.FirstChildStateId;
            var layer = new CoCoStateLayerSource(
                CoCoStateGraphTestFactory.LayerId,
                first,
                new[]
                {
                    CoCoStateGraphTestFactory.State(first, second, second, 1),
                    CoCoStateGraphTestFactory.State(second, first, first, 2)
                },
                Array.Empty<CoCoTransitionSource>());

            CoCoStateGraphCompileResult result = Compile(
                CoCoStateGraphTestFactory.Source(layer, 3003UL));

            Assert.IsNull(result.Graph);
            Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
                diagnostic.Diagnostic.Code == CoCoDiagnosticCode.ParentStateCycle &&
                diagnostic.Location.ElementKind == CoCoGraphElementKind.State &&
                diagnostic.Location.Field == CoCoGraphField.ParentState));
        }

        [Test]
        public void CompositeWithoutDirectInitialChildIsRejectedAtInitialChildField()
        {
            var layer = new CoCoStateLayerSource(
                CoCoStateGraphTestFactory.LayerId,
                CoCoStateGraphTestFactory.RootStateId,
                new[]
                {
                    CoCoStateGraphTestFactory.State(
                        CoCoStateGraphTestFactory.RootStateId,
                        default,
                        default,
                        1),
                    CoCoStateGraphTestFactory.State(
                        CoCoStateGraphTestFactory.FirstChildStateId,
                        CoCoStateGraphTestFactory.RootStateId,
                        default,
                        2)
                },
                Array.Empty<CoCoTransitionSource>());

            CoCoStateGraphCompileResult result = Compile(
                CoCoStateGraphTestFactory.Source(layer, 3004UL));

            Assert.IsNull(result.Graph);
            CoCoGraphDiagnostic diagnostic = RequireDiagnostic(
                result,
                CoCoDiagnosticCode.InvalidInitialState,
                CoCoGraphField.InitialChildState);
            Assert.AreEqual(CoCoStateGraphTestFactory.RootStateId, diagnostic.Location.StateId);
        }

        [Test]
        public void CrossLayerTransitionIsRejectedAtEndpointField()
        {
            CoCoLayerId secondLayerId = CoCoStateGraphTestFactory.CreateLayerId(2UL);
            CoCoStateId secondLayerStateId = CoCoStateGraphTestFactory.CreateStateId(40UL);
            var crossLayerTransition = new CoCoTransitionSource(
                CoCoStateGraphTestFactory.FirstTransitionId,
                CoCoStateGraphTestFactory.RootStateId,
                secondLayerStateId,
                0,
                CoCoTransitionWindow.Always,
                CoCoTransitionInterruptPolicy.RequireSourceCompletion,
                Array.Empty<CoCoConditionSource>());
            var firstLayer = new CoCoStateLayerSource(
                CoCoStateGraphTestFactory.LayerId,
                CoCoStateGraphTestFactory.RootStateId,
                new[]
                {
                    CoCoStateGraphTestFactory.State(
                        CoCoStateGraphTestFactory.RootStateId,
                        default,
                        default,
                        1)
                },
                new[] { crossLayerTransition });
            var secondLayer = new CoCoStateLayerSource(
                secondLayerId,
                secondLayerStateId,
                new[]
                {
                    CoCoStateGraphTestFactory.State(secondLayerStateId, default, default, 2)
                },
                Array.Empty<CoCoTransitionSource>());
            var source = new CoCoStateGraphSource(
                CoCoStateGraphCompiler.CurrentSchemaVersion,
                3005UL,
                CoCoStateGraphTestFactory.GraphId,
                new[] { firstLayer, secondLayer },
                Array.Empty<CoCoEventToIntentDeclarationSource>());

            CoCoStateGraphCompileResult result = Compile(source);

            Assert.IsNull(result.Graph);
            CoCoGraphDiagnostic diagnostic = RequireDiagnostic(
                result,
                CoCoDiagnosticCode.CrossLayerReference,
                CoCoGraphField.TargetState);
            Assert.AreEqual(CoCoGraphElementKind.Transition, diagnostic.Location.ElementKind);
            Assert.AreEqual(0, diagnostic.Location.LayerIndex);
        }

        [Test]
        public void DuplicateStateIdIsRejectedAtSecondStateIdentity()
        {
            var duplicate = CoCoStateGraphTestFactory.State(
                CoCoStateGraphTestFactory.RootStateId,
                default,
                default,
                2);
            var layer = new CoCoStateLayerSource(
                CoCoStateGraphTestFactory.LayerId,
                CoCoStateGraphTestFactory.RootStateId,
                new[]
                {
                    CoCoStateGraphTestFactory.State(
                        CoCoStateGraphTestFactory.RootStateId,
                        default,
                        default,
                        1),
                    duplicate
                },
                Array.Empty<CoCoTransitionSource>());

            CoCoStateGraphCompileResult result = Compile(
                CoCoStateGraphTestFactory.Source(layer, 3006UL));

            Assert.IsNull(result.Graph);
            CoCoGraphDiagnostic diagnostic = RequireDiagnostic(
                result,
                CoCoDiagnosticCode.DuplicateIdentifier,
                CoCoGraphField.Identifier);
            Assert.AreEqual(CoCoGraphElementKind.State, diagnostic.Location.ElementKind);
            Assert.AreEqual(1, diagnostic.Location.StateIndex);
        }

        [Test]
        public void InvalidGraphHeaderFieldsAreAllLocatedAndSuppressOutput()
        {
            CoCoStateGraphSource valid = CoCoStateGraphTestFactory.CreateTerminalSource();
            var source = new CoCoStateGraphSource(
                CoCoStateGraphCompiler.CurrentSchemaVersion + 1U,
                0UL,
                default,
                valid.Layers,
                Array.Empty<CoCoEventToIntentDeclarationSource>());

            CoCoStateGraphCompileResult result = Compile(source);

            Assert.IsNull(result.Graph);
            Assert.AreEqual(0UL, result.ContentFingerprint);
            Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
                diagnostic.Diagnostic.Code == CoCoDiagnosticCode.UnsupportedSchemaVersion &&
                diagnostic.Location.Field == CoCoGraphField.SchemaVersion));
            Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
                diagnostic.Location.Field == CoCoGraphField.ContentFingerprint));
            Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
                diagnostic.Diagnostic.Code == CoCoDiagnosticCode.InvalidIdentifier &&
                diagnostic.Location.Field == CoCoGraphField.Identifier));
        }

        [Test]
        public void DescriptorConfigTypeMismatchIsRejectedAtConfigField()
        {
            var invalidState = new CoCoStateSource(
                CoCoStateGraphTestFactory.RootStateId,
                default,
                default,
                CoCoStateGraphTestFactory.StateDescriptorId,
                CoCoStateGraphTestFactory.ConditionConfig(5));
            var layer = new CoCoStateLayerSource(
                CoCoStateGraphTestFactory.LayerId,
                invalidState.StateId,
                new[] { invalidState },
                Array.Empty<CoCoTransitionSource>());

            CoCoStateGraphCompileResult result = Compile(
                CoCoStateGraphTestFactory.Source(layer, 3007UL));

            Assert.IsNull(result.Graph);
            CoCoGraphDiagnostic diagnostic = RequireDiagnostic(
                result,
                CoCoDiagnosticCode.DescriptorTypeMismatch,
                CoCoGraphField.Config);
            Assert.AreEqual(CoCoGraphElementKind.State, diagnostic.Location.ElementKind);
        }

        [Test]
        public void InvalidTransitionWindowAndInterruptPolicyAreBothReported()
        {
            var transition = new CoCoTransitionSource(
                CoCoStateGraphTestFactory.FirstTransitionId,
                CoCoStateGraphTestFactory.RootStateId,
                CoCoStateGraphTestFactory.RootStateId,
                0,
                default,
                CoCoTransitionInterruptPolicy.None,
                Array.Empty<CoCoConditionSource>());
            var layer = new CoCoStateLayerSource(
                CoCoStateGraphTestFactory.LayerId,
                CoCoStateGraphTestFactory.RootStateId,
                new[]
                {
                    CoCoStateGraphTestFactory.State(
                        CoCoStateGraphTestFactory.RootStateId,
                        default,
                        default,
                        1)
                },
                new[] { transition });

            CoCoStateGraphCompileResult result = Compile(
                CoCoStateGraphTestFactory.Source(layer, 3008UL));

            Assert.IsNull(result.Graph);
            RequireDiagnostic(
                result,
                CoCoDiagnosticCode.InvalidTransitionWindow,
                CoCoGraphField.Window);
            RequireDiagnostic(
                result,
                CoCoDiagnosticCode.InvalidInterruptPolicy,
                CoCoGraphField.InterruptPolicy);
        }

        [TestCase(CoCoTransitionWindowMode.LocalSeconds, 0d, 1d, true)]
        [TestCase(CoCoTransitionWindowMode.LocalSeconds, 0d, double.MaxValue, true)]
        [TestCase(CoCoTransitionWindowMode.LocalSeconds, -1d, 1d, false)]
        [TestCase(CoCoTransitionWindowMode.LocalSeconds, 1d, 1d, false)]
        [TestCase(CoCoTransitionWindowMode.LocalSeconds, double.NaN, 1d, false)]
        [TestCase(CoCoTransitionWindowMode.LocalSeconds, 0d, double.PositiveInfinity, false)]
        [TestCase(CoCoTransitionWindowMode.LocalSeconds, double.NegativeInfinity, 1d, false)]
        [TestCase(CoCoTransitionWindowMode.Normalized, 0d, 1d, true)]
        [TestCase(CoCoTransitionWindowMode.Normalized, 0.25d, 0.75d, true)]
        [TestCase(CoCoTransitionWindowMode.Normalized, 0d, 1.000001d, false)]
        [TestCase(CoCoTransitionWindowMode.Normalized, 0.5d, 0.5d, false)]
        [TestCase(CoCoTransitionWindowMode.None, 0d, 1d, false)]
        public void TransitionWindowCreationEnforcesTheFrozenIntervalContract(
            CoCoTransitionWindowMode mode,
            double startInclusive,
            double endExclusive,
            bool expectedValid)
        {
            bool created = CoCoTransitionWindow.TryCreate(
                mode,
                startInclusive,
                endExclusive,
                out CoCoTransitionWindow window);

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
            Assert.IsTrue(CoCoTransitionWindow.TryCreate(
                CoCoTransitionWindowMode.Always,
                double.NaN,
                double.PositiveInfinity,
                out CoCoTransitionWindow window));
            Assert.AreEqual(CoCoTransitionWindow.Always, window);
            Assert.AreEqual(0d, window.StartInclusive);
            Assert.AreEqual(0d, window.EndExclusive);
        }

        [TestCase(
            CoCoTransitionWindowMode.Always,
            CoCoTransitionInterruptPolicy.RequireSourceCompletion,
            true)]
        [TestCase(
            CoCoTransitionWindowMode.LocalSeconds,
            CoCoTransitionInterruptPolicy.RequireSourceCompletion,
            false)]
        [TestCase(
            CoCoTransitionWindowMode.Normalized,
            CoCoTransitionInterruptPolicy.RequireSourceCompletion,
            false)]
        [TestCase(
            CoCoTransitionWindowMode.Always,
            CoCoTransitionInterruptPolicy.AllowDuringSourceActivation,
            true)]
        [TestCase(
            CoCoTransitionWindowMode.LocalSeconds,
            CoCoTransitionInterruptPolicy.AllowDuringSourceActivation,
            true)]
        [TestCase(
            CoCoTransitionWindowMode.Normalized,
            CoCoTransitionInterruptPolicy.AllowDuringSourceActivation,
            true)]
        public void CompilerEnforcesTheWindowInterruptMatrix(
            CoCoTransitionWindowMode mode,
            CoCoTransitionInterruptPolicy policy,
            bool expectedSuccess)
        {
            CoCoTransitionWindow window;
            if (mode == CoCoTransitionWindowMode.Always)
            {
                window = CoCoTransitionWindow.Always;
            }
            else
            {
                Assert.IsTrue(CoCoTransitionWindow.TryCreate(
                    mode,
                    0d,
                    mode == CoCoTransitionWindowMode.Normalized ? 1d : double.MaxValue,
                    out window));
            }

            CoCoStateGraphCompileResult result = CompileSingleSelfLoop(window, policy);

            Assert.AreEqual(expectedSuccess, result.Succeeded);
            Assert.AreEqual(expectedSuccess, result.Graph != null);
            Assert.AreEqual(
                expectedSuccess ? 0 : 1,
                result.Diagnostics.Count(diagnostic =>
                    diagnostic.Diagnostic.Code == CoCoDiagnosticCode.InvalidInterruptPolicy));
            Assert.IsFalse(result.Diagnostics.Any(diagnostic =>
                diagnostic.Diagnostic.Code == CoCoDiagnosticCode.InvalidTransitionWindow));
        }

        [Test]
        public void ValidatorReturnsTheCompilerDiagnosticSetWithoutBuildingASeparateContract()
        {
            CoCoStateGraphSource source = new CoCoStateGraphSource(
                CoCoStateGraphCompiler.CurrentSchemaVersion,
                4001UL,
                CoCoStateGraphTestFactory.GraphId,
                Array.Empty<CoCoStateLayerSource>(),
                Array.Empty<CoCoEventToIntentDeclarationSource>());

            CoCoStateGraphCompileResult compileResult = Compile(source);
            var validator = new CoCoStateGraphValidator();

            CollectionAssert.AreEqual(compileResult.Diagnostics, validator.Validate(source, catalog));
        }

        [Test]
        public void MultiErrorDiagnosticOrderIsDeterministicAcrossRepeatedCompile()
        {
            var source = new CoCoStateGraphSource(
                CoCoStateGraphCompiler.CurrentSchemaVersion + 1U,
                0UL,
                default,
                System.Array.Empty<CoCoStateLayerSource>(),
                Array.Empty<CoCoEventToIntentDeclarationSource>());

            CoCoStateGraphCompileResult first = new CoCoStateGraphCompiler().Compile(source, catalog);
            CoCoStateGraphCompileResult second = new CoCoStateGraphCompiler().Compile(source, catalog);

            Assert.Greater(first.Diagnostics.Count, 1);
            CollectionAssert.AreEqual(first.Diagnostics, second.Diagnostics);
        }

        private CoCoStateGraphCompileResult Compile(CoCoStateGraphSource source) =>
            new CoCoStateGraphCompiler().Compile(source, catalog);

        private CoCoStateGraphCompileResult CompileSingleSelfLoop(
            CoCoTransitionWindow window,
            CoCoTransitionInterruptPolicy policy)
        {
            var transition = new CoCoTransitionSource(
                CoCoStateGraphTestFactory.FirstTransitionId,
                CoCoStateGraphTestFactory.RootStateId,
                CoCoStateGraphTestFactory.RootStateId,
                0,
                window,
                policy,
                Array.Empty<CoCoConditionSource>());
            var layer = new CoCoStateLayerSource(
                CoCoStateGraphTestFactory.LayerId,
                CoCoStateGraphTestFactory.RootStateId,
                new[]
                {
                    CoCoStateGraphTestFactory.State(
                        CoCoStateGraphTestFactory.RootStateId,
                        default,
                        default,
                        1)
                },
                new[] { transition });
            return Compile(CoCoStateGraphTestFactory.Source(layer, 3100UL));
        }

        private static CoCoGraphDiagnostic RequireDiagnostic(
            CoCoStateGraphCompileResult result,
            CoCoDiagnosticCode code,
            CoCoGraphField field)
        {
            CoCoGraphDiagnostic[] matches = result.Diagnostics.Where(diagnostic =>
                diagnostic.Diagnostic.Code == code && diagnostic.Location.Field == field).ToArray();
            Assert.AreEqual(
                1,
                matches.Length,
                $"Expected exactly one {code} diagnostic at {field}, but found {matches.Length}.");
            return matches[0];
        }
    }
}
