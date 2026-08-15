#if UNITY_EDITOR
using System.Collections.Generic;

namespace CoCoFlow.Editor.ProjectScaffold
{
    internal static class ProjectScaffoldTemplates
    {
        internal static IEnumerable<ProjectScaffoldFile> Create(
            ProjectScaffoldRequest request,
            bool generateProvider)
        {
            string graph = request.ProjectRoot + "/Graph/";
            string runtime = request.ProjectRoot + "/Runtime/";
            yield return new ProjectScaffoldFile(
                graph + "ProjectContractIds.cs",
                ProjectContractIds);
            yield return new ProjectScaffoldFile(
                graph + "ProjectIntent.cs",
                ProjectIntent);
            yield return new ProjectScaffoldFile(
                graph + "ProjectStateLogic.cs",
                ProjectStateLogic);
            yield return new ProjectScaffoldFile(
                graph + "ProjectOperationContracts.cs",
                ProjectOperationContracts);
            yield return new ProjectScaffoldFile(
                graph + "CoCoFlowProject.Graph.asmdef",
                GraphAssemblyDefinition);
            yield return new ProjectScaffoldFile(
                runtime + "ProjectPlayerIntentSource.cs",
                ProjectPlayerIntentSource);
            yield return new ProjectScaffoldFile(runtime + "ProjectContext.cs", ProjectContext);
            yield return new ProjectScaffoldFile(
                runtime + "ProjectContextSource.cs",
                ProjectContextSource);
            yield return new ProjectScaffoldFile(
                runtime + "ProjectOperations.cs",
                ProjectOperations);
            yield return new ProjectScaffoldFile(
                runtime + "ProjectPersistence.cs",
                ProjectPersistence);
            yield return new ProjectScaffoldFile(
                runtime + "ProjectStateGraphBindings.cs",
                CreateProjectStateGraphBindings(generateProvider));

            yield return new ProjectScaffoldFile(
                runtime + "ProjectInputBindingOverrideStore.cs",
                ProjectInputBindingOverrideStore);

            if (request.AssemblyMode ==
                ProjectScaffoldAssemblyMode.CustomAssemblyDefinition)
            {
                yield return new ProjectScaffoldFile(
                    request.ProjectRoot + "/CoCoFlowProject.Runtime.asmdef",
                    CustomAssemblyDefinition);
            }
        }

        internal static string GeneratedProviderGuidance() =>
            "The generated ProjectStateGraphBindingProvider installs the " +
            "runtime Catalog and bindings.\n\n" +
            SceneIntegrationGuidance;

        internal static string ExistingProviderGuidance(string providerPath) =>
            "One existing project binding provider was found at " + providerPath +
            ". ProjectStateGraphBindings.cs will contain only the reusable " +
            "binding module; it will not install a second provider.\n\n" +
            "Catalog construction, before Freeze:\n" +
            "ProjectStateGraphBindings.TryRegisterCatalog(builder, out diagnostic);\n\n" +
            "TryConfigure declaration phase, before TryBeginIntentBindings:\n" +
            "ProjectStateGraphBindings.TryRegisterRuntimeDeclarations(\n" +
            "    builder, out var projectBindings, out diagnostic);\n\n" +
            "After every project Intent declaration is registered:\n" +
            "builder.TryBeginIntentBindings(out diagnostic);\n" +
            "ProjectStateGraphBindings.TryBindRuntime(\n" +
            "    builder, projectBindings, out diagnostic);\n\n" +
            SceneIntegrationGuidance +
            " Do not install a second provider.";

        private const string SceneIntegrationGuidance =
            "Scene setup:\n" +
            "1. Assign ProjectPlayerIntentSource to Host Intent Source slot 0.\n" +
            "2. Assign ProjectOperations to Host Operators.";

