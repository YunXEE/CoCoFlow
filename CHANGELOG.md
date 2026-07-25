# Changelog

All notable changes to CoCoFlow are documented in this file.

The project uses `0.4.0-pre.N` for prerelease packages. The 0.4 line targets new
projects and does not include a migration runtime for 0.3.9 projects.

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
- Updated Setup Assistant reporting, public documentation, the package version,
  and the two existing Unity Package Validation Suite exception scopes to
  `0.4.0-pre.11`. DOTween and UniTask remain optional rather than hard package
  dependencies.

### Verification

- `PASS` (static): Animation Contracts, base Runtime, conditional DOTween,
  conditional UniTask, and focused contract test assemblies compile against
  the Unity 6 host references. Seven focused contract tests pass through the
  direct test runner.
- `BLOCKED`: full Unity batch execution remains blocked before test execution
  by the local Unity Licensing Client protocol mismatch.
- `UNVERIFIED`: Unity EditMode/PlayMode execution, package-wide tests, Package
  Validation Suite, runtime Controller/SMB/root-motion integration, performance
  observations, and macOS Universal IL2CPP with High Managed Stripping.

### Deferred

- Exact Animator replay did not pass the bounded replay gate within the frozen
  Pre11 scope. `AnimOperator` is forward-only:
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
