# CoCoFlow StateGraph Runtime and Host

> Contract status: `0.4.0-pre.4` · Updated 2026-07-17

Pre4 adds the engine-independent StateGraph Runtime and the single Unity
assembly point for one Actor. It deliberately stops at a staged Tick: Operator
execution, Outcome collection, ContextFrame commit, and committed EventOutbox
publication remain Pre5 responsibilities.

## Unity assembly model

The required scene surface is intentionally small:

```text
Actor GameObject
├─ CoCoStateGraphHost        required, exactly one
│  └─ StateGraphAsset       required
└─ Operator scripts          optional; explicit Host list arrives in Pre5
```

`CoCoStateGraphHost` is the only public `MonoBehaviour` introduced by this
runtime and disallows duplicate instances on the same GameObject. Runtime,
Clock, Inbox, Router, StateLogic, Condition, ActivationMemory, and Factory
objects are ordinary C# objects, not components or extra Inspector assets.

The Host exposes lifecycle control, Manual Step, and read-only Lifecycle,
Fault, GraphInstanceId, and committed ActivePath inspection. Driver mode,
AutoStart, Actor TimeScale, and diagnostic capacity are settings on that same
Host, not reasons to add another component. The custom Inspector validates the
Asset, project runtime binding, EventDomain, and Driver configuration without
auto-adding anything.

The Host never scans old `CoCoStateController` instances, Context providers,
children, or the scene. Pre4 neither creates placeholder Operators nor executes
an Operator. Pre5 adds an explicit Operator reference list on the Host; it does
not add discovery by component scanning.

## Shared graph, isolated Actor state

Hosts may share one immutable compiled result from the same Asset. Everything
that can change at runtime remains instance-owned: StateLogic and Condition
instances, double-buffered State memory, the active leaf of every Layer, Clock,
Inbox, pending staged Tick, and the latched Fault.

Project executable bindings are installed once before Host startup through an
immutable, AOT-safe registration entry point. The Host obtains that registration
without another component or serialized binding asset. Binding coverage must
match the compiled State, Condition, Memory, Intent Source, and Event Adapter
requirements exactly. Missing, extra, duplicate, or type-incompatible bindings
leave the Host in `Created`; no callback, Tick, or Router registration occurs.
The Asset declaration list is the authoritative Event Adapter execution order.
The compiled manifest preserves that order; the project binding Provider may
only satisfy the declarations and cannot reorder their runtime semantics.

Each State factory also supplies AOT-safe Memory create, copy, reset, and
fingerprint operations. Memory references are valid only while the owning State
callback is running; mutation retained past that boundary is detected and faults
the staged Tick instead of changing committed state behind the transaction.

## Active path and lifecycle callbacks

Each Layer stores only its authoritative active leaf. The compiled hierarchy
derives the full active path, including composite ancestors; a composite State
can be part of that path but can never be a Transition endpoint.

Starting a fresh Runtime chooses each Layer's initial leaf and records the path
that still needs to enter. `Start` itself invokes no user callback. On the first
accepted Tick the complete path therefore runs `OnEnter` from parent to child,
then its mandatory `Update` from root to leaf.

For each Layer, one Tick has this order:

1. Run the newly entered suffix's optional `OnEnter` callbacks, parent to child.
2. Run the full active path's mandatory `Update` callbacks, root to leaf.
3. Let the active leaf request zero or more predeclared Transition handles.
4. After all Updates, evaluate windows and Conditions and arbitrate by Priority.
5. When there is a winner, run the old exit suffix's optional `OnExit`
   callbacks, leaf to parent.

`Update` always runs. `OnEnter` and `OnExit` are independent optional phases,
not mutually exclusive Tick modes. A newly active path can run Enter and Update
in the same Tick; a Transition decision can make the still-effective source
path run Update and Exit in the same Tick.

The source path remains authoritative for the whole Transition Tick. A
successfully staged target becomes authoritative only after the Tick commits,
then runs Enter and Update on the next accepted Tick. There is at most one
winner per Layer and Tick; no continuous multi-hop or hidden substep exists.