        private const string ProjectIntent = @"using CoCoFlow.Runtime.Core;

namespace CoCoFlowProject
{
    public readonly struct ProjectMoveValue
    {
        public ProjectMoveValue(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float X { get; }
        public float Y { get; }
        public bool HasValue => X != 0f || Y != 0f;
    }

    public enum ProjectPlayerCommand : byte
    {
        Interact = 1,
        Submit = 2,
        Cancel = 3
    }

    public enum ProjectPlayerCommandPhase : byte
    {
        Performed = 1,
        Canceled = 2
    }

    public readonly struct ProjectPlayerCommandEvent
    {
        public ProjectPlayerCommandEvent(
            ProjectPlayerCommand command,
            ProjectPlayerCommandPhase phase,
            ulong sequence)
        {
            Command = command;
            Phase = phase;
            Sequence = sequence;
        }

        public ProjectPlayerCommand Command { get; }
        public ProjectPlayerCommandPhase Phase { get; }
        public ulong Sequence { get; }
        public bool IsValid =>
            Sequence != 0UL &&
            (Phase == ProjectPlayerCommandPhase.Performed ||
             Phase == ProjectPlayerCommandPhase.Canceled);
    }

    public struct ProjectPlayerCommandBatch
    {
        public const int Capacity = 8;

        private ProjectPlayerCommandEvent _item0;
        private ProjectPlayerCommandEvent _item1;
        private ProjectPlayerCommandEvent _item2;
        private ProjectPlayerCommandEvent _item3;
        private ProjectPlayerCommandEvent _item4;
        private ProjectPlayerCommandEvent _item5;
        private ProjectPlayerCommandEvent _item6;
        private ProjectPlayerCommandEvent _item7;
        private int _count;

        public int Count => _count;

        public bool TryAdd(in ProjectPlayerCommandEvent commandEvent)
        {
            if (_count >= Capacity || !commandEvent.IsValid)
            {
                return false;
            }

            switch (_count)
            {
                case 0: _item0 = commandEvent; break;
                case 1: _item1 = commandEvent; break;
                case 2: _item2 = commandEvent; break;
                case 3: _item3 = commandEvent; break;
                case 4: _item4 = commandEvent; break;
                case 5: _item5 = commandEvent; break;
                case 6: _item6 = commandEvent; break;
                case 7: _item7 = commandEvent; break;
                default: return false;
            }

            _count++;
            return true;
        }

        public bool TryGet(
            int index,
            out ProjectPlayerCommandEvent commandEvent)
        {
            if (index < 0 || index >= _count)
            {
                commandEvent = default;
                return false;
            }

            switch (index)
            {
                case 0: commandEvent = _item0; return true;
                case 1: commandEvent = _item1; return true;
                case 2: commandEvent = _item2; return true;
                case 3: commandEvent = _item3; return true;
                case 4: commandEvent = _item4; return true;
                case 5: commandEvent = _item5; return true;
                case 6: commandEvent = _item6; return true;
                case 7: commandEvent = _item7; return true;
                default:
                    commandEvent = default;
                    return false;
            }
        }
    }

    public struct ProjectPlayerIntent
    {
        public ProjectMoveValue Move;
        public ProjectPlayerCommandBatch Commands;
    }

    public struct ProjectPlayerIntentReducer :
        ICoCoIntentReducer<ProjectPlayerIntent>
    {
        public ProjectPlayerIntent Reduce(
            in ProjectPlayerIntent current,
            in ProjectPlayerIntent candidate) => candidate;
    }

    public sealed class ProjectPlayerIntentReducerFactory :
        ICoCoIntentReducerFactory<
            ProjectPlayerIntent,
            ProjectPlayerIntentReducer>
    {
        public ProjectPlayerIntentReducer Create(
            CoCoGraphInstanceId graphInstanceId) =>
            new ProjectPlayerIntentReducer();
    }
}
";

