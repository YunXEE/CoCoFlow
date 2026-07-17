# CoCoFlow

[English](README.md) | [简体中文](README.zh-CN.md)

> **Version**: 0.4.0-pre.4 · **Unity**: 6000+
>
> Pre4 adds the per-Actor StateGraph Runtime, the single Unity Host, deterministic
> lifecycle/Transition evaluation, staged OperationFrames, Clock, Inbox, and
> internal event routing. Operator execution and Context commit arrive in Pre5.

CoCoFlow is a Unity 6 State Flow and layered HFSM framework for new
single-player 3D adventure and action projects. Its 0.4 architecture separates
input intent, graph decisions, side-effect execution, committed Actor state,
and cross-Object messages instead of combining them in one mutable Context.

## State Flow

One accepted CoCoTick follows one direction:

```text
Input / AI / Network + sealed EventInbox
  -> Event-to-Intent Adapters
  -> freeze IntentFrame
  + Previous ContextFrame
  -> StateGraph
  -> OperationFrame Sections
  -> Operators
  -> Outcomes + EventOutbox candidates
  -> commit ContextFrame
  -> assign EventSequence and publish EventOutbox
```

Cross-Object gameplay input joins the flow through an Actor mailbox:

```text
CoCoEventPacket<TEvent>
  -> Actor Host Gateway
  -> internal EventRouter for one EventDomain
  -> target Actor EventInbox
  -> next accepted CoCoTick
  -> Event-to-Intent Adapter
  -> IntentFrame
```

The Host Gateway is the Actor's single event entry, the EventEnvelope is the
shipping label, the EventRouter is the cross-Actor sorting centre, and the
EventInbox is the mailbox. Local events enter the same Host's Inbox directly;
only cross-Actor targeted and declared-broadcast packets use the Router. An
Event-to-Intent Adapter translates a sealed message into this Actor's intent.
StateGraph never reads a raw callback, envelope, Router, or mailbox.

## Frozen Vocabulary

| Term | Meaning |
|---|---|
| `IntentFrame` | The immutable input for one CoCoTick. It is sampled and arbitrated once, is not persisted, and is not part of rewind history. |
| `OperationFrame` | The complete execution guide produced by StateGraph. It is the only Frame that exposes public Section contracts. |
| `ContextFrame` | A generation-scoped, read-only handle to the complete committed logical state of one Actor at a Tick boundary. It is the authority for restore and for later Temporal/Durable projections while the captured storage generation remains alive. |
| `EventInbox` | Pending cross-Object gameplay input for one GraphRuntimeInstance. It is not fact storage. |
| `EventOutbox` | Cross-Object output candidates produced during Operator execution. They are published only after ContextFrame commit succeeds. |
| `EventAgent` | A helper for EventBus subscription lifetime only. It does not route, queue, own, or persist messages. |

An `IntentFrame`, an `OperationFrame`, and a `ContextFrame` are not aliases and
cannot substitute for one another. Inbox contents, raw envelopes, the current
IntentFrame, and unpublished Outbox candidates never become ContextFrame state.

## StateGraph Asset and Compilation

`CoCoStateGraphAsset` is the sole serialized authoring truth. It stores Graph,
Layer, recursive State, and Layer-owned Transition records with stable IDs.
Rename, move, reorder, save/reload, and Config edits preserve those IDs. Whole
Asset, Layer, and State-subtree duplication deliberately remap the new copy and
its internal references; duplicated Config data is not shared with its source.
Layer list order is nevertheless semantic: first is lowest composition priority,
last is highest, so reordering changes the content fingerprint and runtime
result without changing a Layer ID.

Before the Host can run, the Unity-facing snapshot boundary deep-freezes
the Asset and passes pure data to `CoCoStateGraphCompiler`. A successful compile
produces one immutable `CoCoCompiledStateGraph` with hierarchy and adjacency
lookups plus three manifests:

- Intent requirements;
- Graph Operation provides;
- ContextFrame state requirements.

