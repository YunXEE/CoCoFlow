# Changelog

All notable changes to CoCoFlow are documented in this file.

The project uses `0.4.0-pre.N` for prerelease packages. The 0.4 line targets new
projects and does not include a migration runtime for 0.3.9 projects.

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
- Immutable identity-only Runtime Trace entries with a configurable fixed ring,
  plus pure Context restore-compatibility validation for later Temporal and
  Persistence orchestration.

### Changed

- Replaced the Pre4 internal test coordinator with the production Operator and
  Context transaction owned by each `CoCoStateGraphHost` instance.
- Made Context arena authority the Host's sole committed Context source and
  split Graph, OperationFrame, and Clock acceptance into fallible preflight and
  an internal no-callback commit path.
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