        private const string ProjectPlayerIntentSource =
            @"using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoCoFlowProject
{
    [DisallowMultipleComponent]
    public sealed class ProjectPlayerIntentSource :
        MonoBehaviour,
        ICoCoIntentFrameSource<ProjectPlayerIntent>
    {
        [SerializeField] private InputReader inputReader;
        [SerializeField] private CoCoStateGraphHost host;
        [SerializeField] private InputActionReference moveAction;
        [SerializeField] private InputActionReference interactAction;
        [SerializeField] private InputActionReference submitAction;
        [SerializeField] private InputActionReference cancelAction;
        [SerializeField, Min(1)] private int queueCapacity =
            InputCommandQueue<ProjectPlayerCommand>.DefaultCapacity;

        private InputCommandQueue<ProjectPlayerCommand> _commands;
        private ulong _sequence;
        private bool _continuousArmed;
        private bool _wasAccepting;
        private ulong _observedInputAuthorityRevision;

        private void Awake()
        {
            _commands = new InputCommandQueue<ProjectPlayerCommand>(
                Mathf.Max(1, queueCapacity));
        }

        private void OnEnable()
        {
            if (inputReader == null)
            {
                return;
            }

            inputReader.ActionChanged += OnActionChanged;
            inputReader.InputFenced += Fence;
            Fence();
            _observedInputAuthorityRevision =
                host != null ? host.InputAuthorityRevision : 0UL;
            _wasAccepting = IsAccepting();
        }

        private void Update()
        {
            SynchronizeAcceptance();
        }

        private void OnDisable()
        {
            if (inputReader != null)
            {
                inputReader.ActionChanged -= OnActionChanged;
                inputReader.InputFenced -= Fence;
            }

            Fence();
        }

        public bool TrySample(
            in CoCoTickFrame tickFrame,
            out ProjectPlayerIntent intent)
        {
            if (!SynchronizeAcceptance() || inputReader == null)
            {
                Fence();
                intent = default;
                return false;
            }

            Vector2 move = Vector2.zero;
            if (_continuousArmed)
            {
                inputReader.TryReadValue(moveAction, out move);
            }

            InputCommandBatch<ProjectPlayerCommand> inputBatch = default;
            _commands.DrainTo(ref inputBatch);
            ProjectPlayerCommandBatch projectBatch = default;
            for (int index = 0; index < inputBatch.Count; index++)
            {
                if (!inputBatch.TryGet(
                        index,
                        out InputCommand<ProjectPlayerCommand> inputCommand))
                {
                    continue;
                }

                projectBatch.TryAdd(new ProjectPlayerCommandEvent(
                    inputCommand.Command,
                    inputCommand.Phase == InputCommandPhase.Canceled
                        ? ProjectPlayerCommandPhase.Canceled
                        : ProjectPlayerCommandPhase.Performed,
                    inputCommand.Sequence));
            }

            intent = new ProjectPlayerIntent
            {
                Move = new ProjectMoveValue(move.x, move.y),
                Commands = projectBatch
            };
            return move.sqrMagnitude > 0f || projectBatch.Count > 0;
        }

        private void OnActionChanged(InputActionEvent actionEvent)
        {
            if (!SynchronizeAcceptance())
            {
                Fence();
                return;
            }

            if (Matches(moveAction, actionEvent.Action))
            {
                _continuousArmed =
                    actionEvent.Phase == InputActionPhase.Performed;
                return;
            }

            if (TryMapCommand(
                    actionEvent.Action,
                    out ProjectPlayerCommand command))
            {
                _sequence++;
                _commands.TryEnqueue(new InputCommand<ProjectPlayerCommand>(
                    command,
                    actionEvent.Phase == InputActionPhase.Canceled
                        ? InputCommandPhase.Canceled
                        : InputCommandPhase.Performed,
                    _sequence));
            }
        }

        private bool TryMapCommand(
            InputAction action,
            out ProjectPlayerCommand command)
        {
            if (Matches(interactAction, action))
            {
                command = ProjectPlayerCommand.Interact;
                return true;
            }

            if (Matches(submitAction, action))
            {
                command = ProjectPlayerCommand.Submit;
                return true;
            }

            if (Matches(cancelAction, action))
            {
                command = ProjectPlayerCommand.Cancel;
                return true;
            }

            command = default;
            return false;
        }

        private bool Matches(
            InputActionReference reference,
            InputAction candidate)
        {
            return candidate != null &&
                   inputReader != null &&
                   inputReader.TryResolveAction(reference, out InputAction action) &&
                   action.id == candidate.id;
        }

        private bool IsAccepting() =>
            isActiveAndEnabled &&
            host != null &&
            host.Lifecycle == CoCoRuntimeLifecycleState.Running &&
            host.TemporalState.Mode != CoCoTemporalMode.Previewing;

        private bool SynchronizeAcceptance()
        {
            ulong revision =
                host != null ? host.InputAuthorityRevision : 0UL;
            if (revision != _observedInputAuthorityRevision)
            {
                Fence();
                _observedInputAuthorityRevision = revision;
            }

            bool accepting = IsAccepting();
            if (accepting != _wasAccepting)
            {
                Fence();
                _wasAccepting = accepting;
            }

            return accepting;
        }

        private void Fence()
        {
            _commands?.Clear();
            _continuousArmed = false;
        }
    }
}
";

        private const string ProjectContext = @"using UnityEngine;

namespace CoCoFlowProject
{
    public struct ProjectContext
    {
        public Vector3 PlayerPosition;
        public int Checkpoint;
    }
}
";

        private const string ProjectContextSource = @"using UnityEngine;

namespace CoCoFlowProject
{
    [DisallowMultipleComponent]
    public sealed class ProjectContextSource : MonoBehaviour
    {
        [SerializeField] private Transform player;
        [SerializeField] private int checkpoint;

        public ProjectContext Capture() => new ProjectContext
        {
            PlayerPosition = player != null
                ? player.position
                : Vector3.zero,
            Checkpoint = checkpoint
        };
    }
}
";

        private const string ProjectContractIds = @"using System;
using CoCoFlow.Runtime.Core;

namespace CoCoFlowProject
{
    public static class ProjectContractIds
    {
        private const ulong High = 0x434F434F50524A31UL;

