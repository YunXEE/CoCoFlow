# Changelog

All notable changes to CoCoFlow are documented in this file.

The earlier release-candidate and pre-release lines are preserved in the history
below. The 0.4 line targets new projects and does not include an automatic
migration runtime for 0.3.9 projects.

## [0.4.1] - Unreleased

Legacy Gameplay sample exit and boundary cleanup. No new Runtime capability.

### Changed

- Rebuilt the Setup Assistant as the **CoCoFlow Utility** panel
  (`CoCoFlow > Utility Panel`, window title "CoCoFlow Utility"), migrated
  from IMGUI to UI Toolkit with the unified `ccflow` visual language, and
  split the 2035-line window into focused setup modules (module catalog,
  dependency actions, status scanner, JSON utility, version policies).
  The `CoCoFlowSetupAssistant` editor type is replaced by `CoCoFlowUtility`.
- "Apply Recommended Dependencies" now shows an explicit impact-disclosure
  confirmation before writing (manifest entries, UniTask source replacement,
  active-target support defines); cancelling writes nothing.
- Support-define and module status now focus on the active build target
  instead of enumerating every build target group. A legacy manual
  `COCOFLOW_UNITASK_SUPPORT` define on the active target is disclosed as a
  warning (versionDefines stays the single authority).
- The DOTween dependency row reports a manifest read error as an error state
  (previously the message said "Manifest error" while the row showed OK/WARN).
- The panel is bilingual (English / 简体中文) with an in-header language
  switch shared with the other ccflow editor windows.

### Removed

