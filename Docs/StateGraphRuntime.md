# CoCoFlow StateGraph Runtime and Host

> Contract status: `0.4.0-pre.7` · Updated 2026-07-22

Pre6 extends the Pre5 composite Actor commit with Host-owned Temporal projection
history. It adds authority-neutral Preview, a single formal Restore into a new
TimelineEpoch, and one explicit Unity binding for Preview, Confirm, Cancel, and
world Correction. StateGraph still executes only forward positive-delta Ticks.
Pre7 makes the Host's scene-instance references explicit, adds a committed-only
Editor debugger snapshot, and permits one internal positive-delta debug Tick
from a healthy Suspended Host without turning that operation into rewind.

## Unity assembly model

The required scene surface is intentionally small:

```text
Actor GameObject
├─ CoCoStateGraphHost        required, exactly one
│  └─ StateGraphAsset       required
├─ Intent Source scripts     explicit ordered Host references as required
├─ Event-to-Intent Adapters  explicit Host references in declaration order
├─ Operator scripts          optional; referenced by the Host in explicit order
├─ Actor Context binding     required only when Actor-owned Slots exist
└─ Context Restore binding   required when Temporal history is enabled
```

`CoCoStateGraphHost` is the only public `MonoBehaviour` introduced by this
runtime and disallows duplicate instances on the same GameObject. Runtime,
Clock, Inbox, Router, StateLogic, Condition, ActivationMemory, and Factory
objects are ordinary C# objects, not components or extra Inspector assets.

The Host exposes lifecycle control, Manual Step, read-only authority inspection,
and the Temporal Begin/Preview/Confirm/Cancel/Correction surface. Driver mode,
AutoStart, Actor TimeScale, history capacity, and diagnostic capacity are
settings on that same Host, not reasons to add another component. The custom
Inspector validates the Asset, explicit component references, project runtime
bindings, EventDomain, Driver, and Temporal configuration. It may suggest
compatible components inside the Host boundary, but never assigns or saves a
suggestion until the user confirms it. All configuration is read-only while the
Host is Running.

The Runtime never scans old `CoCoStateController` instances, Context providers,
children, or the scene. Intent Sources, Event-to-Intent Adapters, and Operators
are serialized as explicit ordered Host references. Runtime validation rejects
null or destroyed entries, incompatible types, duplicate references, duplicate
stable IDs, entries outside the Host transform scope, and entries hidden behind
a nested Host. Optional capability is represented by an explicit no-op binding
where its contract requires one, not by runtime discovery.

The Actor Context binding is also one explicit Host reference. It must implement
the binding contract, stay inside the Host's transform scope without crossing a
nested Host, and cover every Actor-owned writable Slot exactly once. It must be
absent when the Layout contains no Actor-owned Slot.

The Context Restore binding is one separate explicit Host reference and must
implement `ICoCoContextRestoreBinding`. It is required when Temporal history
is enabled with capacity of at least two, must remain inside the same Host
boundary, and is never discovered through scene scanning. One component may
implement both Actor capture and Context restore contracts and be assigned to
both fields.

## Shared graph, isolated Actor state

Hosts may share one immutable compiled result from the same Asset. Everything
that can change at runtime remains instance-owned: StateLogic and Condition
instances, double-buffered State memory, the active leaf of every Layer, Clock,
Inbox, pending staged Tick, Temporal Ring/cursor, and the latched Fault.

Project executable bindings are installed once before Host startup through an
immutable, AOT-safe registration entry point. The Host obtains that registration
without another component or serialized binding asset. Binding coverage must
match the compiled State, Condition, Memory, Intent Source, Event Adapter, and
Context Slot requirements exactly. The deduplicated Operator requirements must
also exactly cover the compiled Graph Operation-provides manifest. Missing,
extra, duplicate, or type-incompatible bindings leave the Host in `Created`; no
callback, Tick, or Router registration occurs.

The Project Provider and the Host own different halves of that setup. The
Provider remains authoritative for the frozen descriptor Catalog, State and
Condition factories, generic Intent and Adapter bindings, Operation/Context
types, Codecs, Layout defaults, and AOT-safe construction. The Host owns the
user-confirmed scene component instances and their order. A Host reference
cannot replace Provider type authority, and a Provider cannot discover,
substitute, persist, or reorder scene instances on the user's behalf.