The Intent manifest also carries the Graph's immutable Event-to-Intent static
declarations. Pre3 validates their Event Domain, payload type, provided Intent
type, contribution-capacity lower bound, and one-Domain-per-Graph rule. Pre4
instantiates the declared Adapters and rejects Host startup unless runtime
binding coverage is exact. Adapter execution follows the Asset declaration-list
order preserved by the compiled manifest; the binding Provider cannot reorder
that semantic order.

Config Freezers write into framework-owned typed Schemas. The framework seals
and defensively copies the canonical field snapshot and computes its
fingerprints; snapshot immutability does not depend on author discipline.

Compilation never constructs or executes user StateLogic or Condition code.
Any Error prevents a compiled result; Warnings such as an unreachable State do
not. Transition cycles and terminal States are valid, while hierarchy cycles,
missing targets, duplicate IDs, and Cross-Layer edges are Errors.

An unchanged Asset fingerprint and catalog return the same cached result;
successful and failed results are both cached, while only success contains a
shared compiled graph. Per-Host mutable runtime state is never stored in that
shared object. See
[StateGraph Asset and Compiler](Docs/StateGraphCompiler.md) for the complete
schema, identity, diagnostic, threading, and deferred-runtime boundary.

The prerelease serialized Schema remains v1 but is redefined in place for Pre4:
there is no migration promise for experimental Pre3 assets. Completion and
InterruptPolicy are removed, and normalized timing is expressed as
Activation-scoped ActionProgress.

## StateGraph Runtime and Host

One Actor needs one component and one asset:

```text
Actor GameObject
├─ CoCoStateGraphHost        required
│  └─ StateGraphAsset       required
└─ Operator scripts          optional; explicit Host list arrives in Pre5
```

Runtime, Clock, Inbox, Router, Logic, Condition, and Memory are not components.
The Host does not scan old Controllers, Context providers, children, or the
scene. Multiple Hosts may share the immutable compiled graph, while each owns
its StateLogic/Condition instances, double Memory banks, active leaves, Clock,
Inbox, staged Tick, and latched Fault.

`Start` chooses initial leaves but runs no callback. On each Layer's first Tick,
the new path runs optional Enter parent-to-child and mandatory Update
root-to-leaf. A leaf Update can request zero or more predeclared outgoing
Transitions; after all Updates, the Runtime evaluates windows and Conditions
and chooses the unique highest Priority. Transition endpoints are leaves and
outgoing Priorities of one source must be unique.

The source path remains effective through its Transition Tick and may run
Update plus Exit. A committed target runs Enter plus Update on the next Tick.
Enter and Exit are optional phases, Update is always present, and each Layer has
at most one winner per Tick. There is no Completion state or implicit exit at
ActionProgress `1`.

Within one Activation, ActionProgress must be finite and monotonically
non-decreasing; repeating a value is a valid stall. A decrease cancels the
candidate Tick, preserves the last committed authority, and latches Fault.
Transactional rollback restores that authority and never authorizes progress to
move backwards.

Layer and path depth give Operation writes fixed rank: a higher Layer overrides
a lower Layer and a child overrides its parent. Continuous Sections compose by
field; a Discrete Section consumes a sequence only for its final winner.
Operation Finalize creates a single-use staged Tick without changing authority.
Pre5 must successfully commit Context before new path, memory, Clock, sequence,
or EventOutbox state becomes visible.

See [StateGraph Runtime and Host](Docs/StateGraphRuntime.md) for Transition
windows, self-loops, rollback, lifecycle, Fault, and event-routing semantics.

## OperationFrame Sections

An Operator declares the execution data it requires through one or more
read-only OperationFrame Section interfaces. StateGraph must provide the
deduplicated union of all required Sections.

- A Section interface may extend only the framework marker; Section-to-Section
  inheritance is rejected. Additional capability is expressed by composition.
- Requiring the same interface identity more than once produces one layout
  entry.
- Two interfaces with equal fields but different identities remain different
  Sections.
- A Section is an execution promise for this Tick, not Actor state and not a
  mutable callback surface.
- Discrete execution remains structured data, including explicit enabled,
  activation, and sequence semantics. StateGraph does not create a parallel
  command queue.
- Layout, descriptors, bindings, priorities, and reducers are fixed before
  Running. Runtime reflection, string-key lookup, and steady-state allocation
  are forbidden from the Tick hot path.

