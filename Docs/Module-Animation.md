# Module: Animation

> Documentation baseline: `0.4.0` · Updated 2026-08-29

The Animation module projects StateGraph Operation Sections into a Unity
`Animator`, records the Animator's latest committed snapshot, and turns selected
StateMachineBehaviour signals into typed feedback for a later StateFlow Tick.
The Animator Controller remains the state-machine, transition, Blend Tree, and
clip-authoring authority.

## Contracts and registration

The engine-free `CoCoFlow.Runtime.Animation.Contracts` assembly defines:

- a 16-lane continuous parameter Operation Section;
- an 8-lane discrete trigger Operation Section;
- fixed binding IDs and parameter/trigger command values;
- a 16-record Animation feedback Intent/Event capacity;
- `AnimSnapshotState` for up to four Animator layers and sixteen parameter
  lanes.

`AnimSectionRegistrar` registers both Operation Sections and the Operator-owned
snapshot Context slot with the Standard Binding path. The snapshot slot is
`Stored` and marked for both Temporal and Durable projection.

## AnimAutoOperator

`AnimAutoOperator` is the current runtime component. It requires one `Animator`
and one `CoCoStateGraphHost` in the same Host boundary. Its serialized bindings
map stable `AnimBindingId` values to concrete Animator parameter names.

Startup/rebuild validation fails closed when bindings are missing, duplicated,
type-incompatible, outside fixed capacities, or attached to the wrong Host
boundary. During each Operator execution it:

1. validates the finalized parameter and trigger Sections;
2. writes Float/Integer/Boolean parameters and Set/Reset trigger commands;
3. commits any staged SMB feedback to the typed Event Outbox;
4. samples the Animator's latest engine state into the owned Context snapshot
   slot.

The snapshot is an engine fact observed after Unity's Animator update, so one
Tick of feedback latency is intentional; it is not a prediction of the
Controller's next state.

## StateMachineBehaviour feedback

`AnimEventSmb` can emit State Enter, configured normalized-time Marker, and
State Exit signals. Per-Animator cursors keep callbacks isolated when an SMB
asset is shared and when current/next state instances overlap.

Signals first enter the Operator's fixed reliable buffer. They are attributed
to a committed or candidate Host identity, written to the Event Outbox only by
a successful Operator transaction, then projected to `AnimFeedbackIntent` for
a later Tick. They never call StateGraph directly. Buffer overflow rejects the
whole batch and requires an explicit stop/rebuild/start recovery boundary.

Marker scanning covers the forward normalized-time interval and loop crossings.
A backward seek establishes a new cursor baseline and does not synthesize
reverse or retroactive events.

## Temporal and durable projection

`AnimSnapshot.Sample` records current layer state hashes, normalized times,
weights, and configured parameter lanes. Restore projection validates the
current Animator layout and state hashes, writes the saved parameter lanes,
uses `Animator.Play` for each stored layer, restores weights, and performs a
zero-time `Animator.Update(0)` so the pose is visible without advancing time.

Layout mismatch fails with `WorldCorrectionRequired`; partial silent restore is
not accepted. This snapshot does not reproduce every internal Animator detail,
transition blend, Playable graph, clip event, root-motion history, or physics
consequence.

## Boundaries

- CoCoFlow does not replace the Animator Controller with a second authored
  animation state machine.
- StateGraph writes declared commands; only the Operator touches `Animator`.
- Direct SMB callbacks never mutate StateGraph or committed Context.
- The current module does not provide a generic Playable API, IK, Animation
  Rigging, retargeting, weapon mounts, or full-body animation.
- This document describes current behavior but makes no maturity classification
  for the Animation module in 0.4.0.