Transaction preflight runs before Clock creation and before
`CoCoStateGraphRuntime.TryCreate`. It classifies every non-Derived Context Slot
exactly once as Graph state, Graph auxiliary value, canonical Claim, Operator
Outcome, or Actor binding output; existing rebuilders exclusively own Derived
Slots. Invalid producer, Operator, Claim, Actor-binding, or Outbox setup is
therefore rejected before Logic, Condition, Memory create/reset/fingerprint, or
Graph-capture callbacks can run. A Transaction created for a later Runtime
create/start/initial-validation failure is disposed before the Host returns.

The Project Provider supplies the actual Context default, Codec, and semantic
fingerprint for each binding. The fingerprint is the Provider's declaration of
semantic compatibility with the Manifest, not a canonical hash recomputed from
the supplied default value. After Runtime start but before Host publication, an
initial Graph-state capture verifies that Graph records match those Layout
defaults without creating Revision 0.

The Asset declaration list is the authoritative Event Adapter execution order.
The compiled manifest preserves that order; the project binding Provider may
only satisfy the declarations and cannot reorder their runtime semantics.

Each State factory also supplies AOT-safe Memory create, copy, reset, and
fingerprint operations. Memory references are valid only while the owning State
callback is running; mutation retained past that boundary is detected and faults
the staged Tick instead of changing committed state behind the transaction. A
Graph-state capture binding is likewise callback-scoped: Runtime rechecks both
candidate and committed fingerprints immediately after capture and before the
first Operator callback, and rechecks committed memory before Host publication.

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

Conditions are pure reads over the frozen IntentFrame, previous Context read view,
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

Pre5 resolves that staged Tick in one fixed transaction:

```text
Preview -> Context Prepare -> Intent Collect/Freeze -> Graph Stage + Trace
  -> Graph-owned State/Value Capture -> Claim Arbitration/Claim Capture
  -> Operators/Outcomes/Outbox -> Actor-owned Capture -> Derived Finalize
  -> Composite Preflight -> Temporal Projection Capture
  -> no-fail Commit + Temporal Publish -> complete Outbox Publish
```

The first Tick receives a `CoCoContextFrameReadView` backed by the layout's
actual defaults; there is no synthetic Tick 0 or Revision 0 Frame. The first
successful commit creates Revision 1. An Operator writer is bound to its
Operator ID, transaction token, and exact non-Derived Operator-owned Slot
allowlist, and expires as soon as its callback returns. A later Operator still
reads only the previously committed Context, never this Tick's candidate.

Claims are calculated for every Operator before the first real callback. A
discrete claim binds `Enabled + ActivationId`; all claims of one Operator win or
lose together. Arbitration uses descending Priority, Host list order, then
stable Operator ID. A loser produces `ClaimDenied`, runs no callback, and leaves
no Context or Outbox write. Ordinary competition therefore does not fault the
Tick. Every Claim declaration identifies one Graph-owned Claim State Slot;
competitors for one Claim ID must identify the same Slot, and arbitration writes
its canonical owner once. Claim authority is released on Activation change,
Exit, Stop, Dispose, or the claim's explicit Suspend policy.

Graph capture completes before arbitration and any real Operator callback.
Operators produce only their declared Operator-owned Slots. The single Actor
binding captures Actor-owned Slots after Operators, but like every Operator it
reads only Previous Context and cannot see this Tick's candidate. Derived
rebuilding begins only after all direct producers finish.

When history is enabled, Temporal projection encoding reads the finalized
Context candidate after Derived rebuild but before authority changes. A Codec
failure cancels the complete Tick, retains the old Ring and logical authority,
and consumes no final sequence. If an Operator already changed Unity, the normal
D12 correction rule still applies.

Only the composite no-fail barrier publishes Context authority, operation
sequence, path, memory, activation, Clock, committed claims, a contiguous final
EventSequence range, the resolved Intent Tick, and the prepared Ring entry. That
barrier contains no callback, allocation, capacity request, or fallible mutation.
Any earlier failure cancels all candidates and retains the previous authority
without final sequence consumption. Rollback cannot make `ActionProgress` move
backwards within an Activation.

The committed `ContextFrame` remains the sole retainable complete Actor commit
record. Normal callers may still `Retain` and `Release` its generation-scoped
handle. Temporal history does not: each Ring entry stores only exact-layout
`Temporal + Stored` bytes and immutable source metadata. Reset and non-Temporal
Stored values use Layout defaults during restore, while Derived values rebuild
from their closed dependency graph. Live Graph banks/path/activation, Clock, and
Claim state remain commit-time mirrors, never independent authority.

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
  Memory, path, Clock, or Context authority is swapped. If a real Operator has
  already touched Unity state, the old Context still remains authoritative but
  the Host faults with `RequiresWorldCorrection`; CoCoFlow does not fabricate a
  Unity-world rollback.