Pre3 records each provided Section's complete immutable Shape: total size plus
every field's dense index, ordinal name, unmanaged type, byte offset, and size.
Catalog and Registry construction share that Shape validator, and runtime binding
must compare the complete Shape rather than treating a fingerprint as proof.

Pre4 writes with fixed Layer/path composition ranks and finalizes an
OperationFrame candidate without consuming Sequence or LastTick:

```text
TryBegin -> Write -> TryFinalize -> FinalizedFrame -> Commit / Cancel
```

Pre2 provides the Section contract and explicit test-layout path, Pre3 compiles
the automatic Graph provides manifest, and Pre4 produces the finalized staged
frame. Pre5 owns the production Operator runtime and accepts it only after the
corresponding Context commit succeeds.

## ContextFrame and Restore

`ContextFrame` is the committed logical state of one Actor, not a world snapshot
and not a Unity scene-object graph. Its fixed StateBlock/Slot layout must contain
the graph, activation, transition progress, Actor values, and controllable
Operator progress needed to continue from that commit boundary, or enough data
to rebuild them deterministically.

Descriptor metadata has two independent axes:

- Projection flags independently mark a slot as **Temporal**, **Durable**, or
  both.
- Restore policy is separately one of **Stored**, **ResetToDefault**, or
  **Derived**.
- Derived slots declare their dependencies, rebuild deterministically from
  stored/default inputs during Finalize, and are not writable or a second
  authoritative value.
- A projection that includes a Derived slot must also include every transitive
  Stored/Derived dependency required to rebuild it. Reset-to-default
  dependencies are deterministic and need not be projected.

`ContextFrame` is a generation-scoped handle over an arena storage cell, not the
reusable cell itself. Retaining a live Frame prevents its cell from being
reused. Once that generation is released and the cell is reused, every older
handle remains invalid permanently; it cannot observe or operate on the new
generation.

Commit uses an explicit two-phase authority boundary:

```text
TryPrepare -> Writer -> TryFinalize -> Finalized Commit -> Commit
```

The Writer may only write authoritative Stored/Reset-to-default inputs.
Finalize rebuilds every Derived slot in deterministic dependency order on every
successful Tick, including a no-op Tick. Failed finalization abandons the
candidate and leaves the previous ContextFrame authoritative.

Restore always lands on a completed commit boundary. It does not restore an
Inbox, IntentFrame, EventAgent subscription, unpublished Event, half-executed
Operator, other Actor, or an already delivered cross-Actor consequence.

Restore must remain in the source Timeline and ClockDomain, advance the
ExecutionSequence, and start a TimelineEpoch newer than both the restored source
and the Actor's current authoritative Epoch. Pre2 validates descriptors and an
internal, same-session, exact-layout Codec spike only. That spike is not a
cross-session save format or stable wire identity. Pre6 owns Temporal storage and rewind;
Pre13 owns durable save documents, StableEntityId-to-runtime resolution,
migration, containers, world facts, and spawned-entity reconstruction.

## Actor Mailbox Rules

Gameplay messages use one atomic value:

```text
CoCoEventPacket<TEvent> = CoCoActorEventEnvelope + immutable typed payload
```

A graph with declarations owns one EventInbox and all its Event types belong to
one EventDomain; a graph with no declaration creates neither Inbox nor Router.
Each EventDomain lazily owns one internal Router, separate from ClockDomain.
Actor-local input goes straight through its Host Gateway, while cross-Actor
Targeted messages route by current GraphInstanceId. A broadcast reaches only
Actors that declared the matching Event-to-Intent Adapter and does not return
to its source by default.

When one Event projects through several declared Adapters, they execute in the
Asset declaration-list order retained by the compiled manifest. The project
binding Provider supplies exact implementations but cannot change that order.

An Inbox can enter Running only while it is bound to a live Intent Runtime whose
bindings are frozen. Its typed lanes must match that Runtime's deduplicated
Adapter manifest exactly by EventDomain, EventType, and payload type; each lane
capacity must not exceed the minimum projection capacity declared by its
matching Adapters. Each GraphRuntimeInstance owns its own reducer instances;
reducers are never shared as mutable state between Actors.