A leaf self-loop is explicit reactivation. The leaf exits on the source Tick,
receives a new Activation and reset Memory when that Tick commits, then enters
on the next Tick. Its common ancestor path does not exit and re-enter.

## Declared Transition evaluation

Every Transition is authored before runtime and connects two leaves in the same
Layer. State Update cannot name an arbitrary target or call `ChangeState`; it
can only request a compiled Transition handle that is already an outgoing edge
of the active leaf. A leaf with no outgoing edge performs no Transition work.

One Update may request several handles. After Update completes, the Runtime
evaluates their declared windows and Conditions and selects the single highest
Priority eligible candidate. Priority is mandatory and must be unique among
the outgoing edges of one source leaf. The same numeric Priority may be reused
by another source or Layer because those candidates never share an arbitration
set.

Conditions are pure reads over the frozen IntentFrame, previous ContextFrame,
Tick, Config, `LocalSeconds`, and `ActionProgress`. They do not declare or write
Operation Sections. Completion and InterruptPolicy are not StateGraph concepts;
reaching progress `1` never exits a State automatically.

Timed windows are half-open `[StartInclusive, EndExclusive)`:

- `LocalSeconds` is Actor-clock time within the current leaf Activation.
- `ActionProgress` is a normalized `0..1` progress supplied for that Activation,
  allowing animation speed-up, slow-down, and presentation-driven timing to
  preserve proportional windows.

Window evaluation sweeps the interval crossed by the Tick rather than sampling
only its final point. A candidate is observable when its progress sweep satisfies
`previous < end && current >= start`, so a large positive Delta cannot silently
jump over a window. Progress must be finite and monotonically non-decreasing
within one Activation; repeating the current value is a valid stall. Any decrease,
including a value below the last committed progress, cancels the candidate Tick,
preserves the last committed authority, and latches Fault. Transactional rollback
means restoring that authority after a rejected or cancelled staged Tick; it never
permits `ActionProgress` itself to move backwards. Reaching `1` has no implicit
completion behavior.

## Layer and Operation composition

The Asset's Layer list is semantic ordering: first is lowest, last is highest.
Reordering Layers changes composition and the content fingerprint while keeping
stable Layer IDs. The compiled `DenseIndex` preserves this list order.

State callbacks write through a restricted Operation writer carrying a fixed
composition rank. A higher Layer overrides a lower Layer; within one Layer, a
deeper child overrides its parent. Invocation order cannot reverse that rank,
so leaf-to-parent Exit still leaves the child's contribution above its parent.
Within the same State and rank, the later Enter/Update/Exit write wins.
The writer accepts only Sections declared by that State's `OperationProvides`
manifest and expires when the callback returns; an escaped or undeclared write
faults and rolls back the candidate Tick.

Continuous Sections compose per field: fields that a higher-ranked State does
not write retain lower-ranked contributions. A Discrete Section allocates its
OperationSequence once, for the final winning contribution only. Declaring the
same Section on parent and child is legal and produces a non-blocking warning;
the same declaration across Layers is normal composition and produces no
warning.

OperationFrame creation uses a transactional protocol:

```text
TryBegin -> Write -> TryFinalize -> FinalizedFrame -> Commit / Cancel
```

Finalize freezes candidate bytes only. It does not consume a sequence or update
the last committed Tick. Pre4 returns a single-use staged Tick containing the
candidate active leaves, State memory, Clock, and finalized OperationFrame, and
refuses another Step while that Tick is unresolved.

Pre5 will execute Operators and Outcomes, finalize Context, and accept or cancel
that staged Tick. Only a successful Context commit atomically publishes the new
path, memory, Clock, ContextFrame, OperationSequence, and EventOutbox. Any
callback, Condition, Operation Finalize, or later Pre5 failure cancels all
candidates, leaving the previous commit authoritative with no Outbox or final
sequence consumption. Pre4 exposes no production shortcut around that barrier;
its tests use an internal-only coordinator to exercise acceptance and rollback.
That rollback cannot make `ActionProgress` move backwards within an Activation.

## Clock, lifecycle, and Fault

