# CoCoFlow StateGraph Runtime and Host

> Runtime contract baseline: `0.4.0` · AnyState increment: `0.4.6`
>
> StateGraph Runtime and StateGraphHost are included in the Core Engine maturity
> statement. Editor inspection is described here only as current tooling.

The Runtime executes only forward, finite, positive-delta Ticks. StateGraphHost
owns the composite Actor transaction, committed Context authority, Event
ingress/egress, Clock, Trace, and optional Temporal history. Temporal Preview is
authority-neutral; a confirmed Restore creates a new TimelineEpoch through one
explicit Unity projection binding.

## Unity assembly model

The required scene surface is intentionally small:

```text
Actor GameObject
├─ StateGraphHost        required, exactly one
│  └─ StateGraphAsset       required
├─ Intent Source scripts     explicit ordered Host references as required
├─ Event Adapters           typed Inbox filters inside the nearest Host boundary
├─ Operator scripts          optional; referenced by the Host in explicit order
├─ Host Context binding     required only when Host-owned Slots exist
└─ Context Restore binding   optional scene projection for restore and correction
```

`StateGraphHost` is the only public `MonoBehaviour` introduced by this
runtime and disallows duplicate instances on the same GameObject. Runtime,
Clock, Inbox, Router, StateLogic, StateActivationMemory, and Factory
objects are ordinary C# objects, not components or extra Inspector assets.

### Migrating a 0.4.10 scene

The Unity component is `CoCoFlow.Runtime.StateGraph.StateGraphHost`. Its script
and the `StateGraphAsset` script retain their original Unity GUIDs. The
serialized `actorContextBinding` reference loads into `hostContextBinding`.
Check the original graph, operator order and both recovery switches when
opening a migrated prefab. The Host owns a separate instance for each scene
component, even when two components share the same graph asset.

Move each retired transition Condition into the source StateLogic before
issuing the transition request. An edge with the old `MailboxReadyCondition`
stays at its source when the logic makes no request. An explicit request can
now take the edge even when the old condition would have rejected it. Exercise
both values of the guard before saving the migrated graph. A retired Animation
Operator appears as a missing component in existing prefabs. Replace its
Inspector reference and verify the associated persistence and temporal switches.

A saved Host checks the graph ID, Context layout ID, layout version and schema
hash before applying its Durable payload. The layout ID includes the project
catalog fingerprint. Retiring a registered Condition or Operator can change that
ID even for a graph that never used the declaration. Such an existing slot
reports `InvalidFrameLayout` and preserves the current Tick. Check an actual
project slot before replacing a catalog on which saved games depend.

Project code derived from `EntityContext`, or declaring an `EntityIdentity`
field, requires a source migration. Inspect serialized references to these
types before resaving project assets. `PersistenceContext` owns its stable
entity ID and prefab key. `LifecycleContext` retains its seven lifecycle values
and transition rules.

### Standard binding startup

`HostBindingBootstrap` attempts standard provider installation before and
after scene load. It leaves an already installed project provider untouched;
otherwise it scans loaded non-package assemblies for concrete
`StateAttribute` logic classes. Finding no attributed State is a valid
no-op for projects that do not use the standard path.

Standard Intent binding matches each Host source by its exact
IIntentFrameSource<T> payload type, not list position. InputIntentContract uses the
Host-local InputIntent; CameraIntentData uses CameraIntent. Missing, extra,
duplicate or ambiguous sources reject startup. Each Host owns its source handles.

For a single source, mark the State property `[Intent]` and supply an
`IIntentFrameSource<T>` component in the Host's Intent Sources list.
The standard catalog derives the ID from the payload. Modules with multiple
sources can declare an explicit reducer and capacity using `IntentRegistrationAttribute`.
An operator uses `[OperatorDefinition]` and `HostOperatorBuilder`
to declare consumed operations, owned Outcome slots and emitted packets.