        static ProjectContractIds()
        {
            if (!CoCoStateDescriptorId.TryCreate(
                    High,
                    1UL,
                    out CoCoStateDescriptorId stateDescriptorId) ||
                !CoCoIntentId.TryCreate(
                    High,
                    2UL,
                    out CoCoIntentId playerIntentId) ||
                !CoCoOperationSectionId.TryCreate(
                    High,
                    3UL,
                    out CoCoOperationSectionId moveOperationSectionId) ||
                !CoCoOperatorId.TryCreate(
                    High,
                    4UL,
                    out CoCoOperatorId moveOperatorId) ||
                !CoCoFrozenConfigFieldId.TryCreate(
                    High,
                    5UL,
                    out CoCoFrozenConfigFieldId configFieldId) ||
                !CoCoStateBlockId.TryCreate(
                    High,
                    6UL,
                    out CoCoStateBlockId graphStateBlockId) ||
                !CoCoStateSlotId.TryCreate(
                    High,
                    7UL,
                    out CoCoStateSlotId graphStateSlotId))
            {
                throw new InvalidOperationException(
                    ""Project contract IDs must be valid."");
            }

            StateDescriptorId = stateDescriptorId;
            PlayerIntentId = playerIntentId;
            MoveOperationSectionId = moveOperationSectionId;
            MoveOperatorId = moveOperatorId;
            ConfigFieldId = configFieldId;
            GraphStateBlockId = graphStateBlockId;
            GraphStateSlotId = graphStateSlotId;
        }

        public static CoCoStateDescriptorId StateDescriptorId { get; }
        public static CoCoIntentId PlayerIntentId { get; }
        public static CoCoOperationSectionId MoveOperationSectionId { get; }
        public static CoCoOperatorId MoveOperatorId { get; }
        public static CoCoFrozenConfigFieldId ConfigFieldId { get; }
        public static CoCoStateBlockId GraphStateBlockId { get; }
        public static CoCoStateSlotId GraphStateSlotId { get; }
    }
}
";

        private const string ProjectStateLogic = @"using System;
using CoCoFlow.Runtime.Core;

namespace CoCoFlowProject
{
    [Serializable]
    public sealed class ProjectStateConfig : CoCoStateConfig
    {
        public bool EmitMove = true;
    }

    public readonly struct ProjectStateConfigSchema :
        ICoCoFrozenConfigSchema
    {
    }

    public static class ProjectStateSchemas
    {
        static ProjectStateSchemas()
        {
            var builder =
                new CoCoFrozenConfigSchemaBuilder<ProjectStateConfigSchema>();
            CoCoDiagnostic fieldDiagnostic = default;
            CoCoDiagnostic freezeDiagnostic = default;
            if (!builder.TryAddField(
                    ProjectContractIds.ConfigFieldId,
                    out CoCoFrozenConfigField<ProjectStateConfigSchema, bool>
                        emitMove,
                    out fieldDiagnostic) ||
                !builder.TryFreeze(
                    out CoCoFrozenConfigSchema<ProjectStateConfigSchema> schema,
                    out freezeDiagnostic))
            {
                throw new InvalidOperationException(
                    fieldDiagnostic.IsError
                        ? fieldDiagnostic.Message
                        : freezeDiagnostic.Message);
            }

            EmitMove = emitMove;
            State = schema;
        }

        public static readonly
            CoCoFrozenConfigField<ProjectStateConfigSchema, bool> EmitMove;
        public static readonly
            CoCoFrozenConfigSchema<ProjectStateConfigSchema> State;
    }

    public sealed class ProjectStateConfigFreezer :
        ICoCoConfigFreezer<ProjectStateConfig, ProjectStateConfigSchema>
    {
        public bool TryFreeze(
            ProjectStateConfig source,
            CoCoFrozenConfigWriter<ProjectStateConfigSchema> writer,
            out CoCoDiagnostic diagnostic)
        {
            if (source == null)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.State,
                    CoCoDiagnosticCode.InvalidFrozenConfig,
                    ""ProjectStateConfig is required."");
                return false;
            }

            return writer.TryWrite(
                ProjectStateSchemas.EmitMove,
                source.EmitMove,
                out diagnostic);
        }
    }

    public sealed class ProjectStateMemory : CoCoActivationMemory
    {
        public byte Value;
    }