- The entire `Samples~/Gameplay` sample tree (Character/Enemy/Item runtime,
Editor tooling, and sample tests, 5193 lines of C#). Git history remains the
  archive; no Legacy or Archive copy is created.
- Runtime public API: `PersistenceCharacterContextAdapter`,
  `PersistenceItemContextAdapter`, and their default registrations in
  `PersistenceContextAdapterRegistry`. The registry is now a pure extension
  point: hosts that need Context capture implement
  `IPersistenceContextAdapter` and call `Register` themselves.
- Runtime internal helper `PersistenceContextReflection` (lost its only
  consumers together with the built-in adapters).
- Core interfaces `ICoCoIntent`, `ICoCoIntentSource<T>`, and
  `ICoCoContextFrameResolver` — the mutable-object intent pattern of 0.3.x.
  The official intent path is the StateFlow `IntentFrame` contract family.
- Package dependencies `com.unity.splines` and `com.unity.ai.navigation`
  (their only package consumers lived in the sample; hosts keep using them
  via their own manifests when needed).
- The `CoCoFlow.Runtime.Gameplay` prefix entries from the StateGraph authoring
  forbidden-assembly guards (runtime catalog, Editor closure validator,
  boundary tests, contracts checklist, and compiler docs), plus the matching
  diagnostic message wording and the `gameplay` package keyword. If an official
  Gameplay layer is ever reintroduced under a `CoCoFlow` prefix, these guard
  entries must be re-added explicitly.

### Tests

- Migrated eight sample tests that proved package contracts into the formal
  `Tests/Runtime` suites with fully test-owned fixtures: event envelope
  creation fields, typed event + envelope dual subscription, event sequence
  vs stable/runtime identity separation, `CoCoEntityContext` projection,
  `PersistenceContext` stable-id semantics, pending-document apply through a
  registered host adapter, container reward granting, and legacy-record apply
  on an actor with a running StateGraph host.
- The remaining 35 sample-specific tests were removed with the sample.

### Changed

- StateGraph Editor rebuilt on the unified UI Toolkit visual language
  (`ccflow-` from `Editor/Common`): the main window, canvas cards, preset
  wizard, and the asset Inspector now share one theme, with bilingual
  (English / Simplified Chinese) static chrome driven by the Editor language
  preference. Asset names, descriptor names, and diagnostic payloads are
  never translated. Editor styles load by package path instead of a hard-coded
  GUID.
- The canvas is rebuilt as a flattened genealogy view: the whole Layer is
  visible on one canvas (positions composed from per-scope EditorLayout
  records), parent-child structure is drawn as white flowchart-style lines,
  and cross-scope transitions are always visible. Cards use a two-layer
  border system: the inner border carries node state (initial/default
  markings with "<scope> Default" badges), the outer border carries dynamic
  states. Selecting a State highlights its full ancestry chain and the
  genealogy segments between them; selecting a Transition highlights only the
  edge; clicking empty canvas clears the selection. Leaf flow states are
  computed from the Layer default (orange dashed ring = reachable dead end,
  red dashed ring = topologically unreachable / default without outgoing
  edges). Composite cards show a leaf count and a "Tidy Subtree" action;
  dragging a composite moves its whole subtree with one layout record. Edges
  follow the Animator presentation (center-anchored clipping, parallel
  offsets for bidirectional pairs, midpoint direction triangles, self loops).
  A dot grid background and an explicit zoom slider were added; wheel zoom
  sensitivity was reduced. All pointer semantics (pan/zoom/drag/capture) and
  the authoring command/Undo boundary are unchanged.
- The StateGraph Asset Inspector moved from IMGUI to UI Toolkit on the same
  visual language. It keeps the reachable surface (identity/count summary,
  Event Adapter Declarations as the sole non-topology editing lane, Open
  Editor, Add Layer, Analyze with Locate, Play Mode read-only) and adds a
  read-only Host-requirements card derived from the three compilation
  manifests. Unreachable dead layer-operation code was removed rather than
  revived.
- `CoCoFlow.Editor.StateGraph` now references `CoCoFlow.Editor.Common`.

### Added

- `Tests/Editor/StateGraph/CoCoStateGraphEditorVisualLanguageTests.cs`: theme
  attachment, severity-to-badge mapping, empty states, bilingual chrome,
  Animator edge interactions (hit/miss selection, parallel bidirectional
  offsets, self-loop, double-click drill), Inspector UI Toolkit equivalents,
  and the three upgraded element-name anchors.

## [0.4.0] - 2026-08-29

Closes the expanded RC2 line as a usable 0.4.0 release. Runtime code, Editor
code, assembly definitions, and serialized assets are unchanged from RC2; this
release changes only documentation, version metadata, validation configuration,
and the Setup Assistant version assertion.

The release standard is deliberately practical: stop adding features, ship the
Runtime that already works, document its limits, and continue with small 0.4.x
iterations. This is not a claim of zero defects, complete optimization,
marketplace certification, or store readiness.

### Maturity policy

- **Mature** means stable public Runtime API, proven use in a real project, and
  accurately documented boundaries. It does not require newest architecture,
  maximum performance, or complete Editor tooling.
- **Core Engine** is mature for Contracts, StateFlow, StateGraph,
  StateGraphAuthoring Runtime, and StateGraphHost. StateGraph Editor and the
  older `Runtime/Core/*.cs` EventBus/Services/Context facilities are outside
  this declaration.
- **Camera**, **Persistence**, and **UI** are mature. They originated in 0.3.9,
  but their current Runtime APIs are stable and usable.
- UI remains efficiency-limited: panels use `Instantiate`/`Destroy`,
  `UIManager` is a singleton with one panel stack and serial transitions, and
  there is no automatic Pooling, virtualized list, or high-throughput guarantee.
- **Map** and **Pooling** are immature implementation snapshots. Their public
  APIs, configuration, and serialized structures have no compatibility
  guarantee during 0.4.x.
- Other modules receive no maturity classification in this release.

### Verification

- No Unity suite was rerun on the final documentation/version commit or tag.
- Runtime-identical RC2 evidence is inherited from
  `6fc755a01089d830e59bf6df56e0e94834a54eb5`:
  - Unity `6000.3.20f1`: EditMode `660/660`, Editor PlayMode `367/367`,
    Player `359/359`.
  - Unity `6000.5.5f1`: EditMode `660/660`, Editor PlayMode `367/367`,
    Player `359/359`.
- Final release packaging is verified statically for JSON, version consistency,
  Markdown links, contradictory maturity wording, whitespace, and a strict
  no-Runtime/no-Editor/no-asmdef/no-asset diff boundary.

## [0.4.0-rc.2] - 2026-08-29

Promotes the rc.1R Core surgery directly as the rc.2 package anchor. RC2 is
defined by the package-level Golden Path v2 rather than the previously planned
Starter gameplay slice.

### Added

- Raw input contracts and runtime sampling, standard state descriptors, and
  automatic StateGraph binding from attributed state logic.
- Package-owned Locomotion Sections, Operator, configuration, and registration,
  with movement committed through Context authority.
- Animator snapshot projection, typed authored transitions, one-stop Add State
  script creation, and typed Host inspector wiring.
- End-to-end coverage for RawInput, standard binding, Locomotion, rejected-tick
  behavior, persistence restore, and post-restore timeline continuation.

### Changed

- The consumer path is now
  `RawInput -> StateGraph -> Operation Sections -> Operators -> Context commit`;
  state scripts no longer need project-owned provider glue for the standard
  path.
- Standard binding is installed after scene load, and a rejected Locomotion
  tick stops loudly at the last committed world state until restore/restart.

### Removed

- The incomplete advanced `AnimOperator` path and its UniTask/DOTween adapters;
  `AnimAutoOperator` remains the supported Animator projection route.
- Project Scaffold and the orphaned semantic Input command queue. Graph-driven
  Add State authoring replaces generated project glue.

### Scope

- The former rc.2 Player/Enemy/Chest slice, UI V2 expansion, DOTween screen
  transitions, HUD/menu work, and `Samples~/Adventure` scene are intentionally
  outside this RC. They carry no automatic commitment to a later version.
- There is no separate rc.1 package/tag for the rc.1R implementation; it is
  promoted directly to `v0.4.0-rc.2`.

## [0.4.0-rc.0] - 2026-08-24

Anchors the start of the 0.4.0 release-candidate line. Documentation-only
change: version strings move from `0.4.0-pre.15` to `0.4.0-rc.0` and this
repository receives its first git tag (`v0.4.0-rc.0`).

Per the RC plan ruling #10, documentation-only changes skip the validation
matrix. Matrix evidence carries over from the Pre15 exit Final Head 661feb6
archive (identical code baseline; this release contains no code changes).

See Notion "0.4.0-rc 稳定化与 Golden Path" for the RC protocol: runtime core
is hard-frozen (fix-only), module core is the expected change surface, and
module-core breaking changes require ledger registration plus an rc bump.

## [0.4.0-pre.15] - 2026-08-22

Closes the Pre15 boundary-hardening line (PR15.01–PR15.10, PR #31–#43).
The 0.4 Runtime public API surface is frozen, legacy runtimes are exited,
and dependency combinations plus Player builds are verified on Unity
6000.3.20f1 / 6000.5.5f1.

### Removed

- Legacy Mono FSM runtime and its dedicated Context consumption chain;
  `StateGraph`/StateFlow is the only state runtime (PR #33, PR15.03).
- Legacy input bridge and implicit service locator; `InputReader` is the
  single input entry point (PR #35, PR15.05, BUG-004).
- Project-specific Gameplay (Character/Enemy/Item) moved out of Runtime
  into the minimal Sample boundary (PR #34, PR15.04).
- Rendering and AssetPipeline module trimming (PR #37, PR15.07).

### Changed

- Runtime API surface frozen per the API Ledger; approved deletions,
  visibility tightening, and `sealed` decisions landed (PR #32, PR15.02;
  PR #36, PR15.06).
- Dependency boundary closure for UniTask, DOTween, Addressables, and
  Cinemachine: declared dependencies, version defines, optional
  combinations, and Player edges; `Animation.UniTask` gains a direct
  `Core.Contracts` reference (PR #43, PR15.10, BUG-011).
- GitHub-hosted static hygiene CI baseline plus maintainer-local
  dual-Unity verification entry (PR #31, PR15.01).
- PR template rewritten: Chinese text, SHA-bound evidence, no private
  links.

### Fixed

- Map/Pool defects BUG-001–BUG-003 (PR #38, PR15.08).
- Map transition handoff stability (PR #40) and exactly-once terminal
  participant cleanup with `ForceShutdown` (PR #41).
- Optional-on test baseline restoration (PR #39).
- Scaffold/Localization BUG-005–BUG-008 and Unity 6000.5 compile
  blockers BUG-010 (PR #42, PR15.09).

### Verification

- `PASS`: on the PR #44 checkpoint matrix, Unity `6000.3.20f1` and
  `6000.5.5f1` persistent hosts — package-wide EditMode `689/689` x2,
  PlayMode editor mode `361/361` x2, and PlayMode player mode `356/356` x2,
  zero Failed and zero Inconclusive; every XML validated by
  `.github/ci/cocoflow_ci.py` (evidence archived under `.ci-artifacts/` for
  the checkpoint Final Head).
- Windows `6000.3` package-wide run (VR-1) pending at release time.
- `SKIPPED`: Unity Package Validation Suite remains locally waived; its
  exception scopes track `0.4.0-pre.15`.

## [0.4.0-pre.14] - 2026-07-27

### Added

- `InputRuntime` over the exact runtime `PlayerInput` Action collection,
  fixed-allocation `InputCommandQueue<TCommand>`, unmanaged eight-command
  `InputCommandBatch<TCommand>`, stable-ID transactional rebinds, prompt
  snapshots, and exact/base-layout glyph lookup.
- Official `com.unity.localization@1.5.9` core integration and localization
  diagnostics, plus optional UI V2 `UIWidgetLocalizedText` and
  `InputPromptPresenter` extensions. Smart String binding arguments and
  fallback text refresh inside the current Screen when UI V2 support is
  enabled.
- Preview-first `ProjectScaffoldWindow` with an always engine-free
  `CoCoFlowProject.Graph` assembly and an Assembly-CSharp or custom-asmdef
  Unity-facing Runtime layer. The generated starter includes a real semantic
  Intent, State, Graph-state binding, Operation Section, Operator, current Host
  Provider wiring, and explicit scene integration checklist.
- Same-directory validated atomic replacement with readable backups for Setup
  Assistant manifest updates.

### Changed

- Marked `InputReader`, `CoCoInputIntent`, and the retained legacy Input
  presentation interfaces obsolete. `InputRuntime` implements those interfaces
  explicitly for the existing UI/Camera transition only; generated project
  input uses `ICoCoIntentFrameSource<TIntent>`.
- Frozen module naming so only Core types use the `CoCo*` prefix. New Input,
  Localization, and generator types use `Input*`, `Localization*`, and
  `ProjectScaffold*`; the package namespace and asmdef identity remain
  `CoCoFlow.*`.
- Added Action Map, rebind, source-disable, Host lifecycle, and Temporal
  Preview/restore fences so queued commands and continuous snapshots cannot
  burst after authority resumes. Action/Map Enable and rebind restore now gate
  actuated controls until they return to neutral, so held input cannot be
  mistaken for a new post-fence command.
- Deferred Runtime Action subscription and the one-time persisted Override load
  until PlayerInput and Store initialization complete. Continuous reads now
  return `false/default` during Runtime Disable, controlled transitions, Binding
  resolution, and Neutral Gate instead of restoring held legacy snapshots.
- Binding-control resolution now fences direct Input System override changes,
  gates newly bound held controls, and refreshes prompts without implicitly
  persisting project-authored overrides. Input fence and prompt observers are
  isolated per subscriber, so an observer exception cannot roll back a
  successful rebind or skip later observers.
- Added `InputAuthorityRevision` so same-frame lifecycle, Temporal, and
  Persistence restore boundaries cannot become input-transparent. Prompt
  selection now follows Control Scheme binding groups and the actual last-used
  paired device; runtime Action Asset replacement unsubscribes the cached old
  collection before accepting replacement input.
- `UIWidgetLocalizedText.SetArguments()` now restores a suppressed
  presentation even when no `LocalizedString` is configured, immediately
  showing the current fallback with a `MissingLocalizedString` diagnostic.
- Scaffold Preview fingerprints now cover compiled Provider type identities,
  request mode, conflicts, paths, and generated content. Apply rechecks
  symlink/reparse-point safety, reports incomplete rollback residuals, and
  reports staging cleanup independently from project-code rollback.
- Scaffold Preview now inventories project asmdefs, reserves the fixed Graph and
  Runtime assembly identities project-wide, rejects a second Scaffold root
  before Provider compilation, and prevents Assembly-CSharp output from
  inheriting an existing asmdef.
- Updated package metadata, Setup Assistant status, docs, and validation
  exception scopes to `0.4.0-pre.14`.
- Setup Assistant now reports default Localization Core separately from the
  optional Localization UI and Input Prompt UI extensions, which retain the
  three existing UI V2 support-define requirements.

### Verification

- `PASS`: Unity 6000.3.20f1 focused EditMode Input `6/6`, Scaffold `20/20`,
  Setup Assistant `23/23`, and Pre14 naming boundary `1/1`; focused PlayMode
  Input `17/17` and Localization `5/5`.
- `PASS`: Assembly-CSharp and custom-asmdef Scaffold outputs compiled in
  CoCoLab. The real `TypeCache` plus `CompilationPipeline` detector recognized
  the generated Provider in `ProjectStateGraphBindings.cs`; a second root kept
  that Provider identity visible but was blocked by the fixed Scaffold assembly
  identities, and two real compiled Providers also blocked Apply.
- `PASS`: a fresh default host with no UI V2 support defines compiled Input
  Core, Localization Core, Setup Assistant, and Project Scaffold Editor
  assemblies while the UI, Localization UI, and Input Prompt UI assemblies
  remained intentionally absent.
- `PASS`: warmed Queue/Batch and Neutral Gate polling each completed 1,000
  iterations with zero managed bytes allocated on the measured thread.
- Package-wide EditMode/PlayMode baseline comparison, generated runtime-loop
  validation, and macOS Universal IL2CPP High-Stripping evidence are recorded
  against the exact final Head in PR #30.
- `SKIPPED`: Unity Package Validation Suite is not installed locally and was
  explicitly waived for this delivery.

### Deferred

- Pre15 removes the legacy Input bridge and handles the broader package naming
  pass, Adventure Starter, and production Character/Item gameplay input.
- Localization does not alter Content Direct/Addressables ownership. The
  official package may bring Addressables transitively, but presentation
  loading and gameplay content authority remain separate.

## [0.4.0-pre.11] - 2026-07-25

### Added

- Engine-independent Animation contracts for fixed-capacity Parameter,
  Trigger, Playback, and Modulation Operation Sections, plus typed playback,
  feedback Event/Intent, reducer, adapter, and `AnimPlaybackContext` surfaces.
- `AnimAutoOperator` for Parameter and Trigger delivery only, and
  `AnimOperator` for one manually evaluated `AnimatorControllerPlayable`.
  `AnimOperator` supports positive Tick or explicit positive Step evaluation,
  mapped `Play`, `CrossFade`, and `Stop`, four playback layers with lifecycle
  tokens, eight modulation lanes, and one owned playback Context outcome.
- `AnimEventSmb` State Enter, Marker, and State Exit feedback with per-Animator
  trigger state. Feedback enters the Actor Event path only after a successful
  commit: Playable feedback is visible on the next accepted Tick, while Direct
  SMB feedback is staged until the next Operator commit and projected on the
  following accepted Tick.
- Optional internal `AnimRootMotionRelay` behavior owned by `AnimOperator`.
  Position and rotation forwarding can be selected independently; the relay
  emits typed deltas and never writes a Transform, CharacterController, or
  Rigidbody.
- Controller-authoritative custom Inspectors for fixed-lane mapping and
  validation, plus retained SMB injection/editing tools. Animator layers,
  states, transitions, Blend Trees, parameters, and clips remain authored in
  the Animator Controller.
- Conditional DOTween modulation and UniTask playback-waiter assemblies.
  DOTween advances only Animation-owned tweens; UniTask cancellation cancels
  only the waiter and never stops playback.

### Changed

- Replaced the retained 0.3.9 `AnimHandler` surface with the two Pre11
  Operators. `AnimEventSmb` is a `StateMachineBehaviour`, and
  `AnimRootMotionRelay` is an internal plain helper, so the Animation V2
  production surface contains exactly two `MonoBehaviour` components.
- Assigned `AnimAutoOperator` the new script GUID
  `e3244dec4ece44d9a45369111d6c2344`. The legacy `AnimHandler` GUID is not
  reused and no migration alias is provided; 0.3.9 scenes must replace the
  resulting Missing Script explicitly.
- Changed `AnimOperator.CurrentPlayback` and `TryGetPlayback` to report only
  committed `AnimPlaybackContext` state. Candidate execution state remains
  private and cannot leak through a cancelled transaction.
- Made the Auto and Advanced descriptors aliases of one Host-exclusive
  Animation Operator identity. A Host cannot configure both variants.
- Scoped playback tokens and the Advanced Playable runtime to the current
  `GraphInstanceId`; Host Stop/Start rebuilds local playback, Hold, modulation,
  root-motion, and feedback state before the next execution.
- Invalidated the previously committed playback snapshot when the Advanced
  runtime is rebuilt, so public queries and active UniTask waiters cannot
  observe playback owned by a destroyed Playable graph.
- Normalized finite rotation modulation with an overflow-safe scaled
  calculation shared by Immediate and DOTween paths.
- Isolated SMB marker cursors by Animator layer and current/next state instance,
  preserved early-transition interruption across large evaluations, and made
  DOTween target-write failures reject the Operator transaction explicitly.
- Updated Setup Assistant reporting, public documentation, the package version,
  and the two existing Unity Package Validation Suite exception scopes to
  `0.4.0-pre.11`. DOTween and UniTask remain optional rather than hard package
  dependencies.

### Verification

- `PASS`: on this six-fix Head, Unity 6000.3.20f1 Batchmode reimport and
  compilation; focused Setup EditMode (`9/9`); focused non-Replay Animation
  PlayMode (`24/24`). The real `AnimatorControllerPlayable` fixture covers
  Loop, OneShot exit-time, Parameter, concurrent same-state SMB Markers, Root
  Motion, natural completion, large-Tick early interruption, and
  committed-only playback queries.
- `FROZEN`: the prior G1/G3 Replay probes remain intentionally
  `INCONCLUSIVE`; this fix pass did not rerun or change Exact Replay.
- `BASELINE`: repeated package-wide EditMode runs are
  `593 PASS / 15 FAIL / 1 SKIP` and `592 PASS / 16 FAIL / 1 SKIP`, against
  exact Base `0df9d486` at `586 PASS / 15 FAIL / 1 SKIP`. The 15 baseline
  failure names recur; the intermittent additional failure is the excluded
  Map timing case `ForceFallbackKeepsLateSourceAheadOfTargetCleanup`.
  Package-wide PlayMode is `331 PASS / 6 FAIL / 2 INCONCLUSIVE` against Base
  `300 PASS / 6 FAIL`; the six failures are identical and the two added
  inconclusive results are the frozen G1/G3 replay gates.
- `UNVERIFIED`: Package Validation Suite is not installed in the local Editor
  or package cache. macOS Universal IL2CPP with High Managed Stripping reached
  the requested `IL2CPP / High / architecture=2` settings, but the Editor's
  macOS IL2CPP player support is not installed. Performance observations also
  remain unverified.

### Deferred

- Exact Animator replay did not pass the bounded replay gate within the frozen
  Pre11 scope (`G1=UNVERIFIED`, `G2=NO-GO`, `G3=UNVERIFIED`). `AnimOperator` is forward-only:
  `AnimExactReplayStatus.Deferred` is explicit, and Temporal Preview,
  projection, restore, Confirm preparation, and correction fail closed rather
  than approximating a pose or evaluating backwards.
- Generic/low-level Playable abstraction, built-in IK or rigging, world
  root-motion application, a second authored state machine/profile, and
  negative Tick or Step evaluation remain outside Pre11.

## [0.4.0-pre.10] - 2026-07-24

### Added

- A complete Map Region-fidelity contract built around stable Region, Chunk,
  participant-slot, mode, and capability identities. `RegionCapabilitySet`
  carries the four ordered `cocoflow.*` standard capabilities and
  project/TA-owned namespaced custom capabilities without reducing the model to
  a fixed enum.
- Editable `CoCoRegionProfile` tier ladders with a built-in five-tier baseline,
  stable serialized Profile/Tier identities, schema version `1`,
  Participant-by-Tier Enabled/Mode/`[SerializeReference]` configuration,
  strict-superset and complete-matrix validation, explicit catalog/provider
  registration, exact snapshotted AOT types, immutable per-tier variants,
  copied `RegionImmutableArray<T>` extension data, and deterministic compilation
  caching suitable for Player/AOT builds.
- `CoCoRegionBinding`, serialized `RegionChunkBinding`, and
  `CoCoRegionChunkAnchor` authoring contracts for Region-global and
  Chunk-scoped participant slots, dependency validation, canonical Direct or
  Addressables Scene ownership, cold-start fragments, and required/optional
  behavior.
- Scope/Lease demand ownership through `CoCoMapHost`,
  `RegionDemandScope`, and `RegionDemandLease`. Immutable lease revisions expose
  `Ready`, `Cancelled`, `Superseded`, `Failed`, and `Disposed` readiness results
  independently from internal transition generations.
- Per-demand Coverage resolution. Region-global nodes merge every live demand;
  each Chunk merges only the demands that cover it, so a high-fidelity demand
  for one Chunk cannot raise sibling Chunks. Unknown Chunk IDs fail the complete
  create or update operation instead of being ignored.
- Capability-triggered cross-Region dependency rules with normalized tuple
  identity, global target/Chunk/DAG validation, independent target Leases,
  target-ready blocking, transitive expansion, and make-before-break release.
- Transactional participant transitions with stable plan-node identity,
  fingerprint-based reuse, ordered Residency/Services/Simulation/Presentation
  phases, reverse cleanup, optional-degraded snapshots, retryable preparation
  failures, blocked-cleanup observation, and explicit terminal commit-fault
  handling.
- Built-in Content, GameObject, Collider, Renderer, Animator, Particle,
  Behaviour, and Pool-aware participant surfaces, plus public extension
  contracts for project and world-response TA implementations.
- Map authoring and runtime inspection surfaces for profile templates,
  Participant-by-Tier configuration, Coverage, dependencies, compiler
  diagnostics, and an internal immutable monitor snapshot covering live demand
  and revision state, desired/committed Tier and effective capability per Chunk,
  participant/dependency ownership, Content sequences, Temporal retention,
  reuse/candidates/retirement, degradation, faults, blocked cleanup, and
  old-plus-candidate peak ownership without exposing raw runtime authority.

### Changed

- Replaced the retained Pre8 scene-pusher Map implementation with Region
  fidelity orchestration. A Region is a logical gameplay/presentation unit;
  Chunks are its optimization partitions and do not define independent policy.
- Made Content the sole Additive Scene lease authority for built-in Map scene
  participants. Managed Chunk scenes cold-start with one metadata-only anchor
  root and inactive managed roots; runtime discovery is restricted to the
  exact leased Scene.
- Bound Pool Scope lifetime to stable committed Map nodes rather than transition
  generations. Unchanged nodes reuse their Scope; a replacement closes the old
  Scope after candidate commit and before its Scene lease is released.
- Added a Map Temporal decorator ahead of optional Pool and project bindings.
  It records committed capability/Coverage for availability barriers and
  retention only; it neither serializes nor replays Map state, and Preview
  performs no scene load, Pool preparation, or fidelity-tier commit. Logical
  demand mutations coalesce behind one callback-spanning barrier and dispatch
  only their final resolutions from `LateUpdate`; startup rejects direct and
  indirect decorator cycles.
- Made retry acceptance transactional and unified explicit shutdown,
  `OnDisable`, `OnDestroy`, and Content-first fallback behind one idempotent
  terminal task that freezes operations, disposes Scope/Lease ownership, cleans
  transitions, and unregisters from Content in order.
- Updated the package and the two existing Unity Package Validation Suite
  exception scopes to `0.4.0-pre.10`; the Unity minimum and package dependency
  list are unchanged.

### Removed

- Removed `MapResourceManager`, `MapStreamTrigger`, and
  `MapChunkLoadedEvent`, including the two legacy script GUIDs. Pre10 provides
  no compatibility facade, migration component, or automatic serialized
  upgrade for the old Map surface.
- Removed the Map assembly's dependency on `CoCoFlow.Runtime.Core`; the new Map
  contract depends on explicit contracts and module boundaries.

### Migration

- Replace `DemandScene` with a `RegionDemandScope` demand and retain its
  `RegionDemandLease`.
- Replace `ReleaseScene` with idempotent lease disposal.
- Replace loaded-event observation with
  `WaitUntilReadyAsync(revision, cancellationToken)` or the immutable Map
  runtime snapshot.
- Reauthor legacy Map scenes as compiled Region/Chunk bindings. A managed Chunk
  Scene must satisfy the cold-start anchor contract before it can be owned by
  Map.

### Verification

- `PASS` (static): separated Map, Pool adapter, Temporal adapter, Editor
  authoring, external TA, and focused test assemblies compile with zero Roslyn
  errors; JSON/asmdef, GUID/meta, legacy-symbol/GUID, dependency, and raw
  loading-authority gates pass.
- `BLOCKED`: Unity `6000.3.20f1` CLI reached CoCoLab domain loading but its local
  Licensing Client rejected protocol `1.18.1`; no test-result XML was emitted
  and CoCoLab's four pre-existing tracked changes remained byte-identical.
- `UNVERIFIED`: actual EditMode/PlayMode execution, Direct/Addressables runtime
  integration, package-wide tests, Package Validation Suite, performance
  observations, and macOS Universal IL2CPP with High Managed Stripping.

### Deferred

- Pre10 validation is scoped to record performance observations for warm
  transitions, large Coverage, overlapping Regions, old-plus-candidate peak
  residency, and cleanup duration. Those observations remain `UNVERIFIED`; no
  hidden budget, automatic downgrade, or threshold policy was added.
- Whole-world rollback, Map-state replay, generic non-GameObject pooling,
  durable reconstruction, and out-of-contract TA scene or Content ownership
  remain outside this release.

## [0.4.0-pre.9] - 2026-07-23

### Added

- A Content-backed GameObject Pool runtime with stable `PoolId`, serializable
  `PoolProfile`, explicit `CoCoPoolHost`/`PoolScope` composition, and one
  Prefab Source `ContentLease` per prepared Pool Entry. The public API does not
  expose a generic pool; Unity's Object Pool remains a private implementation
  detail.
- Asynchronous atomic Prepare and explicit Prewarm, followed by synchronous
  Ready-only Rent. `PrewarmCount` is a preparation target and `MaxRetained`
  limits only idle retention; empty pools may burst, and return overflow is
  destroyed without introducing a hard active/total cap.
- Readonly generation-safe `PooledHandle` ownership, inactive bind-before-
  activate flow, deterministic synchronous `IPoolable` rent/return callbacks,
  stale/duplicate/cross-Scope rejection, reset-failure destruction, external
  destruction detection, and force-cleanup diagnostics for leaked rentals.
- Explicit Scope close semantics that reject new work, destroy idle and late-
  returned instances, and release the Prefab Source only after every physical
  and Temporal-retained instance is terminal. Content-first shutdown invokes
  the same Pool dependency drain before disposing Content Scopes.
- Immutable identity/count-only Pool snapshots, a fixed-capacity diagnostic
  ledger, Pool Host authoring, a Runtime Monitor with manual idle Clear, and
  Setup Assistant status for Pooling and Pooling Temporal without a separate
  Pool package installation action.
- An optional Host-scoped Pooling Temporal sidecar with pure
  `CoCoTemporalEntityId`, generation-authority transfer, physical-instance
  quarantine while history remains reachable, restoration of the live
  presentation parent on reappearance, branch/overwrite cleanup, and a separate
  synchronous Temporal Apply hook. Transform references remain outside history.
  The sidecar is not multi-Actor or whole-world rollback.
- Contract, lifecycle, Content-ownership, Unity Object Pool behavior, Temporal
  retention/projection, Direct-only dependency, optional Addressables, and
  Player-build verification surfaces for Pre9.

### Changed

- Updated the package version and existing Unity Package Validation Suite
  exception scopes to `0.4.0-pre.9`.
- Updated Content, Temporal, UI, and Map documentation to distinguish Prefab
  Source leases from physical-instance ownership. Existing UI, Map, and Enemy
  consumers are intentionally not migrated by Pre9.
- Kept Addressables optional: Direct and Addressables Prefab Sources enter
  Pooling through the same Content request, and Pooling retains no raw
  Addressables handle.

### Fixed

- Avoided forced-shutdown warnings for clean Content-first Pool dependency
  drains while preserving terminal force cleanup and diagnostics for live
  physical or Temporal ownership.
- Guarded public Temporal Adopt, Activate, and Despawn mutations with the frozen
  downstream Restore identity, Unity-liveness, and Host-boundary checks before
  Pool ownership can change.
- Kept `TemporalState` Confirm eligibility observation side-effect free;
  physical identity is revalidated by the actual projection and Confirm
  preparation paths.
- Restored each physical instance's captured prefab-root local transform
  baseline whenever normal or Temporal ownership returns it to retention.
- Preserved explicit Scene Root and the latest live Transform parent across
  Temporal replay, with structured terminal cleanup for destroyed physical or
  parent identity.
- Matched Temporal Rent/Return callbacks across pending, active, quarantined,
  Host-stop, and re-entry paths; pending activation remains non-despawnable.
- Froze downstream Restore identity at Host attachment and validated it before
  Pool mutation, around the downstream call, and before after-restore
  activation.
- Allowed projected-only physical loss to complete the same absent-authority
  Cancel or Correction while retaining the fault for authority-present loss.
- Limited Runtime Monitor `Clear Inactive` enablement to Ready entries and
  clarified same-frame external-destroy/force-shutdown event classification as
  best-effort while terminal ownership guarantees remain strict.

### Deferred

- Generic non-GameObject pools, Unity versions below Unity 6, a separate Pool
  package/container, hard active/total caps, automatic trim/LRU, runtime hot
  profiles, and direct Addressables ownership remain outside Pre9.
- UI/Map/Enemy migration, permanent-scene-object pooling, network or world
  rollback, durable entity reconstruction, and reflection-driven automatic
  cleanup remain owned by downstream work or explicitly unplanned extensions.

## [0.4.0-pre.8] - 2026-07-23

### Added

- A Unity-facing Content acquisition and ownership runtime with stable Content
  IDs, explicit owner Scopes, reference-type idempotent Leases, typed Asset,
  Prefab Source, and Additive Scene requests, and one explicit project/world
  composition Host instead of a static global runtime.
- Exact-key single-flight loading, per-caller cancellation isolation,
  generation-safe late-completion cleanup, retryable load failures, immediate
  last-lease release, and fail-closed release tombstones without a hidden
  cache, grace period, or LRU policy.
- A Direct backend for serialized Assets/Prefab Sources and SceneManager
  Additive Scenes, including exact-instance ownership for concurrent same-path
  requests, plus an optional conditional Addressables backend that keeps backend
  handles private while preserving the same request/result/lease API.
- Immutable runtime snapshots, a fixed-capacity identity-only ownership ledger,
  optional bounded acquisition-stack capture, structured Content diagnostics,
  Inspector authoring, and a Content Runtime Monitor.
- Contract and PlayMode coverage for ownership, cancellation races, generation
  replacement, release failure, Direct/Addressables paths, requester sharing,
  UI panel-source lifetime, assembly boundaries, and dependency variants.

### Changed

- Migrated UI panel prefab ownership from a manager-lifetime Addressables handle
  cache to one Prefab Source lease per live panel instance. UI still owns
  Instantiate/Destroy; navigation, focus, transition, and final authoring policy
  remain deferred to Pre12.
- Migrated Map scene ownership from one global string desired-set to explicit
  requester-scoped Content demands. Different requesters may share one physical
  load, and one requester cannot unload another's scene lease; Region/Chunk and
  production streaming policy remain deferred to Pre10.
- Removed Addressables from `package.json` hard dependencies. Direct-only hosts
  do not need Addressables; Setup Assistant exposes an explicit optional
  Addressables installation action.
- Updated the package version and existing Unity Package Validation Suite
  exception scopes to `0.4.0-pre.8`.

### Fixed

- Protected concurrent `ContentScope` request completion and disposal so its
  cancellation source, owned leases, and Runtime registration cannot be left
  partially cleaned.
- Added failed-load cleanup authority to the backend contract. Addressables
  failed handles are now released through Runtime ownership, and a cleanup
  failure retains the same fail-closed tombstone as a published release
  failure.
- Rejected worker-thread `ContentRuntime` creation before backend registration
  instead of treating the first caller thread as Unity's main thread.
- Canonicalized Direct additive-Scene locators against Build Settings and made
  queued Scene loads observe cancellation before starting physical work.
- Aligned Setup Assistant compatibility reporting with the optional
  Addressables assembly range `[2.9.1,3.0.0)`.
- Added delayed UI/Map lifecycle races and real Addressables additive-Scene
  ownership to the Pre8 verification surface.

### Deferred

- Pre9 owns Object Pool instance rent/return, reset, capacity, and prewarm.
- Pre10 owns Map Region/Chunk policy, distance streaming, race orchestration,
  replay, and final monitoring.
- Pre11/Pre12/Pre13 respectively own Animation consumption, final UI behavior,
  and Persistence content identity/migration. Pre16 owns complete cross-module
  performance and platform certification.

## [0.4.0-pre.7] - 2026-07-22

### Added

- A constrained StateGraph Editor for Layer, recursive State, and same-Layer
  leaf-to-leaf Transition authoring. Every topology mutation uses one
  Undo-aware authoring operation; subtree deletion removes incident
  Transitions. Deleting an initial State requires an explicit valid surviving
  sibling when one exists; with no survivor, the user may explicitly confirm
  an empty compiler-invalid draft.
- A separately versioned, presentation-only Editor layout stored in the Graph
  Asset by stable State ID. State positions survive save, reload, Asset copy,
  Layer duplication, and subtree copy/remap without changing runtime schema v1,
  content fingerprints, or compilation-cache identity. Layer selection,
  breadcrumb, foldout, pan/zoom, search, and selection remain session state.
- Deterministic internal Catalog enumeration, overlays for the existing Intent,
  Graph Operation, and ContextFrame State manifests, plus Catalog-parameterized
  presets. Simple creates two generic States and one same-Layer Transition;
  Combo creates the generic
  `Step1 -> Step2 -> Step3 -> Step4 -> Exit` topology without gameplay logic,
  animation timing, Samples, public category metadata, or a fourth Manifest.
- Explicit ordered Host references for Intent Sources, Event-to-Intent Adapters,
  and Operators, plus the existing Actor Context and Restore references. The
  Inspector suggests compatible scene components but persists nothing until
  the user confirms, and all configuration is read-only while Running.
- An internal immutable committed debugger snapshot for point-in-time Host,
  Context, Clock, and per-Layer path inspection. It is distinct from the
  optional fixed-capacity identity-only Trace history, whose capacity defaults
  to zero and cannot change while Running, and exposes no payload, mutable
  Frame, retained Context handle, Inbox, Envelope, or private field.
- An internal Editor debug step for a healthy Suspended Host under Update,
  FixedUpdate, or Manual driving. One explicit positive delta executes exactly
  one normal forward Tick; success returns the Host to Suspended, while Fault
  and world-correction failures remain visible instead of being disguised as a
  successful suspension.

### Changed

- Kept the Project Provider authoritative for the frozen descriptor Catalog,
  generic/AOT factories, binding types, Codecs, and Context defaults while the
  Host owns the user-confirmed scene component references and their order.
- Restricted new 0.4 Editor and public documentation terminology to Graph,
  Layer, State, and Transition. Retained 0.3.9 controller implementation names
  remain transitional code and are not migrated or deleted by Pre7.
- Updated the package version and the two existing Unity Package Validation
  Suite exception scopes to `0.4.0-pre.7`; dependencies remain unchanged.

### Deferred

- Cross-Asset and cross-Editor-session clipboard transfer remain deferred.
- Pre11 owns Animator/Playable behavior and concrete combo timing; Pre13 owns
  durable persistence and migration; Pre15 owns production gameplay States and
  replacement Samples; Pre16 owns complete cross-module certification; Pre17
  owns final visual and XML-documentation polish.

## [0.4.0-pre.6] - 2026-07-19

### Added

- One fixed-capacity Temporal Ring per `CoCoStateGraphHost`, storing
  preallocated exact-layout Temporal projection payloads instead of retaining
  complete `ContextFrame` handles. Capacity zero disables history, enabled
  history requires at least two entries, and every successful commit records one
  entry including the current authority.
- An authority-neutral Preview workflow with explicit Begin, depth selection,
  Confirm, and Cancel operations. Preview never runs StateGraph or Operators
  backwards; Confirm performs one Restore into a new TimelineEpoch. Cancel
  reapplies unchanged current authority only after a successful Preview
  projection; Begin-to-Cancel invokes no binding.
- One synchronous `ICoCoContextRestoreBinding` for enabled Temporal history and
  its Preview, Confirm, Cancel, and world Correction operations, plus read-only
  `CoCoTemporalState` inspection without exposing mutable Frames, payloads, or
  generation handles. Capacity zero ignores invalid binding assignments but may
  retain one valid Host-local binding solely for general world Correction.

### Changed

- Temporal capture now encodes the finalized Context candidate before the
  composite authority barrier. A capture failure cancels the Tick with the old
  authority, zero committed Outbox publication, and zero final sequence
  consumption; history publication after the barrier is no-fail.
- Restore rebuilds a complete Context from Stored Temporal payload bytes,
  Layout defaults, and the Derived dependency closure. A successful Confirm
  keeps Timeline and ClockDomain identity, advances Epoch and execution
  sequence, discards the old future, and records the new branch head.
- Beginning Preview immediately clears Inbox queues, sealed batches, and
  deduplication state. Messages arriving during Preview are dropped and counted;
  Cancel keeps the original Epoch but does not resurrect the cleared backlog.
- Kept the Runtime lifecycle unchanged and added an orthogonal Host
  `CoCoTemporalMode`. A clean binding preflight failure before any projection
  rejects the request without Fault. Once a callback starts or a successful
  Preview projection remains active, binding failure leaves Context authority
  unchanged and requires explicit world Correction before progress can resume.
- Updated the package version and the two existing Unity Package Validation
  Suite exception scopes to `0.4.0-pre.6`; dependencies remain unchanged.

### Deferred

- Pre11 owns concrete Animator/Playable temporal presentation, and Pre13 owns
  durable persistence formats, migration, and world facts.
- Pre15 owns replacement production Samples; Pre16 owns full cross-module
  performance and lifecycle certification.

## [0.4.0-pre.5] - 2026-07-18

### Added

- Explicit per-Host Operator bindings with exact Operation Section coverage,
  typed Outcome ownership, deterministic Claim arbitration, and fixed-capacity
  typed EventOutbox candidates.
- Default-backed first-Tick Context reads without inventing a committed Tick 0,
  followed by one complete ContextFrame Revision for every accepted Tick.
- One per-Actor composite authority barrier covering staged StateGraph state,
  OperationSequence, Clock, ContextFrame, Claims, and EventSequence before any
  committed EventOutbox packet becomes visible.
- Exact Graph-state, Graph-value, Claim, Operator, Actor, and Derived Context
  producer ownership, including one explicit Actor binding when Actor-owned
  Slots exist.
- Immutable identity-only Runtime Trace entries with Candidate/Winner roles and
  value-only Frame references, plus pure complete-Actor restore validation and
  an internal no-callback composite prepare/apply seam that remains available
  at idle faulted boundaries without clearing Fault or world-correction state.

### Changed

- Replaced the Pre4 internal test coordinator with the production Operator and
  Context transaction owned by each `CoCoStateGraphHost` instance.
- Made Context arena authority the Host's sole committed Context source and
  made Graph, Clock, and Claim caches mirrors that can be rebuilt uniquely from
  it. Transaction preflight now rejects invalid producer, Operator, Claim,
  Actor-binding, and Outbox coverage before Runtime factories execute.
- Kept Context defaults supplied by the trusted Project Provider. Their semantic
  fingerprints are declaration tokens checked against the Manifest, not
  framework-computed canonical hashes of the supplied values.
- Updated the package version and the two existing Unity Package Validation
  Suite exception scopes to `0.4.0-pre.5`; dependencies remain unchanged.

### Deferred

- Pre6 owns Temporal history, Host Restore Binding, rewind/resume, and new
  TimelineEpoch orchestration; Pre7 owns Trace UI.
- Pre11 owns concrete Animator/Playable Operators, and Pre13 owns durable
  persistence formats, migration, and world facts.

## [0.4.0-pre.4] - 2026-07-17

### Added

- An engine-independent StateGraph Runtime with per-Host StateLogic, Condition,
  double Memory banks, ActiveLeaf state, Actor Clock, staged Tick, and latched
  Fault ownership; multiple Hosts share only the immutable compiled graph.
- Pure C# State callbacks with optional `OnEnter`, mandatory `Update`, and
  optional `OnExit`, including parent-to-child Enter, root-to-leaf Update, and
  leaf-to-parent Exit ordering.
- Declaration-and-evaluation Transition handling: leaf Update may request
  several precompiled outgoing handles, then windows, Conditions, and explicit
  Priority produce at most one winner per Layer and Tick.
- Activation-scoped `LocalSeconds` and `ActionProgress` windows with half-open
  sweep evaluation, large-Delta crossing support, and no implicit exit when
  progress reaches one. Progress is finite and monotonically non-decreasing;
  equal values may stall, while a decrease cancels the candidate and latches
  Fault. Transactional rollback restores committed authority but never permits
  progress to move backwards.
- Ranked Operation composition where later Layers override earlier Layers and
  children override parents, with field-level Continuous merging and final-only
  Discrete sequence allocation.
- A transactional OperationFrame
  `TryBegin -> Write -> TryFinalize -> FinalizedFrame -> Commit/Cancel`
  protocol. Finalize freezes a candidate without consuming Sequence or LastTick.
- `CoCoStateGraphHost` as the only new public MonoBehaviour and the Actor's
  unified gameplay-event boundary, backed by internal Clock/Driver, Gateway,
  per-Domain Router, Inbox, Registry, and EventAgent bridge objects.
- Exact immutable runtime binding coverage for State, Condition, Memory, Intent
  Source, and Event Adapter factories. A mismatch fails Host startup before any
  callback, Tick, or Router registration.
- Asset declaration-list order as the authoritative Event Adapter execution
  order; the project binding Provider cannot reorder those semantics.
- Typed local, Targeted, and declared-broadcast event ingress using atomic
  `CoCoEventPacket<TEvent>` values, next-Tick Inbox sealing, bounded Suspend
  accumulation, Fault gating, and lifecycle-safe Router registration.

### Changed

- Redefined the prerelease StateGraph Schema v1 in place. Transition endpoints
  must be leaves, outgoing Priority is explicit and unique per source leaf, and
  all Event declarations in one Graph must belong to one EventDomain.
- Made Asset Layer list order the runtime composition order from low to high.
  Reordering changes the content fingerprint and `DenseIndex` order without
  changing stable Layer IDs.
- Kept a Transition Tick on its source path through Update and Exit; the
  committed target enters and updates on the next accepted Tick. A self-loop
  reactivates only the leaf and resets its Activation and Memory.
- Made Start select initial leaves and queue Enter work without invoking user
  callbacks. Suspend preserves the instance and bounded Inbox; Stop discards it
  without synthesizing an Exit Tick.
- Restricted legal Runtime lifecycle edges: `Created` cannot Stop and Host
  public `TryDispose` accepts only `Created` or `Stopped`; Runtime `Dispose()`
  and Unity destruction force live cleanup through `Stopped` before disposal.
- Rejected lifecycle reentry during Host startup and Tick advancement, and made
  Unity destruction cancel startup publication or staged authority before commit.
- Updated the package version and the two existing Unity Package Validation Suite
  exception scopes to `0.4.0-pre.4`; no dependency or new exception was added.

### Removed

- Framework-level Completion, `RequireSourceCompletion`,
  `AllowDuringSourceActivation`, and the entire InterruptPolicy surface.
- Equal-Priority tie breaking within one source leaf, composite-State
  Transition endpoints, and any implication that ActionProgress reaching one
  automatically exits a State.

### Deferred

- Pre5 owns the explicit Host Operator list, Operator contracts/execution,
  Outcome aggregation, ContextFrame commit, and committed EventOutbox
  publication. Pre4 intentionally exposes no production commit shortcut.
- Pre6 owns Temporal history, Restore, rewind, and new TimelineEpoch creation;
  Pre11 owns Animator/Playable/SMB replacement and visual reverse mapping.
- Network Drivers, durable persistence, production Samples, cross-Layer calls,
  queries, signals, Transitions, arbitrary `ChangeState`, and 0.3.9 runtime or
  experimental Pre3 Asset migration are not part of Pre4.

## [0.4.0-pre.3] - 2026-07-16

### Added

- `CoCoStateGraphAsset` as the sole Unity authoring truth for Graph, Layer,
  recursive State, and Layer-owned Transition records with serialized stable
  identities.
- An engine-independent StateGraph compiler and validator that produce
  immutable hierarchy, active-path, and adjacency lookups without executing
  user StateLogic or Condition code.
- Frozen Intent Requirement, Graph Operation Provides, and ContextFrame State
  Requirement manifests for later Host binding validation.
- Framework-owned FrozenConfig Schemas and writers that canonicalize stable
  fields, defensively copy arrays, seal snapshots, and compute fingerprints
  without relying on author-provided frozen objects or hash functions.
- Graph-level Event-to-Intent static declarations in the Intent manifest,
  including Event Domain/payload and provided-Intent type/capacity validation;
  actual Adapter instances and coverage remain a Pre4 binding concern.
- Complete immutable Operation Section Shapes in the Graph provides manifest,
  including deterministic field indices, names, unmanaged types, offsets, and
  sizes shared with the StateFlow Registry validator.
- Immutable AOT binding tokens for Intent reducers, Operation Section views,
  and derived StateSlot rebuilders; catalog fingerprints include both binding
  type and deterministic semantic identity without retaining executable
  instances.
- Structured graph diagnostics with Graph/Layer/State/Transition and field-path
  locations, including non-blocking unreachable-State warnings.
- Editor authoring operations for identity-safe whole-Asset, Layer, and
  State-subtree duplication, compile diagnostics, and diagnostic navigation.
- Editor Analyze and Player-build gates that validate the complete resolved
  dependency closure of registered author assemblies, plus temporary linker
  preservation metadata for validated Operation Section Shapes.

### Changed

- Allowed valid empty Intent and Operation layouts so terminal or no-operation
  graphs do not require artificial entries.
- Split StateGraph into an engine-independent compiler assembly, a Unity-facing
  authoring assembly, and an Editor-only tooling assembly with one-way
  dependencies.
- Keyed the Unity-facing compilation cache by Graph/content/catalog/schema and
  retained both successful and failed result identities; throwing factories are
  evicted instead of poisoning a key.
- Made FrozenConfig immutability and deterministic fingerprints framework
  guarantees, and made complete Operation Shape validity a Pre3 compile-time
  requirement rather than a later runtime assumption.
- Advanced the package and the two existing Unity Package Validation Suite exception
  scopes to `0.4.0-pre.3`; no package dependency or new validation exception was
  added.

### Deferred

- `CoCoStateGraphHost`, Clock/Driver integration, per-Actor runtime state,
  actual Event Adapter/Factory binding and exact coverage, EventRouter,
  EventAgent, and Inbox lifecycle are owned by Pre4.
- State evaluation orchestration, Operator execution, Outcome aggregation,
  ContextFrame commit, and EventOutbox publication are owned by Pre5.
- Temporal history and rewind remain Pre6 work; durable persistence and
  migration remain Pre13 work.
- Generated C#, baked compiled Assets, parallel scheduling, replacement
  Samples, and complete cross-module performance certification are not part of
  Pre3.

## [0.4.0-pre.2] - 2026-07-15

### Added

- State Flow frame contracts that separate frozen input (`IntentFrame`), the
  StateGraph execution guide (`OperationFrame`), and the committed Actor state
  (`ContextFrame`).
- OperationFrame Section identity, descriptor, fixed-registry, composition, and
  no-inheritance rules. Equal interface identities deduplicate; equal field
  shapes with different identities remain distinct.
- ContextFrame StateBlock/Slot descriptors, Temporal/Durable/Derived metadata,
  restore boundaries, and a versioned Codec feasibility path.
- Atomic `EventPacket<TEvent>` identity plus Actor EventInbox contracts for
  fixed-capacity double buffering, next-Tick visibility, deduplication,
  lifecycle handling, and Event-to-Intent projection.
- Contract gates for Frame isolation, Mailbox failure semantics, restore
  round-trips, AOT viability, and allocation-free steady-state paths.
- Generation-scoped ContextFrame handles plus explicit Prepared/Finalized
  commit tokens, preventing recycled arena storage from reviving stale Frame
  references.
- Per-GraphRuntime reducer factories and frozen Event Adapter manifests used to
  validate Actor Inbox startup.

### Changed

- Replaced the proposed Context-driven execution model with the one-way State
  Flow: Intent collection and freeze, StateGraph evaluation, OperationFrame
  generation, Operator execution, and ContextFrame commit.
- Reserved public Section contracts for `OperationFrame`. `IntentFrame` does not
  reuse Operation Sections, while `ContextFrame` stores committed state through
  StateBlock/Slot descriptors rather than a user-authored Root Context.
- Defined cross-Object gameplay communication as
  `EventBus -> EventRouter -> Actor EventInbox -> Event-to-Intent Adapter`.
  Raw EventBus callbacks and envelopes never enter StateLogic.
- Defined EventOutbox candidates as invisible until ContextFrame commit. Final
  event sequence allocation and publication occur only after a successful
  commit.
- Made Derived state Finalize-owned: Writers cannot set Derived slots, every
  successful commit rebuilds them in dependency order, and projected Derived
  state requires a closed set of Stored/Derived dependencies.
- Tightened Restore so a resumed TimelineEpoch must be newer than both the
  source and current authoritative Epoch, must remain in the source Timeline and
  ClockDomain, and must advance ExecutionSequence, while preserving precise
  Codec diagnostics.
- Hardened Intent/Inbox lifecycle behavior: old IntentFrames invalidate at the
  next collection boundary, callback failures cancel collection before
  propagating, startup requires an exact frozen Adapter manifest, and disposing
  a bound Runtime stops its Running Inbox.
- Made Intent reduction transactional for Runtime-owned reducer state and
  deferred callback-time Inbox lifecycle changes until they can safely cancel
  collection and invalidate the sealed projection batch.
- Required an idle bound Intent Runtime for Inbox sealing, suspension, and
  resumption, so events arriving after collection begins remain next-Tick data.
- Clarified that the Pre2 Durable Codec path is an internal, same-session,
  exact-layout spike; Pre13 owns the cross-session save identity and migration
  contract.
- Advanced the package and Unity Package Validation Suite exception scope to
  `0.4.0-pre.2`.

### Deferred

- StateGraph Asset compilation and automatic requirement/layout aggregation are
  owned by Pre3.
- `CoCoStateGraphHost`, Clock/Driver integration, the central EventRouter, and
  EventAgent subscription lifecycle are owned by Pre4.
- Production Operator execution, Outcome aggregation, ContextFrame commit, and
  EventOutbox publication are owned by Pre5.
- Temporal Ring Buffer, rewind/resume, and TimelineEpoch switching are owned by
  Pre6.
- Animation V2 and Playable integration are owned by Pre11; durable save
  projections, migrations, world facts, and container integration are owned by
  Pre13.
- Full cross-module performance and lifecycle certification remains owned by
  Pre16.

## [0.4.0-pre.1] - 2026-07-13

### Added

- Engine-independent Core contract domains for graph/runtime identity, time,
  lifecycle, diagnostics, and StateLogic dependency roles.
- Getter-only, reference-free Context Section requirements and declared
  Operation Port entries; Operation commands cross the State boundary as
  unmanaged values with no synchronous gameplay-result channel.
- Contract and architecture gates that keep Core independent from Gameplay,
  presentation modules, Editor code, Animator, and Playables.
- A pull-request checklist for Pre scope, package surface, Unity host validation,
  and contract changes.

### Changed

- Froze Graph, runtime, timeline, and clock identities as immutable Runtime
  values; Unity asset serialization schemas and ID authoring remain owned by
  the Pre3 StateGraph Asset/Compiler work.
- Tightened Context Section validation to accept only parameterless abstract
  instance getters and reject indexers, default/static members, fields, native
  handles, by-ref-like fact values, and strings nested inside composite value
  facts while continuing to allow a direct immutable string fact.
- Renamed the public dependency tokens to `CoCoContextSectionRequirement` and
  `CoCoOperationPortRequirement` before the Pre1 contract is merged.
- Froze the later-Pre authoring boundary around one `CoCoStateGraphHost` per
  actor, one ContextRuntime per GraphRuntime instance, framework-owned Context
  composition, and explicit Source/Operation bindings instead of a user-authored
  Root Context or Provider connection.
- Made failed TimelinePosition creation return an invalid sentinel, rejected
  malformed Diagnostic domain/code values, and allowed Created Runtime values
  to Dispose explicitly before their first Run.
- Assigned Port/Command affinity, handle-free Command shape validation, and
  StateLogic authoring dependency enforcement to their owning compiler and
  Operation Pres instead of overstating what the Pre1 marker types guarantee.
- Reframed Context execution as
  `Source -> Frozen Frame -> Independent Layers -> Operation -> Next Frame`.
- Set the package version to `0.4.0-pre.1` and marked the package as a
  Core-contract prerelease.
- Updated public documentation to distinguish frozen 0.4 contracts from
  transitional 0.3.9 module implementations.

### Removed

- The 0.3.9 Samples and Add-on import surface from package metadata and public
  setup/documentation.
- The 0.3.9 read-only state graph inspection workflow from the 0.4 public
  authoring surface.

### Transitional

- The 0.3.9 CCS Runtime remains temporarily to preserve compilation and
  regression evidence during the contract freeze. Pre4 replaces it; its APIs and
  Unity-lifecycle behavior are not 0.4 compatibility guarantees.
- Existing Camera, Animation, Persistence, Gameplay, and Editor implementations
  remain transitional until their owning Pres replace or retire them.

## [0.3.9] - 2026-07-01

- Historical CCS Runtime line. Existing projects should remain pinned to this
  revision family rather than mixing 0.3.9 behavior with 0.4 prereleases.