Standard Config arrays and `List<T>` are restored from the frozen Values by the
existing standard Factory. Omitted fields retain Logic constructor defaults;
explicit empty collections override those defaults. Configured collections are
independent for each node and runtime Logic instance. Struct element edits are
written back through their containing collection. Invalid paths, lengths,
indices, or leaf values reject freezing through the existing diagnostic path;
failed Apply does not publish partial field changes. Authoring edits after
freezing cannot mutate the frozen snapshot.

The Host exposes lifecycle control, Manual Step, read-only authority inspection,
and the Temporal Begin/Preview/Confirm/Cancel/Correction surface. Driver mode,
AutoStart, Actor TimeScale, history capacity, and diagnostic capacity are
settings on that same Host, not reasons to add another component. The custom
Inspector validates the Asset, explicit component references, project runtime
bindings, EventDomain, Driver, and Temporal configuration. It may suggest
compatible components inside the Host boundary, but never assigns or saves a
suggestion until the user confirms it. All configuration is read-only while the
Host is Running.

Intent Sources and Operators retain their explicit Host references. Typed Inbox
adapters can be assigned or discovered among components inside the nearest
Host boundary. A nested Host owns its own descendants.

The Host Context binding is also one explicit Host reference. It must implement
the binding contract, stay inside the Host's transform scope without crossing a
nested Host, and cover every Host-owned writable Slot exactly once. It must be
absent when the Layout contains no Host-owned Slot.

The Context Restore binding is one separate explicit Host reference and must
implement `IHostRestoreBinding`. When assigned for Temporal history, it must remain inside the same Host
boundary. The Inspector supplies the explicit reference. One component may
implement both Host capture and Context restore contracts and be assigned to
both fields.

## Shared graph, isolated Actor state

Hosts may share one immutable compiled result from the same Asset. Everything
that can change at runtime remains instance-owned: StateLogic
instances, double-buffered State memory, the active leaf of every Layer, Clock,
Inbox, pending staged Tick, Temporal Ring/cursor, and the latched Fault.

The standard binding provider freezes the discovered State and Operator
declarations into a catalog. To limit discovery, use
`HostBindingProvider.BuildForAssemblyOf<T>()` and
`HostProjectBindings.TryInstall` before starting the Host.
The Host validates the assigned sources and operators against the compiled
graph before a callback or Tick can run.

Transaction preflight runs before Clock creation and before
`StateGraphInstance.TryCreate`. It classifies every non-Derived Context Slot
exactly once as Graph state, Graph auxiliary value, canonical Claim, Operator
Outcome, or Actor binding output; existing rebuilders exclusively own Derived
Slots. Invalid producer, Operator, Claim, Actor-binding, or Outbox setup is
therefore rejected before Logic, Memory create/reset/fingerprint, or
Graph-capture callbacks can run. A Transaction created for a later Runtime
create/start/initial-validation failure is disposed before the Host returns.

The standard binding supplies the actual Context default, Codec, and semantic
fingerprint for each binding. The fingerprint is the Provider's declaration of
semantic compatibility with the Manifest, not a canonical hash recomputed from
the supplied default value. After Runtime start but before Host publication, an
initial Graph-state capture verifies that Graph records match those Layout
defaults without creating Revision 0.

For assets containing old Event-to-Intent declarations, remove the declarations,
read the payload from the State Inbox and use an Inbox adapter for filtering.

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
3. Run the optional Layer AnyState's `Update` after the ordinary leaf.
4. Evaluate requested edges: eligible AnyState edges win before ordinary edges;
   within each source, arbitrate by Priority after Window checks.
5. When there is a winner, run the old ordinary exit suffix's optional `OnExit`
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

## AnyState: one persistent Logic per Layer

Bind a concrete `AnyStateLogic` with the usual `[State]` attribute.
Each Layer has at most one optional binding. It implements only `Update`;
implementing `IStateEnter` or `IStateExit` is rejected. It retains the
complete Logic binding surface:

| Capability | AnyState contract |
| --- | --- |
| Config / Intent / previous Context | Existing frozen Config, current Intent and callback-scoped read rules |
| Memory / Activation | Independent, persistent for this Graph instance; candidate writes remain transactional |
| Operation | Existing declared Operations, field composition and callback-scoped writer |
| Inbox / Outbox | Existing typed mailbox and commit-only publication; no extra permission restriction |
| Transition requests | Predeclared AnyState outgoing edges to ordinary leaves in its own Layer |
| LocalSeconds / ActionProgress | AnyState's own Activation clock/progress; progress requires the usual declaration |

For `A → B → C`, the ordinary active path stays `A/B/C`, and C stays a leaf.
Execution is `A.Update → B.Update → C.Update → AnyState.Update → arbitration
→ ordinary Exit → commit`. AnyState runs at the next deeper execution position;
it never changes `ParentStateId`, the initial child, or the ordinary active path.
Its declared `StateId` and transition `SourceStateId` identify AnyState.
`ActiveLeafStateId` on the State execution context identifies the current
ordinary leaf without replacing the owner's Config, Memory or clock identity.

AnyState → C while C is active is the existing C → C self-loop: C exits, gets
a new Activation and reset Memory/progress, then enters on the next successful
Tick. A/B and AnyState retain their Activation, Memory and clocks. Ordinary
leaf changes also preserve AnyState. Suspend preserves it; Stop ends execution,
and a fresh Graph instance initializes it again. Failed Ticks roll it back;
Context/Temporal capture and restore include its persistent state.

The example assumes a registered project `LifeIntent` with `IsDead`, an ordinary
`DeathAction` Logic, and one unconditional `Always` AnyState → DeathAction edge.
The Host explicitly binds that Intent source and the Locomotion Operator.

```csharp
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.Locomotion.Contracts;

[State("PlayerLifeAny")]
public sealed class PlayerLifeAnyLogic : AnyStateLogic
{
    [Config] public bool EnableDeath = true;
    [Memory] public bool DeathRequested;
    [Memory] public int UpdateCount;
    [Intent] public LifeIntent Life { get; set; }
    [Operation] public ILocomotionOperation Movement { get; set; }

    public override void Update(StateExecutionContext context)
    {
        UpdateCount++;
        if (!Life.IsDead) DeathRequested = false;
        if (!EnableDeath || !Life.IsDead) return;
        Movement.MoveX = 0f;
        Movement.MoveZ = 0f;
        if (!DeathRequested && context.TryRequestTransitionTo<DeathAction>())
            DeathRequested = true;
    }
}
```

Select the Layer, choose this Logic in its **AnyState** card, click **Add AnyState**, then author its
edge to DeathAction and an explicit DeathAction → Idle revival edge. Config
uses the normal Save/Discard draft. `UpdateCount` survives Idle/Run/Death and
self-reentry. The guard is a project choice for this unconditional single edge;
request acceptance alone does not prove a conditional edge won. Projects with
conditional requests should base acknowledgement on committed state as needed.
Removing the guard permits a sustained request to re-enter Death every Tick;
the framework deliberately does not deduplicate requests to the active leaf.

Writing Movement alone does not exit C. AnyState's rank is current leaf depth
plus one, so its MoveX/MoveZ override this Layer's ordinary writes, including
later Exit writes; untouched fields retain lower-ranked contributions. A higher
Layer still wins. Ordinary Update outputs and Outbox entries are not discarded
because AnyState won the transition; all follow the same transaction rules.

## Declared Transition evaluation

Every Transition is authored before runtime, from an ordinary leaf or the
Layer's AnyState to an ordinary leaf in that Layer. Update cannot call `ChangeState`; it
can only request a compiled Transition handle that is already an outgoing edge
of its own source. A leaf with no outgoing edge performs no ordinary Transition work.

One Update may request several handles. After Update completes, the Runtime
evaluates their declared windows and selects the single highest
Priority eligible candidate. AnyState's eligible set takes precedence; only if
it has no eligible request can an ordinary request win. Priority is unique among
the outgoing edges of one declared source. The same numeric Priority may be reused
by another source or Layer because those candidates never share an arbitration
set.

A State may read frozen Intent, previous Context, Tick, Config and its own progress
before making a request. Reaching progress 1 leaves that decision to the State.