    public sealed class ProjectStateMemoryBinding :
        ICoCoActivationMemoryStateBinding<ProjectStateMemory, byte>
    {
        public const ulong Fingerprint = 3UL;

        public ulong SemanticFingerprint => Fingerprint;

        public bool TryCapture(
            ProjectStateMemory memory,
            out byte state,
            out CoCoDiagnostic diagnostic)
        {
            if (memory == null)
            {
                state = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Context,
                    CoCoDiagnosticCode.InvalidContextProducer,
                    ""Project State memory is required for capture."");
                return false;
            }

            state = memory.Value;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryPrepareRestore(
            in byte state,
            ProjectStateMemory candidateMemory,
            out CoCoDiagnostic diagnostic)
        {
            if (candidateMemory == null)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Context,
                    CoCoDiagnosticCode.InvalidContextProducer,
                    ""Project State candidate memory is required for restore."");
                return false;
            }

            candidateMemory.Value = state;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }
    }

    public sealed class ProjectStateLogic :
        CoCoStateLogic,
        ICoCoStateUpdate
    {
        private readonly CoCoIntentHandle<ProjectPlayerIntent> _intent;
        private readonly
            CoCoOperationSectionField<ProjectMoveValue> _moveField;
        private readonly bool _emitMove;

        public ProjectStateLogic(
            CoCoStateFactoryContext context,
            CoCoIntentHandle<ProjectPlayerIntent> intent,
            CoCoOperationSectionField<ProjectMoveValue> moveField)
        {
            _intent = intent;
            _moveField = moveField;
            _emitMove =
                context != null &&
                context.Config.TryRead(
                    ProjectStateSchemas.EmitMove,
                    out bool emitMove)
                    ? emitMove
                    : true;
        }

        public void Update(CoCoStateExecutionContext context)
        {
            if (!_emitMove ||
                !_intent.IsValid ||
                !_moveField.IsValid ||
                context.Intents == null ||
                !context.Intents.TryGet(
                    _intent,
                    out ProjectPlayerIntent intent))
            {
                return;
            }

            context.Operations.Write(_moveField, intent.Move);
        }
    }
}
";

        private const string ProjectOperationContracts = @"using System;
using CoCoFlow.Runtime.Core;

namespace CoCoFlowProject
{
    public interface IProjectMoveOperationSection :
        ICoCoOperationSection
    {
        ProjectMoveValue Move { get; }
    }

    public sealed class ProjectMoveOperationSectionView :
        IProjectMoveOperationSection
    {
        private readonly CoCoOperationSectionReader _reader;
        private readonly CoCoOperationSectionField<ProjectMoveValue> _moveField;

        public ProjectMoveOperationSectionView(
            CoCoOperationSectionReader reader,
            CoCoOperationSectionField<ProjectMoveValue> moveField)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _moveField = moveField;
        }