The bound Intent Runtime must be idle while an Inbox is attached, started,
sealed for a Tick, suspended, or resumed. This prevents a batch sealed after
collection begins from becoming visible to the current IntentFrame.
Reducer state is checkpointed for Freeze and rolled back with the partial Frame
when reduction fails. Inbox Stop/Dispose requested by a user callback is deferred
until that callback exits, aborts the current collection, and cannot publish a
contribution from an invalidated sealed batch.

Inbox storage is fixed-capacity and double-buffered. Router callbacks may only
validate, deduplicate, route, and enqueue. At Step start, the visible batch is
sealed; messages arriving during the Step are visible no earlier than the next
accepted Tick. A message projects into at most one IntentFrame. Any meaning that
must persist is committed as ContextFrame state.

- Suspend keeps Router registration and may accumulate messages only within the
  fixed capacity.
- Rewind and Restore reject new gameplay messages and record diagnostics.
- Reliable overflow latches Host Fault at a safe boundary; Fault rejects new
  gameplay input and normal Resume. Unreliable overflow rejects the newest
  message and increments diagnostics.
- Stop and Dispose clear queued messages and deduplication state.
- Beginning a new Intent collection, cancellation, Timeline reset, and Dispose
  invalidate any previously readable IntentFrame. Source, Adapter, or Reducer
  exceptions cancel the collection before propagating; user callbacks cannot
  re-enter collection/freeze operations.
- Cancellation rolls back the Inbox projection claim and forbids beginning the
  same Tick again.
- Disposing a bound Intent Runtime stops and clears a Running Inbox. A Created
  Inbox is only unbound so that a replacement Runtime may be attached.
- Presentation-only audio, VFX, and logging events may continue using the normal
  EventBus without entering a gameplay Inbox.

Host startup registers only after every binding check succeeds; Stop and Dispose
unregister first. The final Host leaving a Domain releases its internal
EventAgent subscription. Pre4 is ingress-only. Outbox publication through the
Host outbound seam is forbidden until Pre5 successfully commits Context.

Legal Runtime-instance lifecycle edges are `Created -> Running`,
`Running <-> Suspended`, `Running/Suspended -> Stopped`, and
`Created/Stopped -> Disposed`. `Created` cannot Stop, and Host public
`TryDispose` accepts only `Created` or `Stopped`. Runtime `Dispose()` and Unity
destruction force live cleanup internally through `Stopped`; neither synthesizes
Exit. Starting a stopped Host allocates a fresh Runtime instance. Lifecycle
calls cannot re-enter startup or an advancing Tick; Unity destruction during
either path prevents publication or cancels the unresolved candidate before
authority changes.

## Commit and Time Boundaries

- Every accepted `CoCoTickFrame` has a finite positive delta.
- Actor TimeScale is also finite and positive. Pause and Suspend produce no
  Tick, no Intent sampling, and no new Frame.
- Unity Update/FixedUpdate accepts at most one CoCoTick per frame; each Manual
  call is one independent Tick, without accumulator or catch-up.
- Rewind does not use a negative delta. Pre6 restores an earlier ContextFrame,
  establishes a new TimelineEpoch, and then resumes positive forward Steps.
- StateGraph reads only the current IntentFrame and Previous ContextFrame. It
  cannot observe an Outcome produced during the current Tick.
- ContextFrame commit is the single externally observable gameplay boundary.
- Commit failure, cancellation, Restore, or Rewind publishes no Outbox Event,
  consumes no final EventSequence, and creates no cross-Actor side effect.

Production Outcome aggregation, ContextFrame commit, and EventOutbox publication
are Pre5 responsibilities. Pre2 freezes and tests their protocol with pure
contract harnesses only.

## Repository and Package Boundary

The 0.3.9 CCS Runtime remains temporarily for compilation and historical
regression evidence. Its mutable Context providers, MonoBehaviour states,
Unity-callback scheduling, and current module APIs are not 0.4 contracts or a
migration layer. Existing 0.3.9 projects should stay pinned to a 0.3.9 revision.