Pre7 also exposes one internal Editor-only debug seam for a healthy Suspended
Host. It accepts one explicit finite positive delta under Update, FixedUpdate,
or Manual driving, executes exactly one ordinary forward Tick through the same
Intent, Graph, Operator, Context, Temporal, Trace, and Outbox boundaries, and
returns a healthy success to Suspended. It does not change the configured Driver
or run an accumulator/catch-up loop. Temporal Preview, an unresolved Tick,
Fault, or required world Correction rejects the request. If the Tick fails, the
real lifecycle, Fault, and correction state remain visible; the Host does not
fabricate a successful suspension.

Callback or Condition exceptions, failed Operation finalization, and reliable
Inbox overflow cancel the candidate and latch Fault at a safe boundary. A
faulted Host rejects normal Resume and new gameplay input. Recovery requires
Stop followed by a fresh instance, except for the narrow Pre6 world-correction
path described below. CoCoFlow does not expose a general `ClearFault()` API.

## Temporal mode and public orchestration

`CoCoTemporalMode` is orthogonal to the Runtime lifecycle; it does not add a
sixth lifecycle state:

- `Disabled`: configured history capacity is zero;
- `Ready`: history is enabled and normal forward Ticks may commit entries;
- `Previewing`: normal Tick and gameplay ingress are blocked while the
  non-authoritative cursor selects history.

Capacity zero does not require a Restore binding and invalid assignments are
ignored. One valid in-boundary binding may still be retained while Temporal Mode
is `Disabled`, solely for `TryCorrectWorld` after a dirty non-Temporal Tick
failure.

Capacity is fixed before Running and counts entries, including the current
authority. Zero disables history; enabled history requires at least two entries,
and capacity one is rejected during startup. The first successful Context commit
makes Count 1. A full Ring overwrites the oldest entry, and
Stop/Dispose/destruction releases the Ring, cursor, scratch storage, and callback
tokens. No mutable Frame, payload, arena handle, or long-lived selection token is
exposed.

The Host API is synchronous:

```text
TemporalState
TryBeginTemporalPreview
TryPreviewTemporal(historyDepth)
TryConfirmTemporalRestore
TryCancelTemporalPreview
TryCorrectWorld
```

Depth zero means the current authority; depth one means the preceding recorded
commit. Begin requires a healthy Running Host and at least one older entry.
Moving the cursor decodes Stored bytes over Layout defaults, rebuilds Derived,
and invokes the single Restore binding with `Preview`. The cursor changes only
after that call succeeds. Preview never invokes State Enter/Exit, Update,
Condition, Transition, Operator, Actor capture, Event, Trace, or sequence work.

Cancel invokes the same binding with `Cancel` to reapply current authority only
after at least one Preview projection succeeded. Cancelling directly after Begin
skips the binding. Neither path performs a logical restore or switches Epoch.
Confirm validates and prepares the complete Context, Graph Path/Memory, Clock,
and Claim candidate, then invokes the binding once with `Confirm`. After that
succeeds, a no-fail barrier swaps all logical authority, discards the abandoned
future, and records the new-Epoch restore commit as the new history branch head.
The next accepted positive-delta Tick resumes normal StateGraph execution.

The callback receives only a token-scoped `CoCoContextRestoreReader`; retaining
it beyond the synchronous call yields an invalid reader. A preflight failure
before any callback leaves a clean session healthy; if a previous Preview
projection remains active, the same failure requires Correction. Once a binding
callback starts, a refusal, exception, destroyed component, re-entry, or possible
partial Unity mutation does not move the cursor or logical authority. The Host
latches Fault and `RequiresWorldCorrection`. `TryCorrectWorld` invokes the same
binding with `Correction` against the last logical authority and clears only the
matching recoverable fault after successful projection.

## Committed debugger snapshot and Trace

The Pre7 Runtime Debugger reads an internal immutable Host snapshot copied from
the latest committed authority boundary. It is a current point-in-time view of
explicitly copied identity and diagnostic state, including Host/Graph identity,
lifecycle and Fault, committed Context/Tick/Clock/Epoch information, and each
Layer's committed active path and Transition result. It never exposes a staged
candidate, mutable Runtime collection, retained Context handle, Context payload,
Inbox, Envelope, Unity object graph, or private reflected field.

Snapshot and Trace are deliberately separate:

- the snapshot answers what is committed now and is replaced only by another
  successfully captured committed point;
- Trace answers what identity-only events happened recently and retains a
  bounded ordered history when its configured capacity is greater than zero.