        public ProjectMoveValue Move => _reader.Read(_moveField);
    }

    public sealed class ProjectMoveOperationSectionViewFactory :
        ICoCoOperationSectionViewFactory<IProjectMoveOperationSection>
    {
        public CoCoOperationSectionHandle<IProjectMoveOperationSection>
            Handle { get; private set; }
        public CoCoOperationSectionField<ProjectMoveValue>
            MoveField { get; private set; }

        public IProjectMoveOperationSection Create(
            in CoCoOperationSectionViewContext<IProjectMoveOperationSection>
                context)
        {
            if (!context.IsValid ||
                !context.TryGetField(
                    0,
                    out CoCoOperationSectionField<ProjectMoveValue> moveField))
            {
                throw new InvalidOperationException(
                    ""Project Move Operation field could not be resolved."");
            }

            Handle = context.Handle;
            MoveField = moveField;
            return new ProjectMoveOperationSectionView(
                context.Reader,
                moveField);
        }
    }
}
";

        private const string ProjectOperations = @"using System;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlowProject
{
    [DisallowMultipleComponent]
    public sealed class ProjectOperations :
        MonoBehaviour,
        ICoCoOperator
    {
        private static readonly CoCoOperationSectionRequirement Requirement;
        private static readonly CoCoOperatorDescriptor OperatorDescriptor;

        static ProjectOperations()
        {
            var builder = new CoCoOperatorDescriptorBuilder();
            CoCoDiagnostic requirementDiagnostic = default;
            CoCoDiagnostic freezeDiagnostic = default;
            if (!builder.TryRequire<IProjectMoveOperationSection>(
                    ProjectContractIds.MoveOperationSectionId,
                    CoCoOperationSectionMode.Continuous,
                    out Requirement,
                    out requirementDiagnostic) ||
                !builder.TryFreeze<ProjectOperations>(
                    ProjectContractIds.MoveOperatorId,
                    out OperatorDescriptor,
                    out freezeDiagnostic))
            {
                throw new InvalidOperationException(
                    requirementDiagnostic.IsError
                        ? requirementDiagnostic.Message
                        : freezeDiagnostic.Message);
            }
        }

        public CoCoOperatorDescriptor Descriptor => OperatorDescriptor;
        public ProjectMoveValue LastMove { get; private set; }

        public bool TryExecute(
            in CoCoOperatorExecutionContext context,
            out CoCoOperatorOutcome outcome)
        {
            if (!context.TryGet(
                    Requirement,
                    out CoCoOperationSectionEntry<IProjectMoveOperationSection>
                        entry))
            {
                outcome = CoCoOperatorOutcome.Rejected(
                    CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Operator,
                        CoCoDiagnosticCode.OperatorExecutionFailed,
                        ""Project Move Operation Section is unavailable.""));
                return false;
            }

            LastMove = entry.View.Move;
            outcome = CoCoOperatorOutcome.Success;
            return true;
        }
    }
}
";

        private const string ProjectPersistence = @"namespace CoCoFlowProject
{
    public static class ProjectPersistence
    {
        public const string SchemaId = ""project.context"";
        public const uint SchemaVersion = 1;
    }
}
";

        private const string ProjectStateGraphBindingsModule = @"using System;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlowProject
{
    public sealed class ProjectStateGraphRuntimeBindings
    {
        internal ProjectStateGraphRuntimeBindings(
            CoCoIntentHandle<ProjectPlayerIntent> intent,
            ProjectMoveOperationSectionViewFactory operationFactory)
        {
            Intent = intent;
            OperationFactory = operationFactory;
        }

        public CoCoIntentHandle<ProjectPlayerIntent> Intent { get; }
        public ProjectMoveOperationSectionViewFactory OperationFactory { get; }
    }

    public static class ProjectStateGraphBindings
    {
        public const ulong PlayerIntentReducerFingerprint = 1UL;
        public const ulong MoveOperationFactoryFingerprint = 2UL;
        public const ulong GraphStateDefaultFingerprint = 4UL;

        public static bool TryRegisterCatalog(
            CoCoGraphDescriptorCatalogBuilder builder,
            out CoCoDiagnostic diagnostic)
        {
            if (builder == null)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Registry,
                    CoCoDiagnosticCode.MissingDescriptor,
                    ""Project catalog builder is required."");
                return false;
            }

            return
                builder.TryRegisterIntent<
                    ProjectPlayerIntent,
                    ProjectPlayerIntentReducer,
                    ProjectPlayerIntentReducerFactory>(
                    ProjectContractIds.PlayerIntentId,
                    1,
                    new CoCoIntentReducerFactoryToken<
                        ProjectPlayerIntent,
                        ProjectPlayerIntentReducer,
                        ProjectPlayerIntentReducerFactory>(
                        PlayerIntentReducerFingerprint),
                    out diagnostic) &&
                builder.TryRegisterOperationSection<
                    IProjectMoveOperationSection,
                    ProjectMoveOperationSectionViewFactory>(
                    ProjectContractIds.MoveOperationSectionId,
                    CoCoOperationSectionMode.Continuous,
                    new CoCoOperationSectionViewFactoryToken<
                        IProjectMoveOperationSection,
                        ProjectMoveOperationSectionViewFactory>(
                        MoveOperationFactoryFingerprint),
                    out diagnostic) &&
                builder.TryRegisterStateBlock(
                    ProjectContractIds.GraphStateBlockId,
                    CoCoStateBlockOwner.Graph,
                    out diagnostic) &&
                builder.TryRegisterStateSlot(
                    ProjectContractIds.GraphStateBlockId,
                    ProjectContractIds.GraphStateSlotId,
                    CoCoContextProjection.Temporal,
                    CoCoContextRestorePolicy.Stored,
                    default(CoCoGraphStateRecord<byte>),
                    GraphStateDefaultFingerprint,
                    default,
                    null,
                    out diagnostic) &&
                builder.TryRegisterState<
                    ProjectStateLogic,
                    ProjectStateConfig,
                    ProjectStateConfigSchema,
                    ProjectStateMemory>(
                    ProjectContractIds.StateDescriptorId,
                    1U,
                    new ProjectStateConfigFreezer(),
                    new CoCoStateRuntimeRegistration<
                        ProjectStateLogic,
                        ProjectStateConfigSchema,
                        ProjectStateMemory>(ProjectStateSchemas.State),
                    new[] { ProjectContractIds.PlayerIntentId },
                    new[] { ProjectContractIds.MoveOperationSectionId },
                    new[] { ProjectContractIds.GraphStateBlockId },
                    out diagnostic);
        }

        public static CoCoGraphDescriptorCatalog CreateCatalog()
        {
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            if (!TryRegisterCatalog(builder, out CoCoDiagnostic diagnostic) ||
                !builder.TryFreeze(
                    out CoCoGraphDescriptorCatalog catalog,
                    out diagnostic))
            {
                throw new InvalidOperationException(diagnostic.Message);
            }

            return catalog;
        }

        public static bool TryRegisterRuntimeDeclarations(
            CoCoStateGraphHostBindingBuilder builder,
            out ProjectStateGraphRuntimeBindings bindings,
            out CoCoDiagnostic diagnostic)
        {
            bindings = null;
            var operationFactory =
                new ProjectMoveOperationSectionViewFactory();
            if (!builder.TryRegisterIntent<
                    ProjectPlayerIntent,
                    ProjectPlayerIntentReducer,
                    ProjectPlayerIntentReducerFactory>(
                    ProjectContractIds.PlayerIntentId,
                    new ProjectPlayerIntentReducerFactory(),
                    PlayerIntentReducerFingerprint,
                    out CoCoIntentHandle<ProjectPlayerIntent> intent,
                    out diagnostic) ||
                !builder.TryRegisterOperation(
                    ProjectContractIds.MoveOperationSectionId,
                    CoCoOperationSectionMode.Continuous,
                    operationFactory,
                    MoveOperationFactoryFingerprint,
                    out CoCoOperationSectionRequirement ignored,
                    out diagnostic))
            {
                return false;
            }

            bindings = new ProjectStateGraphRuntimeBindings(
                intent,
                operationFactory);
            return true;
        }

        public static bool TryBindRuntime(
            CoCoStateGraphHostBindingBuilder builder,
            ProjectStateGraphRuntimeBindings bindings,
            out CoCoDiagnostic diagnostic)
        {
            if (bindings == null ||
                !CoCoIntentSourceRequirement<ProjectPlayerIntent>.TryCreate(
                    bindings.Intent,
                    1,
                    out CoCoIntentSourceRequirement<ProjectPlayerIntent>
                        sourceRequirement))
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Registry,
                    CoCoDiagnosticCode.InvalidIntentDescriptor,
                    ""Project runtime bindings are incomplete."");
                return false;
            }

            if (!builder.TryBindIntentSource(
                    0,
                    sourceRequirement,
                    out diagnostic))
            {
                return false;
            }

            if (!TryFindSingleProjectState(
                    builder.Graph,
                    out CoCoLayerId layerId,
                    out CoCoStateId stateId) ||
                !CoCoActivationId.TryCreate(
                    1UL,
                    out CoCoActivationId activationId) ||
                !CoCoGraphStateRecord<byte>.TryCreate(
                    layerId,
                    stateId,
                    true,
                    activationId,
                    0d,
                    0d,
                    true,
                    0UL,
                    0,
                    out CoCoGraphStateRecord<byte> defaultState))
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Registry,
                    CoCoDiagnosticCode.InvalidContextProducer,
                    ""The starter binding requires exactly one Project State in the Graph."");
                return false;
            }

            if (!builder.TryBindGraphStateSlot<
                    ProjectStateMemory,
                    byte,
                    ProjectStateMemoryBinding>(
                    layerId,
                    stateId,
                    ProjectContractIds.GraphStateBlockId,
                    ProjectContractIds.GraphStateSlotId,
                    defaultState,
                    GraphStateDefaultFingerprint,
                    new ProjectStateMemoryBinding(),
                    out diagnostic))
            {
                return false;
            }

            var stateFactory =
                new CoCoStateRuntimeFactory<
                    ProjectStateLogic,
                    ProjectStateMemory>(
                    context => new ProjectStateLogic(
                        context,
                        bindings.Intent,
                        bindings.OperationFactory.MoveField),
                    () => new ProjectStateMemory(),
                    (source, destination) =>
                        destination.Value = source.Value,
                    memory => memory.Value = 0,
                    memory => memory.Value);
            return builder.TryBindState(
                ProjectContractIds.StateDescriptorId,
                stateFactory,
                out diagnostic);
        }

        private static bool TryFindSingleProjectState(
            CoCoCompiledStateGraph graph,
            out CoCoLayerId layerId,
            out CoCoStateId stateId)
        {
            layerId = default;
            stateId = default;
            if (graph == null)
            {
                return false;
            }

            int count = 0;
            for (int layerIndex = 0;
                 layerIndex < graph.Layers.Count;
                 layerIndex++)
            {
                CoCoCompiledStateLayer layer = graph.Layers[layerIndex];
                for (int stateIndex = 0;
                     stateIndex < layer.States.Count;
                     stateIndex++)
                {
                    CoCoCompiledState state = layer.States[stateIndex];
                    if (state.Descriptor.DescriptorId !=
                        ProjectContractIds.StateDescriptorId)
                    {
                        continue;
                    }

                    count++;
                    layerId = layer.LayerId;
                    stateId = state.StateId;
                }
            }

            return count == 1;
        }
    }