```text
Runtime/Core/Contracts   engine-independent 0.4 contracts
Runtime/Core/StateFlow   engine-independent 0.4 Frame, Section, Intent, and Mailbox contracts
Runtime/Core/StateGraph  engine-independent compiler, runtime, clock, and staged Tick
Runtime/Core/StateGraphAuthoring  Unity StateGraph Asset, snapshot, and compilation cache
Runtime/StateGraphHost   Unity Host plus internal Gateway/Router integration
Runtime/Core/*.cs        transitional 0.3.9 runtime plus later-Pre integration
Runtime/Gameplay         transitional gameplay implementations
Runtime/Modules          transitional presentation and service modules
Editor/StateGraph        Editor-only identity operations and diagnostic navigation
Editor                   dependency/setup and transitional module tooling
Tests                    contract, architecture, and transition regressions
```

Core contract and State Flow surfaces must not depend on Gameplay,
presentation modules, Editor code, project code, Animator, Playables, a network
framework, or a persistence backend. StateLogic and Layer APIs must expose no
EventBus, EventAgent, EventEnvelope, EventRouter, or EventInbox dependency.

For registered StateGraph author code, Editor Analyze and Player build preflight
walk the complete resolved assembly dependency closure. Every reachable custom
assembly must be an engine-independent asmdef; forbidden or unprovable
dependencies fail the build-time gate.

Pre1 remains the historical identity, time, lifecycle, diagnostic, and pure
StateLogic contract release. Where its proposed Context-driven flow conflicts
with this document, the Pre2 State Flow model is authoritative.

## Deferred 0.4 Work

- **Pre5**: explicit Host Operator references, Operator bindings/execution,
  claims, Outcome aggregation, ContextFrame commit, and committed EventOutbox
  publication.
- **Pre6**: Temporal Ring Buffer, Restore, rewind, and new TimelineEpoch creation.
- **Pre11**: Playable-based Animation V2, animation Operator contracts, combo
  timing, and root-motion ownership.
- **Pre13**: Persistence V2, durable projection, migration, containers, and
  world facts.
- **Pre15/Pre16**: replacement Samples, golden-path content, documentation, and
  full cross-module performance/lifecycle certification.

## Dependencies

The dependency set remains unchanged in Pre4 because transitional 0.3.9 modules
still compile against it.

| Package | Version | Current owner |
|---|---:|---|
| Addressables | 2.9.1 | Map and UI transitional workflows |
| Input System | 1.18.0 | Input module |
| Newtonsoft Json | 3.2.2 | Persistence transitional module |
| Cinemachine | 3.1.6 | Camera transitional module |
| AI Navigation | 2.0.0 | Character and Enemy navigation |
| Mathematics | 1.3.3 | Enemy/spline assemblies |
| Splines | 2.6.0 | Enemy spline support |

Dependency pruning belongs to the Pre that replaces each owning module.

## Installation and Validation

Install through Unity Package Manager with an explicit Git revision, or place
the package in a Unity project's `Packages/` directory. Do not use a moving
development branch as a production dependency.

This repository is a UPM package rather than a complete Unity project. Release
validation therefore requires a clean Unity 6 host project, the Core, State
Flow, StateGraph Runtime EditMode and Host PlayMode suites, relevant
IL2CPP/High-Stripping checks, and Unity Package Validation Suite.
`CoCoFlow/Setup/Setup Assistant` remains a
dependency/support-define tool; it does not install project content.

## Documentation

- [State Flow / Network Boundary](Docs/ContextNetworkBoundary.md)
- [StateGraph Asset and Compiler](Docs/StateGraphCompiler.md)
- [StateGraph Runtime and Host](Docs/StateGraphRuntime.md)
- [Module: Animation](Docs/Module-Animation.md)
- [Module: Camera](Docs/Module-Camera.md)
- [Module: Persistence](Docs/Module-Persistence.md)
- [Changelog](CHANGELOG.md)

Module documents describe transitional implementations unless they explicitly
mark a 0.4 contract as authoritative.

## Versioning

- Integration branch: `dev/0.4.0`
- Work branches: `pre/NN-topic`
- UPM prereleases: `0.4.0-pre.N`
- 0.3.9 remains the historical runtime line; 0.4 has no automatic migration or
  dual-runtime promise.

## License

MIT