The lifecycle remains `Created / Running / Suspended / Stopped / Disposed`.
Fault is a latched overlay, not a sixth lifecycle value.

The legal Runtime-instance edges are `Created -> Running`,
`Running <-> Suspended`, `Running/Suspended -> Stopped`, and
`Created/Stopped -> Disposed`. `Created` cannot Stop, and Host public
`TryDispose` accepts only `Created` or `Stopped`. Runtime `Dispose()` and Unity
destruction are non-rejectable cleanup: a live instance is first torn down
through `Stopped`, then disposed, without synthesizing lifecycle callbacks.
Starting a stopped Host allocates a fresh Runtime instance rather than reviving
the stopped one.

- Delta and Actor TimeScale must be finite and greater than zero. Zero speed is
  represented by Suspend, never by a zero-delta Tick.
- `Update` and `FixedUpdate` drivers accept at most one CoCoTick per Unity frame.
  Manual calls are independent Ticks; there is no accumulator or catch-up loop.
- Suspend preserves Runtime, path, memory, Actor time, pending Enter state, and
  Inbox contents within their fixed capacities. It samples and Steps nothing.
- Stop discards the current Graph instance. Starting again creates a fresh
  instance rather than resuming the old one.
- Stop, Dispose, and GameObject destruction never synthesize a final Tick or
  call `OnExit` as cleanup.
- Host lifecycle calls cannot re-enter startup or an advancing Tick. Unity
  destruction closes ingress immediately; during startup it prevents Runtime
  publication, and during a staged Tick it cancels before any Operation,
  Memory, path, Clock, or Context authority is swapped.

Callback or Condition exceptions, failed Operation finalization, and reliable
Inbox overflow cancel the candidate and latch Fault at a safe boundary. A
faulted Host rejects normal Resume and new gameplay input. Recovery requires
Stop followed by a fresh instance, or a future Pre6 Restore into a new Epoch.

## Actor event boundary

The Host is the Actor's single gameplay-event boundary, but Pre4 exposes ingress
only. StateLogic and future Operators cannot publish an immediately visible
gameplay event.

An Actor-local event enters that Host's private Gateway and Inbox directly.
Cross-Actor Targeted and declared broadcast packets pass through one lazily
created internal Router per EventDomain. The Router accepts only atomic
`CoCoEventPacket<TEvent>` values; the legacy split `PublishWithEnvelope` path is
not part of this protocol.

All Event declarations in one graph must belong to one EventDomain. A graph
with no Event declaration creates neither Inbox nor Router. When declarations
project one Event type into several Intents, the Host creates one typed Inbox
lane and runs each declared Adapter over that sealed lane in the Asset
declaration-list order preserved by the compiled manifest. The binding Provider
cannot substitute a different order.

Host startup registers with its Router only after every other startup check has
succeeded. Stop and Dispose unregister first; the final Host leaving a Domain
releases the internal EventAgent subscription. Router callbacks only validate
and enqueue. Packets received after a Step seals its Inbox are visible no
earlier than the next accepted Tick.

Suspend keeps Router registration and bounded accumulation. Fault rejects new
gameplay input, and reliable overflow latches Fault at the next safe boundary.
Pre5 may publish a staged EventOutbox through the Host's internal outbound seam
only after the corresponding Context commit succeeds.

## Explicitly deferred

- **Pre5**: explicit Host Operator list, Operator contracts/execution, Outcome,
  Context commit, and committed EventOutbox publication.
- **Pre6**: Temporal history, Restore, rewind, and new TimelineEpoch creation.
- **Pre11**: Animator/Playable/SMB replacement and presentation reverse mapping.
- **Pre13**: durable persistence and migration.

Pre4 does not add cross-Layer calls, queries, signals, or Transitions; an
arbitrary state-change API; a network Driver; persistence; a production Sample;
or a migration runtime for the retained 0.3.9 implementation.

The serialized StateGraph Schema remains version 1 because it had not been
formally delivered with production assets. Pre4 redefines that prerelease v1 in
place—removing Completion/Interrupt fields and changing the normalized window
to ActionProgress—and provides no migration promise for experimental Pre3
assets.
