# CoCoFlow Temporal Rewind

> Contract status: `0.4.0-pre.10` · Updated 2026-07-25
>
> Pre10 Map decorator verification: `UNVERIFIED` until the Unity-host,
> package, Player-build, and Package Validation Suite evidence is recorded.

Temporal Rewind is a same-session, single-Actor facility owned by one
`CoCoStateGraphHost`. It records bounded projections of successful Context
commits, previews history without changing logical authority, and performs one
formal Restore into a new TimelineEpoch when the caller confirms.

It is not reverse StateGraph execution, a world snapshot, durable persistence,
or a cross-Actor side-effect rollback system.

Pre7's internal Editor debug step is also not Temporal Rewind. It executes one
ordinary positive-delta forward Tick from a healthy Suspended Host and can
therefore commit state, record history/Trace, run Operators, and publish
committed events.

## Authority model

One normal forward commit has this ordering:

```text
positive-delta Tick
  -> Graph / Operator / Actor production
  -> finalize complete Context candidate and Derived values
  -> encode Temporal projection into staging
  -> composite no-fail authority commit
  -> publish prepared Ring entry
  -> publish committed EventOutbox
```

The complete committed `ContextFrame` remains the Actor's logical authority.
Graph Path/Memory, ActorClock, and Claim state are mirrors exchanged with it by
the same authority barrier.

The Temporal Ring is not another authority and does not retain a ContextFrame.
It contains fixed-capacity, preallocated projection payloads and immutable source
metadata. Normal generation-scoped `ContextFrame.Retain()` and `Release()` remain
valid public contracts; Temporal history simply does not use them.

## Projection payload

The Layout's projection flag and restore policy are independent:

| Slot classification | Stored in Ring | Restore value |
|---|---:|---|
| `Temporal + Stored` | Yes | Encoded historical value |
| `Temporal + ResetToDefault` | No | Layout default |
| `Temporal + Derived` | No | Deterministically rebuilt from its closed dependencies |
| Non-Temporal Stored | No | Layout default |

A Temporal projection containing a Derived Slot is legal only when all of its
transitive Stored/Derived dependencies are in the projection. Reset-to-default
dependencies do not need payload bytes. Missing dependencies, incompatible
Layout identity, or incompatible Codec configuration reject capture/restore.

IntentFrame, Inbox, sealed batches, Outbox, Trace payloads, Unity objects, and
cross-Actor consequences never enter the Ring.

Each entry also records value-only source metadata:

- GraphInstanceId;
- complete source TickFrame, including Timeline, ClockDomain, Epoch, Tick,
  TimelinePosition, positive Delta, and ExecutionSequence;
- Context Revision and Origin.

The representation is internal, exact-layout, and same-session. It is not a
stable wire format or a Pre13 durable-save document.

## Capacity and capture

History capacity is configured before Running:

- capacity `0` disables Temporal history; enabled history requires at least `2`
  entries so the Ring can hold current authority and one older commit;
- capacity counts commits, including the current authority;
- the first successful Context commit makes Count `1`;
- a full Ring overwrites the oldest entry;
- Running does not resize or hot-swap history configuration;
- Stop, Dispose, and destruction release the Ring, preview cursor, scratch
  storage, and callback tokens.

Every successful Context commit records automatically. There is no public
`Record()` call. Failed/cancelled Ticks, Suspend, Pause, and periods with no Tick
produce no entry.

Capture reads the finalized candidate before authority changes. Encoding uses a
separate staging payload so capture failure leaves both the current authority
and Ring unchanged. It also publishes no Outbox and consumes no final sequence.
If an Operator already touched Unity, the Host follows the world-correction rule
below. After the composite authority barrier succeeds, publishing or overwriting
the prepared Ring entry is no-fail.

## Host surface and TemporalMode

Temporal state is orthogonal to the Runtime lifecycle:

```text
Lifecycle:   Created / Running / Suspended / Stopped / Disposed
Temporal:    Disabled / Ready / Previewing
```

Pre6 does not add a Runtime lifecycle state, global Temporal manager, or shared
mutable history. Each Host owns its Ring, cursor, binding, and orchestration.

The public Host surface is synchronous:

```text
CoCoTemporalState TemporalState
bool TryBeginTemporalPreview(out CoCoDiagnostic diagnostic)
bool TryPreviewTemporal(int historyDepth, out CoCoDiagnostic diagnostic)
bool TryConfirmTemporalRestore(out CoCoDiagnostic diagnostic)
bool TryCancelTemporalPreview(out CoCoDiagnostic diagnostic)
bool TryCorrectWorld(out CoCoDiagnostic diagnostic)
```

