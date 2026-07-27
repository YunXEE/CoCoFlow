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
            string runtime = request.ProjectRoot + "/Runtime/";
            yield return new ProjectScaffoldFile(runtime + "ProjectIntent.cs", ProjectIntent);
            yield return new ProjectScaffoldFile(runtime + "ProjectContext.cs", ProjectContext);
            yield return new ProjectScaffoldFile(
                runtime + "ProjectContextSource.cs",
                ProjectContextSource);
            yield return new ProjectScaffoldFile(
                runtime + "ProjectStateLogic.cs",
                ProjectStateLogic);
            yield return new ProjectScaffoldFile(
                runtime + "ProjectOperations.cs",
                ProjectOperations);
            yield return new ProjectScaffoldFile(
                runtime + "ProjectPersistence.cs",
                ProjectPersistence);
            if (generateProvider)
            {
                yield return new ProjectScaffoldFile(
                    runtime + "ProjectStateGraphBindings.cs",
                    ProjectStateGraphBindings);
            }

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

        internal static string ExistingProviderGuidance(string providerPath) =>
            "One existing project binding provider was found at " + providerPath +
            ". ProjectStateGraphBindings.cs will not be generated.\n\n" +
            "Catalog integration:\n" +
            "builder.TryRegisterIntent<ProjectPlayerIntent, " +
            "ProjectPlayerIntentReducer, ProjectPlayerIntentReducerFactory>(\n" +
            "    projectPlayerIntentId, 1,\n" +
            "    new CoCoIntentReducerFactoryToken<ProjectPlayerIntent, " +
            "ProjectPlayerIntentReducer, ProjectPlayerIntentReducerFactory>(fingerprint),\n" +
            "    out diagnostic);\n\n" +
            "TryConfigure source integration:\n" +
            "builder.TryRegisterIntent<ProjectPlayerIntent, " +
            "ProjectPlayerIntentReducer, ProjectPlayerIntentReducerFactory>(\n" +
            "    projectPlayerIntentId, new ProjectPlayerIntentReducerFactory(), " +
            "fingerprint, out var handle, out diagnostic);\n" +
            "builder.TryBeginIntentBindings(out diagnostic);\n" +
            "CoCoIntentSourceRequirement<ProjectPlayerIntent>.TryCreate(\n" +
            "    handle, 0, out var sourceRequirement);\n" +
            "builder.TryBindIntentSource(0, sourceRequirement, out diagnostic);\n\n" +
            "Register the project-owned Operation Section in this same provider " +
            "with builder.TryRegisterOperation(...). Do not install a second provider.";

        private const string ProjectIntent = @"using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoCoFlowProject
{
    public enum ProjectPlayerCommand : byte
    {
        Interact = 1,
        Submit = 2,
        Cancel = 3
    }

    public struct ProjectPlayerIntent
    {
        public Vector2 Move;
        public InputCommandBatch<ProjectPlayerCommand> Commands;
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

    [DisallowMultipleComponent]
    public sealed class ProjectPlayerIntentSource :
        MonoBehaviour,
        ICoCoIntentFrameSource<ProjectPlayerIntent>
    {
        [SerializeField] private InputRuntime inputRuntime;
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

        private void Awake()
        {
            _commands = new InputCommandQueue<ProjectPlayerCommand>(
                Mathf.Max(1, queueCapacity));
        }

        private void OnEnable()
        {
            if (inputRuntime == null)
            {
                return;
            }

            inputRuntime.ActionChanged += OnActionChanged;
            inputRuntime.InputFenced += Fence;
            Fence();
            _wasAccepting = IsAccepting();
        }

        private void Update()
        {
            SynchronizeAcceptance();
        }

        private void OnDisable()
        {
            if (inputRuntime != null)
            {
                inputRuntime.ActionChanged -= OnActionChanged;
                inputRuntime.InputFenced -= Fence;
            }

            Fence();
        }

        public bool TrySample(
            in CoCoTickFrame tickFrame,
            out ProjectPlayerIntent intent)
        {
            if (!SynchronizeAcceptance() || inputRuntime == null)
            {
                Fence();
                intent = default;
                return false;
            }

            Vector2 move = Vector2.zero;
            if (_continuousArmed)
            {
                inputRuntime.TryReadValue(moveAction, out move);
            }

            InputCommandBatch<ProjectPlayerCommand> batch = default;
            _commands.DrainTo(ref batch);
            intent = new ProjectPlayerIntent
            {
                Move = move,
                Commands = batch
            };
            return move.sqrMagnitude > 0f || batch.Count > 0;
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
                   inputRuntime != null &&
                   inputRuntime.TryResolveAction(reference, out InputAction action) &&
                   action.id == candidate.id;
        }

        private bool IsAccepting() =>
            isActiveAndEnabled &&
            host != null &&
            host.Lifecycle == CoCoRuntimeLifecycleState.Running &&
            host.TemporalState.Mode != CoCoTemporalMode.Previewing;

        private bool SynchronizeAcceptance()
        {
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

        private const string ProjectStateLogic = @"namespace CoCoFlowProject
{
    public static class ProjectStateLogic
    {
        public static bool HasMovement(in ProjectPlayerIntent intent) =>
            intent.Move.sqrMagnitude > 0f;
    }
}
";

        private const string ProjectOperations = @"using UnityEngine;

namespace CoCoFlowProject
{
    [DisallowMultipleComponent]
    public sealed class ProjectOperations : MonoBehaviour
    {
        public void Apply(in ProjectContext context)
        {
            // Add project-owned world mutations here, then register the
            // corresponding Operation Section in the project binding provider.
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

        private const string ProjectStateGraphBindings = @"using System;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlowProject
{
    public sealed class ProjectStateGraphBindingProvider :
        ICoCoStateGraphProjectBindingProvider
    {
        public const ulong PlayerIntentReducerFingerprint = 1UL;
        private static readonly ProjectStateGraphBindingProvider Instance =
            new ProjectStateGraphBindingProvider();

        private ProjectStateGraphBindingProvider()
        {
            var builder = new CoCoGraphDescriptorCatalogBuilder();

            // Register the project's Intent, State, Context, and Operation
            // descriptors here before freezing this catalog.
            if (!builder.TryFreeze(
                    out CoCoGraphDescriptorCatalog catalog,
                    out CoCoDiagnostic diagnostic))
            {
                throw new InvalidOperationException(diagnostic.Message);
            }

            Catalog = catalog;
        }

        public CoCoGraphDescriptorCatalog Catalog { get; }

        public bool TryConfigure(
            CoCoStateGraphHostBindingBuilder builder,
            out CoCoDiagnostic diagnostic)
        {
            // Bind the descriptors registered above. A typical Input binding
            // registers ProjectPlayerIntentReducerFactory, begins Intent
            // bindings, then binds host Intent Source slot 0.
            diagnostic = CoCoDiagnostic.None;
            return true;
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

        private const string CustomAssemblyDefinition = @"{
    ""name"": ""CoCoFlowProject.Runtime"",
    ""rootNamespace"": ""CoCoFlowProject"",
    ""references"": [
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