Timed windows are half-open `[StartInclusive, EndExclusive)`:

- `LocalSeconds` is Actor-clock time within the source's own Activation.
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
The writer accepts only Operations declared by that State's `OperationProvides`
manifest and expires when the callback returns; an escaped or undeclared write
faults and rolls back the candidate Tick.

Continuous Operations compose per field: fields that a higher-ranked State does
not write retain lower-ranked contributions. A Discrete Operation allocates its
OperationSequence once, for the final winning contribution only. Declaring the
same Operation on parent and child is legal and produces a non-blocking warning;
the same declaration across Layers is normal composition and produces no
warning.

OperationFrame creation uses a transactional protocol:

```text
TryBegin -> Write -> TryFinalize -> FinalizedFrame -> Commit / Cancel
```

Finalize freezes candidate bytes only. It does not consume a sequence or update
the last committed Tick. The Runtime returns a single-use staged Tick containing the
candidate active leaves, State memory, Clock, and finalized OperationFrame, and
refuses another Step while that Tick is unresolved.

StateGraphHost resolves that staged Tick in one fixed transaction:

```text
Preview -> Context Prepare -> Intent Collect/Freeze -> Graph Stage + Trace
  -> Graph-owned State/Value Capture -> Claim Arbitration/Claim Capture
  -> Operators/Outcomes/Outbox -> Host-owned Capture -> Derived Finalize
  -> Composite Preflight -> Temporal Projection Capture
  -> no-fail Commit + Temporal Publish -> complete Outbox Publish
```

The first Tick receives a `ContextFrameReadView` backed by the layout's
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
binding captures Host-owned Slots after Operators, but like every Operator it
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

The package also exposes one internal Editor-only debug seam for a healthy Suspended
Host. It accepts one explicit finite positive delta under Update, FixedUpdate,
or Manual driving, executes exactly one ordinary forward Tick through the same
Intent, Graph, Operator, Context, Temporal, Trace, and Outbox boundaries, and
returns a healthy success to Suspended. It does not change the configured Driver
or run an accumulator/catch-up loop. Temporal Preview, an unresolved Tick,
Fault, or required world Correction rejects the request. If the Tick fails, the
real lifecycle, Fault, and correction state remain visible; the Host does not
fabricate a successful suspension.

Callback exceptions, failed Operation finalization, and reliable
Inbox overflow cancel the candidate and latch Fault at a safe boundary. A
faulted Host rejects normal Resume and new gameplay input. Recovery requires
Stop followed by a fresh instance, except for the narrow world-correction
path described below. CoCoFlow does not expose a general `ClearFault()` API.

## Temporal mode and public orchestration

`HostHistoryMode` is orthogonal to the Runtime lifecycle; it does not add a
sixth lifecycle state:

- `Disabled`: configured history capacity is zero;
- `Ready`: history is enabled and normal forward Ticks may commit entries;
- `Previewing`: normal Tick and gameplay ingress are blocked while the
  non-authoritative cursor selects history.

With capacity zero, a valid in-boundary Restore binding can still project a
durable save into the scene or apply `TryCorrectWorld` after a failed Tick.
The binding is optional at any capacity. Enabled history validates an assigned
binding before the Host enters Running.

Capacity is fixed before Running and counts entries, including the current
authority. Zero disables history; enabled history requires at least two entries,
and capacity one is rejected during startup. The first successful Context commit
makes Count 1. A full Ring overwrites the oldest entry, and
Stop/Dispose/destruction releases the Ring, cursor, scratch storage, and callback
tokens. No mutable Frame, payload, arena handle, or long-lived selection token is
exposed.

The Host API is synchronous:

```text
HostHistoryState TemporalState
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
Transition, Operator, Actor capture, Event, Trace, or sequence work.

Cancel invokes the same binding with `Cancel` to reapply current authority only
after at least one Preview projection succeeded. Cancelling directly after Begin
skips the binding. Neither path performs a logical restore or switches Epoch.
Confirm validates and prepares the complete Context, Graph Path/Memory, Clock,
and Claim candidate, then invokes the binding once with `Confirm`. After that
succeeds, a no-fail barrier swaps all logical authority, discards the abandoned
future, and records the new-Epoch restore commit as the new history branch head.
The next accepted positive-delta Tick resumes normal StateGraph execution.

The callback receives only a token-scoped `HostRestoreReader`; retaining
it beyond the synchronous call yields an invalid reader. A preflight failure
before any callback leaves a clean session healthy; if a previous Preview
projection remains active, the same failure requires Correction. Once a binding
callback starts, a refusal, exception, destroyed component, re-entry, or possible
partial Unity mutation does not move the cursor or logical authority. The Host
latches Fault and `RequiresWorldCorrection`. `TryCorrectWorld` invokes the same
binding with `Correction` against the last logical authority and clears only the
matching recoverable fault after successful projection.

## Committed debugger snapshot and Trace

The Runtime Debugger reads an internal immutable Host snapshot copied from
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

The Editor presents the current Host lifecycle, the copied committed frame, and
the Temporal ring metadata in one read-only debugger view. A failed refresh
preserves the previous snapshot and marks it as a retained last committed
snapshot (stale). The focused 0.4.1 debugger does not expose Trace filtering or
lifecycle controls; Trace remains a separate Runtime diagnostic surface.

Trace capacity defaults to zero, creates no buffer in that mode, and is fixed
before Running. The Editor cannot resize it on a live Host. A failed transaction
does not change committed snapshot authority and its Trace cannot contain
Context commit, final sequence, or publication entries.

World-correction state does not itself reject debugger capture. At an otherwise
idle committed boundary, the Host may copy its last committed Graph, Context,
and Claims together with the current Fault, correction latch, and diagnostic.
Capture does not invoke Actor/Graph bindings, advance Clock or Tick, append
Trace, apply correction, clear Fault, or expose the failed candidate. Starting,
advancing, Temporal operations, committed-event publication, active Preview,
and lower-level candidate/transaction/restore activity continue to reject
capture.

The suspended debug step is not an authority-neutral debugger preview. Because
it is one normal positive-delta Tick, it may advance Context/Clock, append
Temporal and Trace entries, execute Operators, and publish a successfully
committed EventOutbox before returning to Suspended.

## Host Inspector and Runtime Debugger tooling (0.4.1)

The 0.4.1 release rebuilds both editor surfaces with the unified `ccflow`
UI Toolkit visual language. Runtime semantics, serialization, and the frozen
contracts above are unchanged.

### Host Inspector

The inspector for `StateGraphHost` is organized into six zones:

- **Overview** — StateGraph asset, driver, auto-start, time scale.
- **Bindings** — the three ordered reference arrays (Intent Sources, Event
  Adapters, Operators) plus the single Actor Context reference. Intent Sources
  and Operators offer Host-boundary-scoped candidate menus and per-row assembly
  hints; Actor Context offers the same boundary-scoped candidates. Event
  Adapters are presented as an ordered list only: slots must match the compiled
  adapter manifest slot-for-slot, which the runtime validates at startup.
- **Restore** — Context Restore root, chain preview, and one-click Auto-wire.
  Auto-wire validates the candidate chain first, then writes the root and all
  downstream links as a single Undo gesture; a failed validation writes
  nothing.
- **Capacities** — the seven capacity fields, with the Trace capacity
  lifecycle note.
- **Runtime Status** — read-only live lifecycle/fault/correction/instance
  state during Play, plus an Open Debugger action.
- **Diagnostics** — collected assembly hints with Locate actions.

Assembly hints are authoring-time only: they mirror the statically decidable
subset of the runtime startup discipline (missing asset, wrong interface,
boundary violations, duplicate references, broken restore chains). Passing all
hints does not imply a successful start; the full startup authority (unique
manifest-bound generics, descriptors, graph coverage, Actor slot matching)
remains with the runtime.

While the inspected Host has a live runtime (Running or Suspended), the four
configuration zones are write-disabled: running configuration is read-only,
matching the frozen Host contract. Status, diagnostics, locating, and opening
the debugger remain available.

### Runtime Debugger

The debugger window is available from the `CoCoFlow/StateGraph Debugger` menu
(the previous `Window/CoCoFlow/StateGraph Debugger` entry was removed). It
observes one concrete scene `StateGraphHost`, not a StateGraph asset, and
answers three questions only:

- **Current committed frame** — frame identity and time metadata plus each
  Layer's current Active State, local time, action progress, and winning
  Transition. State IDs are resolved to authoring names when the Host asset
  provides them.
- **Temporal ring** — a circular `Count / Capacity` view. Logical depth zero is
  the current retained authority; older Context frames proceed clockwise. A
  selected node shows GraphInstance, TickFrame, Context Revision, and Origin
  metadata. Retained payload bytes and Context field values are never exposed.
- **Persisted frame** — shown only when the Host has a matching
  `PersistenceContext` and a compatible StateGraph record already exists in a
  standard on-disk save slot. The newest valid `updatedUtc` record is shown and
  highlighted on the ring when its exact frame identity is still retained.

The analyzed Host is marked at its Transform position in Scene view while the
window is open. The marker and ring selection are Editor-only and do not dirty
the scene. The Editor consumes two narrow top-level debug seams:

- the Host seam copies the current committed frame plus only Temporal ring
  capacity and per-frame identity/time/revision/origin metadata; it exposes no
  retained payload, Context field value, mutable collection, or operation;
- the Persistence seam scans existing standard slot documents and returns only
  the newest compatible record's source-frame metadata and write time. It does
  not create a save directory or invoke Capture, Apply, or a persistence
  session.

Both seams and their helpers are `internal`, `UNITY_EDITOR`-only, read-only,
and non-serialized. They do not Preview, Restore, advance a Tick, modify Host
diagnostics, or change Runtime lifecycle state. The window contains no Suspend,
Resume, single-step, Trace, or Context mutation action.

## Actor event boundary

The Host is the Actor's single gameplay-event boundary. StateLogic cannot
publish an immediately visible gameplay event; Operators may only append typed,
preallocated EventOutbox candidates owned by their current transaction token.

### Direct State mailbox (D043-04/07/11/12/14/15)

An attached component implements `IEventInboxAdapter<TEvent>.Accept(in TEvent)`
for each unmanaged event it receives. Place the component inside the nearest Host boundary. A Host supports multiple components and each component supports
multiple event interfaces. One subscription per event type ORs their filters;
one bus delivery enters the typed Incoming queue at most once. Separate Publish
calls remain separate messages. Target/group filtering belongs to the payload
and Adapter, not a second routing API.

Adapters must belong to this Host boundary; a nested Host owns its own descendants.
Direct mail is accepted only while Running. Each event type keeps at most
`eventLaneCapacity` incoming messages; a full queue drops new arrivals without
faulting the Host (D043-14). Suspended and Previewing Hosts drop direct input.
A serialized zero capacity retains no direct mail only when the Graph has no
legacy Event declarations. Legacy or mixed Graphs require positive capacity;
the Host Inspector keeps its minimum of 1.

At the Tick boundary the Host freezes Incoming buffers, including Hosts without
business Intents. State callbacks use `context.Inbox.TryGetBatch<TEvent>(out var mails)`
and `mails.TryRead(index, out var mail)`. Registered empty batches have Count 0;
unknown types return false. Views expire after their callback. Reading does not
consume mail, and later Ticks replace unread batches. Mail arriving after the
freeze waits for a later Tick. Subscription cleanup follows Host Stop/Dispose;
lifecycle input-authority changes clear the direct mailbox backlog. Temporal
attempts still advance `InputAuthorityRevision`, but clear direct mail only when
the operation succeeds. Rejected Temporal calls preserve pending mail (D043-15).

`context.Outbox.Publish(in mail)` snapshots an unmanaged payload during Enter,
Update, or Exit. It requires no send registration or receiving Adapter. Success
publishes the original `TEvent` through ordinary `CoCoEventBus.Publish` after
the complete commit barrier, in State call order; failure/cancellation publishes
nothing. Capacity shares the Host outbox limit. Cached typed publication items
are reused after warm-up without per-message allocation. Ordinary bus subscribers
and other Hosts' mailbox Adapters observe the same published event type.

A State marked `[State("Attack", ProvidesActionProgress = true)]` may advance
its own action progress and request a declared edge from Update. Evaluate any
gameplay rule before requesting the edge. A progress window and source priority
then determine which request can win on this Tick.

### Packet delivery and lifecycle

`TryEnqueueLocal` accepts a typed packet for a running Host with a registered
lane. Targeted packets must identify that Host. Reliable packet overflow
latches a fault at the next boundary. An unreliable packet reports a drop.
Direct payload publication uses the bounded Inbox and drops excess values.
Suspend or Temporal Preview clears pending State Inbox values and closes input.
Resume or completion of the temporal operation reopens the running Host.

Outbox finalization validates capacity and metadata without consuming sequence.
After commit, all event types of one GraphInstance/Epoch share one contiguous
EventSequence range, published in Host Operator order and then append order.
Subscriber exceptions remain isolated. An infrastructure exception records a
Fault but publication continues for the remaining committed packets; sequences
are neither reclaimed nor automatically retried. Destroy, Stop, or Dispose
requested during publication is deferred until the committed list completes.

Runtime Trace is an optional fixed-capacity immutable ring. Capacity zero creates
no buffer, and Running never resizes it. Accepted Transition Candidates are emitted in compiled order after
their source and window match, and the Winner is emitted again with an
explicit Winner role. A value-only Frame reference records Frame identity, exact
Layout identity/version/schema hash, Revision, and whether a committed Frame
exists. The first Tick therefore references exact Previous Layout defaults with
`HasCommittedFrame == false`, never a fictional Revision 0.

Successful ordering is Tick inputs, Candidates, Winner, Operations,
Operator Outcomes, Context commit, ActivePath, EventSequence, then publication
or publish diagnostics. A failed transaction ends with Cancelled and cannot
contain commit, sequence, or published entries. Trace never stores payloads,
Unity objects, mutable Frames, Router/Inbox state, or diagnostic strings, and it
does not retain Context; a caller that needs long-lived Context access must
explicitly retain and release it. Filters may constrain entries by State ID or
Transition ID without changing the stored evidence.

## Restore authority barrier

Formal Confirm uses the same single-use validation/prepare discipline:

```text
validate source and selection generation
  -> materialize Stored + Default + Derived Context candidate
  -> prepare Graph Path/Memory + Clock + Claim + branch-head entry
  -> IHostRestoreBinding.TryApply(Confirm)
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
cannot fail. Restore itself invokes no State, Transition, Operator,
Actor capture, Event, Trace, Outbox, OperationSequence, or EventSequence work.

This is same-session, same-GraphInstance, exact-layout restoration. Temporal
payloads are not durable documents or stable wire identities. They do not restore
Inbox contents, IntentFrame, CoCoEventAgent subscription, unpublished Outbox,
half-executed Operator work, another Actor, or already delivered cross-Actor
consequences.

## Boundaries

- StateGraph does not expose cross-Layer calls, queries, signals, or
  Transitions, nor an arbitrary change-state API.
- Durable file storage is owned by Persistence. StateGraphHost exposes a
  Durable Context projection payload seam; it does not own slots, JSON files,
  scene spawning, or save-slot policy.
- Animation, physics, navigation, and other Unity-world projection remain
  explicit Operator or restore-binding responsibilities.
- The Runtime maturity statement does not claim whole-world rollback,
  cross-Actor transactionality, complete Editor tooling, or zero defects.

The serialized StateGraph schema remains version `1`, uses ActionProgress for
normalized transition windows, and exposes no automatic migration for
non-v1 graph Assets.

An absent AnyState requires no migration or reconnecting. Existing ordinary
topology, four-argument Layer source construction, IDs, layout, fingerprints and
Activation allocation remain unchanged; no absent-slot hash marker is added.