`CoCoTemporalState` exposes Mode, Capacity, Count, PreviewDepth, immutable
current/preview metadata, rewind/restore dropped-input count, and whether the
selection can Confirm. It does not expose mutable Frames, payload buffers,
prepared candidates, arena handles, or long-lived history tokens.

The Confirm eligibility flag is a side-effect-free structural snapshot derived
from cached Temporal records. Reading `TemporalState` never probes Unity object
liveness, marks a retained physical identity unavailable, or latches world
correction. Actual projection and Confirm preparation revalidate the physical
identity and may still reject a selection that became unavailable after the
snapshot was read.

Depth is absolute from the branch head: `0` means current authority, `1` means
the preceding recorded commit, and so on. Confirm requires a historical depth;
selecting current authority does not manufacture a new Epoch.

## Suspended debug stepping is not rewind

The Editor-only debug seam accepts one explicit finite positive delta from a
healthy Suspended Host under Update, FixedUpdate, or Manual driving. It does not
change the configured Driver. Internally it advances exactly one complete normal
Tick through Intent collection, StateGraph, Operators, Context commit, Temporal
capture, Trace, and EventOutbox publication, then returns a healthy result to
Suspended.

Unlike Preview, this operation is authoritative and may create a new committed
Context Revision and Temporal entry. It does not select a historical payload,
apply a Restore binding, create a branch Epoch, or use negative Delta.

The request is rejected while Temporal Mode is `Previewing`, while another Tick
or callback is unresolved, or when Fault or `RequiresWorldCorrection` makes
forward progress illegal. If the Tick itself fails, the Host preserves the real
Fault and world-correction result instead of reporting a fabricated successful
Suspended state.

## One synchronous Restore binding

An enabled Temporal Host references exactly one component implementing:

```csharp
public interface ICoCoContextRestoreBinding
{
    bool TryApply(
        in CoCoContextRestoreBindingContext context,
        out CoCoDiagnostic diagnostic);
}
```

The same aggregate binding handles `Preview`, `Confirm`, `Cancel`, and
`Correction`. It must prevalidate before mutation where possible and complete
synchronously. A component may implement both the Actor-capture and restore
contracts, but the Host fields remain explicit.

Capacity zero disables the Temporal Ring and does not require a binding. An
invalid, destroyed, or out-of-boundary component is ignored in that mode; one
valid in-boundary binding may still be retained solely so `TryCorrectWorld` can
repair Unity after a non-Temporal Tick failure that may have dirtied the world.

The callback receives a read-only, token-scoped `CoCoContextRestoreReader`.
It can read the fully materialized policy-effective candidate, including Layout
defaults and rebuilt Derived values. The reader expires when the callback
returns; storing it does not retain a Frame or candidate.

## Begin and Preview

Begin requires a healthy Running Host, Temporal Mode `Ready`, no active Tick or
binding callback, and at least one older entry. It immediately:

1. clears queued Inbox lanes, any sealed batch, and deduplication state;
2. switches the Host to `Previewing` at depth zero;
3. blocks automatic and Manual Tick advancement;
4. keeps ingress registered only so new gameplay messages can be rejected and
   counted.

Moving the cursor decodes the selected payload over Layout defaults, rebuilds
Derived values, and invokes the binding with `Preview`. The logical Context,
Graph, Clock, Claims, Revision, Epoch, Sequence, Trace, and Outbox remain
unchanged. The cursor advances only after the binding succeeds.

A missing or moved binding detected before Begin changes Inbox state rejects the
request without faulting the Host. The same preflight failure before the first
Preview callback leaves the session clean and can still be cancelled without the
binding. If an earlier Preview projection succeeded, losing the binding requires
world Correction because Unity may still present that selection.

Preview does not run State Enter/Exit, State Update, Condition, Transition,
Operator, Actor capture, Event, or Trace callbacks. It never feeds a negative
Delta into StateGraph.

## Cancel

If at least one Preview projection succeeded, Cancel invokes the same binding
with `Cancel` and the complete current logical authority. A session cancelled
directly after Begin, before any Preview projection, skips the binding. Both paths
clear the preview cursor and return Temporal Mode to `Ready` only after the Inbox
can leave rewind mode.