";

        private const string ProjectStateGraphBindingProvider = @"
    public sealed class ProjectStateGraphBindingProvider :
        ICoCoStateGraphProjectBindingProvider
    {
        private static readonly ProjectStateGraphBindingProvider Instance =
            new ProjectStateGraphBindingProvider();

        private ProjectStateGraphBindingProvider()
        {
            Catalog = ProjectStateGraphBindings.CreateCatalog();
        }

        public CoCoGraphDescriptorCatalog Catalog { get; }

        public bool TryConfigure(
            CoCoStateGraphHostBindingBuilder builder,
            out CoCoDiagnostic diagnostic)
        {
            if (!ProjectStateGraphBindings.TryRegisterRuntimeDeclarations(
                    builder,
                    out ProjectStateGraphRuntimeBindings bindings,
                    out diagnostic) ||
                !builder.TryBeginIntentBindings(out diagnostic))
            {
                return false;
            }

            return ProjectStateGraphBindings.TryBindRuntime(
                builder,
                bindings,
                out diagnostic);
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (CoCoStateGraphProjectBindings.IsInstalled)
            {
                return;
            }

            if (!CoCoStateGraphProjectBindings.TryInstall(
                    Instance,
                    out CoCoDiagnostic diagnostic))
            {
                Debug.LogError(
                    ""[ProjectStateGraphBindings] "" + diagnostic.Message);
            }
        }
    }
