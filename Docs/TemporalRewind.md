# CoCoFlow Temporal Rewind

> Contract status: `0.4.0-pre.6` · Updated 2026-07-19

Temporal Rewind is a same-session, single-Actor facility owned by one
`CoCoStateGraphHost`. It records bounded projections of successful Context
commits, previews history without changing logical authority, and performs one
formal Restore into a new TimelineEpoch when the caller confirms.

It is not reverse StateGraph execution, a world snapshot, durable persistence,
or a cross-Actor side-effect rollback system.

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

- capacity `0` disables Temporal history;
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

Depth is absolute from the branch head: `0` means current authority, `1` means
the preceding recorded commit, and so on. Confirm requires a historical depth;
selecting current authority does not manufacture a new Epoch.

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

## Explicit non-goals

- negative Delta or reverse StateGraph/Operator execution;
- multi-Actor or whole-world rollback;
- undoing already delivered cross-Actor consequences;
- Animator, Playable, AnimationClip, or root-motion reverse mapping (Pre11);
- durable saves, migration, containers, or world facts (Pre13);
- production Samples and golden-path content (Pre15/Pre16);
- a global Temporal manager, runtime capacity resizing, or shared history.

See [State Flow / Event Boundary](ContextNetworkBoundary.md) for the complete
Context and mailbox authority model, and
[StateGraph Runtime and Host](StateGraphRuntime.md) for normal Tick, lifecycle,
Operator, and event-publication semantics.