Trace capacity defaults to zero, creates no buffer in that mode, and is fixed
before Running. The Editor cannot resize it on a live Host. A failed transaction
does not change committed snapshot authority and its Trace cannot contain
Context commit, final sequence, or publication entries.

The suspended debug step is not an authority-neutral debugger preview. Because
it is one normal positive-delta Tick, it may advance Context/Clock, append
Temporal and Trace entries, execute Operators, and publish a successfully
committed EventOutbox before returning to Suspended.

## Actor event boundary

The Host is the Actor's single gameplay-event boundary. StateLogic cannot
publish an immediately visible gameplay event; Operators may only append typed,
preallocated EventOutbox candidates owned by their current transaction token.

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

Suspend keeps Router registration and bounded accumulation. Beginning Temporal
Preview instead clears queued messages, any sealed batch, and deduplication
state immediately. New gameplay packets during Preview are dropped and counted.
Cancel keeps the existing Epoch but never resurrects the cleared backlog;
Confirm invalidates all old-Epoch packet and dedup state before accepting new
input for the new Epoch. Fault rejects new gameplay input, and reliable overflow
latches Fault at the next safe boundary.
Outbox finalization validates capacity and metadata without consuming sequence.
After commit, all event types of one GraphInstance/Epoch share one contiguous
EventSequence range, published in Host Operator order and then append order.
Subscriber exceptions remain isolated. An infrastructure exception records a
Fault but publication continues for the remaining committed packets; sequences
are neither reclaimed nor automatically retried. Destroy, Stop, or Dispose
requested during publication is deferred until the committed list completes.

Runtime Trace is an optional fixed-capacity immutable ring. Capacity zero creates
no buffer, and Running never resizes it. Accepted Transition Candidates are emitted in compiled order after
their source/window/conditions pass, and the Winner is emitted again with an
explicit Winner role. A value-only Frame reference records Frame identity, exact
Layout identity/version/schema hash, Revision, and whether a committed Frame
exists. The first Tick therefore references exact Previous Layout defaults with
`HasCommittedFrame == false`, never a fictional Revision 0.

Successful ordering is Tick inputs, Candidates, Winner, Operation Sections,
Operator Outcomes, Context commit, ActivePath, EventSequence, then publication
or publish diagnostics. A failed transaction ends with Cancelled and cannot
contain commit, sequence, or published entries. Trace never stores payloads,
Unity objects, mutable Frames, Router/Inbox state, or diagnostic strings, and it
does not retain Context; a caller that needs long-lived Context access must
explicitly retain and release it. Filters may constrain entries by State ID or
Transition ID without changing the stored evidence.

## Restore authority barrier

Formal Confirm retains the Pre5 single-use validation/prepare discipline:

```text
validate source and selection generation
  -> materialize Stored + Default + Derived Context candidate
  -> prepare Graph Path/Memory + Clock + Claim + branch-head entry
  -> ICoCoContextRestoreBinding.TryApply(Confirm)
  -> no-fail authority swap + future discard + mailbox Epoch switch
```

The source TimelineId and ClockDomainId remain unchanged. The target
TimelineEpoch is strictly newer than both source and current Epoch, and
ExecutionSequence strictly advances. The target Tick and TimelinePosition come
from the historical source; Revision advances from the current authority and
Origin records the selected source identity.

All compatibility, overflow, graph-path, Memory, Clock, Claim, history capacity,
mailbox, and token validation completes before the Unity binding. Once that
binding succeeds, the remaining authority exchange and branch-head publication
cannot fail. Restore itself invokes no State, Condition, Transition, Operator,
Actor capture, Event, Trace, Outbox, OperationSequence, or EventSequence work.

This is same-session, same-GraphInstance, exact-layout restoration. Temporal
payloads are not durable documents or stable wire identities. They do not restore
Inbox contents, IntentFrame, EventAgent subscription, unpublished Outbox,
half-executed Operator work, another Actor, or already delivered cross-Actor
consequences.

## Explicitly deferred

- **Pre11**: Animator/Playable/SMB replacement and presentation reverse mapping.
- **Pre13**: durable persistence and migration.

Pre7 does not add cross-Layer calls, queries, signals, or Transitions; an
arbitrary state-change API; a network Driver; persistence; a production Sample;
or a migration runtime for the retained 0.3.9 implementation.

The serialized StateGraph Schema remains version 1 because it had not been
formally delivered with production assets. Pre4 redefines that prerelease v1 in
place—removing Completion/Interrupt fields and changing the normalized window
to ActionProgress—and provides no migration promise for experimental Pre3
assets.