Cancel performs no formal Restore, creates no Revision, and keeps the existing
TimelineEpoch. The Inbox backlog cleared by Begin remains cleared; Cancel never
revives old queue, batch, packet, or dedup state.

## Confirm and branch creation

Confirm prepares all fallible work before the Unity binding:

```text
validate source and selection generation
  -> validate Graph/Layout/Timeline/Clock metadata
  -> materialize Stored + Default + Derived Context candidate
  -> prepare Graph Path/Memory + Clock + Claim
  -> prepare new-Epoch history branch head and mailbox switch
  -> ICoCoContextRestoreBinding.TryApply(Confirm)
  -> no-fail authority exchange
```

The target:

- keeps the source TimelineId and ClockDomainId;
- restores source Tick and TimelinePosition;
- uses a TimelineEpoch strictly newer than both source and current Epoch;
- strictly advances ExecutionSequence;
- creates a Revision newer than current authority;
- records the selected GraphInstance/Epoch/Tick/Revision as Origin.

After the binding succeeds, one no-fail barrier swaps Context, Graph, Clock, and
Claim authority, discards history after the selected point, publishes the new
Epoch restore commit as the branch head, clears old mailbox state, and returns
Temporal Mode to `Ready`. The next accepted positive-delta Tick resumes normal
forward StateGraph execution.

Restore itself consumes no OperationSequence or EventSequence and publishes no
EventOutbox or Trace entry. It does not call State, Condition, Transition,
Operator, Actor capture, or Event code.

## Mailbox and Epoch rules

Rewind ingress differs deliberately from ordinary Suspend:

- Begin immediately clears queue, sealed batch, and dedup state;
- gameplay messages arriving while Previewing are dropped and counted;
- Cancel keeps the original Epoch but does not restore old backlog;
- Confirm invalidates all old-Epoch batch, packet, and dedup state;
- after Confirm, only messages created for the new Epoch are accepted;
- Suspend/Resume keeps the original Epoch and preserves legal bounded backlog.

The framework does not secretly defer rewind-time input until after Restore.

## Failure and world Correction

Validation failures before a binding callback leave Unity and logical authority
unchanged. They do not fault a clean session. If a previous Preview projection is
still active, a binding preflight failure instead requires Correction because
Unity may still differ from authority.

Once a binding callback starts, a refusal, exception, destroyed component,
re-entry, or possible partial Unity mutation is treated more conservatively:

- the preview cursor does not advance;
- the old Context/Graph/Clock/Claim authority remains current;
- no branch head, Epoch, Revision, Trace, Outbox, or final sequence is committed;
- the Host latches Fault and sets `RequiresWorldCorrection`.

CoCoFlow does not fabricate a Unity-world rollback. `TryCorrectWorld` invokes the
same binding with `Correction` against the last logical authority (or Layout
defaults before the first commit). Only a successful correction clears the
matching recoverable fault and `RequiresWorldCorrection`; unrelated or
non-recoverable faults remain latched. Stop and a fresh Host instance remain the
general recovery path.

## Map and Pooling Temporal decorator chain

Pre10 composes optional availability decorators into the Host's one synchronous
Restore-binding slot:

```text
Map -> optional Pool -> project restore binding
```

The Map decorator captures committed Region capability and Coverage. It uses
that identity for retention and an availability barrier only; it does not put
Map state into the Temporal ring, restore a fidelity tier, or replay Map
transitions.

Map holds one internal barrier across Preview, Confirm, and Cancel; an
independent Correction holds it from Prepare through Finish. Barrier entry is
atomic and rejects a real transition already in flight, a Map fault, blocked
cleanup, or an existing deferred flush.

While held, demand Create, Update, and Dispose still advance logical demand,
revision, and final resolution. They only update a deterministic dirty set:
Temporal Preview cannot load a Scene, prepare a Pool, Prepare/Commit a
participant, or retry a Region. If historical presentation is not already
available through retained committed ownership, the callback fails through the
existing world-correction contract rather than causing hidden streaming.

Barrier release schedules, but does not synchronously run, transition work.
`CoCoMapHost.LateUpdate` dispatches only the final resolution for each dirty
Region after the callback stack returns, and nested dependency recomputation is
coalesced into that same flush. Confirm branch truncation therefore queues
retention decreases until they cannot invalidate a live decorator chain.

The optional Pool decorator projects whether an adopted
`CoCoTemporalEntityId` is physically present; the Context projection remains
authoritative for gameplay values.

