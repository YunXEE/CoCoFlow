# CoCoFlow

[English](README.md) | [简体中文](README.zh-CN.md)

> **Version**: 0.4.0-pre.2 · **Unity**: 6000+
>
> Pre2 freezes the State Flow Frame, OperationFrame Section, ContextFrame
> restore, and Actor Mailbox contracts. It is not the finished 0.4 runtime,
> StateGraph authoring workflow, rewind system, or Persistence V2.

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
EventPacket<TEvent>
  -> CoCoEventBus
  -> EventRouter for one EventDomain
  -> target Actor EventInbox
  -> next accepted CoCoTick
  -> Event-to-Intent Adapter
  -> IntentFrame
```

The EventBus is the road, the EventEnvelope is the shipping label, the
EventRouter is the sorting centre, the Actor EventInbox is the mailbox, and the
Event-to-Intent Adapter translates a delivered message into this Actor's
intent. StateGraph never reads a raw EventBus callback, envelope, Router, or
mailbox.

## Frozen Vocabulary

| Term | Meaning |
|---|---|
| `IntentFrame` | The immutable input for one CoCoTick. It is sampled and arbitrated once, is not persisted, and is not part of rewind history. |
| `OperationFrame` | The complete execution guide produced by StateGraph. It is the only Frame that exposes public Section contracts. |
| `ContextFrame` | The complete committed logical state of one Actor at a Tick boundary. It is the authority for restore and for later Temporal/Durable projections. |
| `EventInbox` | Pending cross-Object gameplay input for one GraphRuntimeInstance. It is not fact storage. |
| `EventOutbox` | Cross-Object output candidates produced during Operator execution. They are published only after ContextFrame commit succeeds. |
| `EventAgent` | A helper for EventBus subscription lifetime only. It does not route, queue, own, or persist messages. |

An `IntentFrame`, an `OperationFrame`, and a `ContextFrame` are not aliases and
cannot substitute for one another. Inbox contents, raw envelopes, the current
IntentFrame, and unpublished Outbox candidates never become ContextFrame state.

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

Pre2 provides the contract and explicit test-layout path. Pre3 owns automatic
Graph requirement aggregation and compiled layout generation; Pre5 owns the
production Operator runtime.

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
  restored inputs, and are not a second authoritative value.

Restore always lands on a completed commit boundary. It does not restore an
Inbox, IntentFrame, EventAgent subscription, unpublished Event, half-executed
Operator, other Actor, or an already delivered cross-Actor consequence.

Pre2 validates descriptors and a Codec spike. Pre6 owns Temporal storage and
rewind; Pre13 owns durable save documents, migration, containers, world facts,
and spawned-entity reconstruction.

## Actor Mailbox Rules

Gameplay messages use one atomic value:

```text
EventPacket<TEvent> = EventEnvelope + immutable typed payload
```

Each GraphRuntimeInstance owns one EventInbox. Each EventDomain owns one central
Router; EventDomain and ClockDomain are separate identities. Targeted messages
route by the current GraphInstanceId. A broadcast reaches only Actors that
declared the matching Event-to-Intent Adapter and does not return to its source
by default.

Inbox storage is fixed-capacity and double-buffered. Router callbacks may only
validate, deduplicate, route, and enqueue. At Step start, the visible batch is
sealed; messages arriving during the Step are visible no earlier than the next
accepted Tick. A message projects into at most one IntentFrame. Any meaning that
must persist is committed as ContextFrame state.

- Suspend may accumulate messages only within the fixed capacity.
- Rewind and Restore reject new gameplay messages and record diagnostics.
- Reliable overflow reports a Host-fault outcome; unreliable overflow rejects
  the newest message and increments diagnostics.
- Stop and Dispose clear queued messages and deduplication state.
- Presentation-only audio, VFX, and logging events may continue using the normal
  EventBus without entering a gameplay Inbox.

The central Router, real EventBus subscription lifecycle, StableEntityId
resolution, Host fault transition, and ghost-subscription tests belong to Pre4.

## Commit and Time Boundaries

- Every accepted `CoCoTickFrame` has a finite positive delta.
- Pause and Suspend produce no Tick, no Intent sampling, and no new Frame.
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
Runtime/Core/*.cs        transitional 0.3.9 runtime plus later-Pre integration
Runtime/Gameplay         transitional gameplay implementations
Runtime/Modules          transitional presentation and service modules
Editor                   dependency/setup and transitional module tooling
Tests                    contract, architecture, and transition regressions
```

Core contract and State Flow surfaces must not depend on Gameplay,
presentation modules, Editor code, project code, Animator, Playables, a network
framework, or a persistence backend. StateLogic and Layer APIs must expose no
EventBus, EventAgent, EventEnvelope, EventRouter, or EventInbox dependency.

Pre1 remains the historical identity, time, lifecycle, diagnostic, and pure
StateLogic contract release. Where its proposed Context-driven flow conflicts
with this document, the Pre2 State Flow model is authoritative.

## Deferred 0.4 Work

- **Pre3**: StateGraph Asset/Compiler, Intent requirements, Graph Operation
  Provides, ContextFrame state requirements, and compiled layout generation.
- **Pre4**: `CoCoStateGraphHost`, Clock/Driver integration, EventRouter,
  EventAgent subscriptions, Actor Inbox registration, and lifecycle integration.
- **Pre5**: Operator bindings, claims, Outcome aggregation, ContextFrame commit,
  and EventOutbox publication.
- **Pre6**: Temporal Ring Buffer, rewind/resume, and TimelineEpoch switching.
- **Pre11**: Playable-based Animation V2, animation Operator contracts, combo
  timing, and root-motion ownership.
- **Pre13**: Persistence V2, durable projection, migration, containers, and
  world facts.
- **Pre15/Pre16**: replacement Samples, golden-path content, documentation, and
  full cross-module performance/lifecycle certification.

## Dependencies

The dependency set remains unchanged in Pre2 because transitional 0.3.9 modules
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
validation therefore requires a clean Unity 6 host project, the Core and State
Flow EditMode suites, relevant PlayMode/AOT checks, and Unity Package Validation
Suite. `CoCoFlow/Setup/Setup Assistant` remains a dependency/support-define tool;
it does not install project content.

## Documentation

- [State Flow / Network Boundary](Docs/ContextNetworkBoundary.md)
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