";

        private static string CreateProjectStateGraphBindings(
            bool generateProvider) =>
            ProjectStateGraphBindingsModule +
            (generateProvider
                ? ProjectStateGraphBindingProvider
                : string.Empty) +
            @"
}
";

        private const string ProjectInputBindingOverrideStore = @"using CoCoFlow.Runtime.Modules.Input;
using UnityEngine;

namespace CoCoFlowProject
{
    [DisallowMultipleComponent]
    public sealed class ProjectInputBindingOverrideStore :
        MonoBehaviour,
        IInputBindingOverrideStore
    {
        public bool TryLoad(string storageKey, out string overrideJson)
        {
            overrideJson = string.IsNullOrEmpty(storageKey)
                ? string.Empty
                : PlayerPrefs.GetString(storageKey, string.Empty);
            return !string.IsNullOrEmpty(storageKey);
        }

        public bool TrySave(string storageKey, string overrideJson)
        {
            if (string.IsNullOrEmpty(storageKey))
            {
                return false;
            }

            PlayerPrefs.SetString(storageKey, overrideJson ?? string.Empty);
            PlayerPrefs.Save();
            return true;
        }
    }
}
";

        private const string GraphAssemblyDefinition = @"{
    ""name"": ""CoCoFlowProject.Graph"",
    ""rootNamespace"": ""CoCoFlowProject"",
    ""references"": [
        ""CoCoFlow.Runtime.Core.Contracts"",
        ""CoCoFlow.Runtime.Core.StateFlow"",
        ""CoCoFlow.Runtime.Core.StateGraph""
    ],
    ""includePlatforms"": [],
    ""excludePlatforms"": [],
    ""allowUnsafeCode"": false,
    ""overrideReferences"": false,
    ""precompiledReferences"": [],
    ""autoReferenced"": true,
    ""defineConstraints"": [],
    ""versionDefines"": [],
    ""noEngineReferences"": true
}
";

        private const string CustomAssemblyDefinition = @"{
    ""name"": ""CoCoFlowProject.Runtime"",
    ""rootNamespace"": ""CoCoFlowProject"",
    ""references"": [
        ""CoCoFlowProject.Graph"",
        ""CoCoFlow.Runtime.Core.Contracts"",
        ""CoCoFlow.Runtime.Core.StateFlow"",
        ""CoCoFlow.Runtime.Core.StateGraph"",
        ""CoCoFlow.Runtime.Core"",
        ""CoCoFlow.Runtime.StateGraphHost"",
        ""CoCoFlow.Runtime.Modules.Input"",
        ""Unity.InputSystem""
    ],
    ""includePlatforms"": [],
    ""excludePlatforms"": [],
    ""allowUnsafeCode"": false,
    ""overrideReferences"": false,
    ""precompiledReferences"": [],
    ""autoReferenced"": true,
    ""defineConstraints"": [],
    ""versionDefines"": [],
    ""noEngineReferences"": false
}
";
    }
}
#endif