Each decorator freezes the downstream Restore component at Host attach: whether
it was configured, its exact `MonoBehaviour` identity, and its interface
reference. Public mutations and every projection validate frozen identity,
Unity liveness, Host boundary, and callback reentry before local mutation,
before the downstream call, and after the call returns. A destroyed, replaced,
moved, rejecting, throwing, or re-entering downstream cannot silently degrade
to “not configured” or continue after-restore activation. Once a downstream
callback has started, failure uses the Host's existing world-correction path;
CoCoFlow does not fabricate a transactional Unity rollback.

Before startup, StateGraph Host internal introspection follows these frozen
decorator references and rejects direct or indirect cycles, including
`Map -> Pool -> Map`. Map and Pool provide the introspection seam independently;
neither product module gains a reverse dependency on the other.

The identity and storage boundaries stay separate:

- `CoCoTemporalEntityId` is a pure Core Contracts value.
- The sidecar keeps a Host-scoped, fixed-capacity presence history aligned with
  the Host history window.
- History stores no GameObject, Component, `PooledHandle`, `ContentLease`,
  backend handle, Transform, or arbitrary domain payload.
- The same physical GameObject is quarantined while any retained history entry
  can still project its entity as present. It cannot be rented to another
  entity during that interval.
- A successful adoption transfers generation authority from the consumer's
  handle to `PoolTemporalRuntime`; old handle copies remain stale after Preview,
  Cancel, Confirm, and Correction.
- Presentation parent is live-record state. Scene Root is represented
  explicitly and replayed with `SetParent(null, false)`; a Transform parent must
  remain live. The most recent parent is refreshed whenever an active entity
  leaves presentation, and no Transform enters the Temporal ring.
- Callback ownership follows physical state. Initial `TemporalInactive` has not
  received Rent and rejects public Despawn; `TemporalActive` owns exactly one
  matched reverse Return; `TemporalQuarantined` was already reset. Host stop
  attempts this normal state-aware release before terminal Force Destroy.
- `IPoolTemporalApply` is a distinct synchronous presentation hook invoked
  after Context projection. It is not a second Context history or a persistence
  codec.

Preview changes only projected presence. Cancel/Correction restore the current
logical-authority projection; Confirm publishes the selected presence into the
new branch authority. Ring overwrite, discarded branch futures, history clear,
or Host stop release quarantine only after the last historical reference
expires.

If an adopted physical instance is externally destroyed or cannot be reset, the
sidecar records it as unavailable. It does not fabricate a replacement with the
same historical identity or rewrite Context history. Selections that require
that entity cannot Confirm until the project repairs or abandons the Host.
When current authority already requires a projected-only entity absent,
physical loss is itself the desired result: the same Cancel or Correction can
finish and return the Host to `Ready`. This tolerance never applies while
authority or the selected history frame still requires the entity present;
authority-present loss continues to latch world correction.

These decorators cover one Host's retained Map availability and pooled physical
identity. They are not a world snapshot and do not roll back Map state, scenes,
physics, navigation, animation, networking, durable state, or already delivered
cross-Actor consequences.

## Explicit non-goals

- negative Delta or reverse StateGraph/Operator execution;
- multi-Actor or whole-world rollback;
- undoing already delivered cross-Actor consequences;
- Animator, Playable, AnimationClip, or root-motion reverse mapping (Pre11);
- durable saves, migration, containers, or world facts (Pre13);
- production gameplay States and replacement Samples (Pre15);
- complete cross-module certification (Pre16) and final visual/XML polish
  (Pre17);
- a global Temporal manager, runtime capacity resizing, or shared history.
- using Pooling Temporal as multi-Actor or whole-world rollback, durable entity
  reconstruction, or automatic domain-payload capture;
- using Map Temporal retention to load during Preview, restore Map state, or
  replace project demand policy;
- treating suspended debug stepping as authority-neutral Preview or reverse
  execution.

See [State Flow / Event Boundary](ContextNetworkBoundary.md) for the complete
Context and mailbox authority model, and
[StateGraph Runtime and Host](StateGraphRuntime.md) for normal Tick, lifecycle,
Operator, and event-publication semantics.
See [Object Pooling and Instance Ownership](ObjectPooling.md) for pooled
instance, handle-generation, quarantine, and Scope ownership. See
[Map Region Fidelity](Module-Map.md) for Region availability, participant
transactions, and Map-owned retention.
