# Module: Animation

> Pre1 transition note (`0.4.0-pre.1`): `AnimHandler`, `AnimEventSmb`, and the
> current Animator Controller tooling are retained 0.3.9 implementations. They
> remain historical/transition code until Animation V2 in Pre11 and are not
> frozen 0.4 APIs.
>
> An Animator or `StateMachineBehaviour` callback must never call StateGraph,
> request an immediate transition, or mutate the current Frozen Context Frame.

Animation is a thin Animator / SMB utility layer. It does not contain a built-in
IK solver, rig graph, weapon mount system, procedural locomotion system, or
full-body animation stack.

## Topology

```mermaid
flowchart TD
  LegacyCaller["Legacy 0.3.9 gameplay caller"] -->|"Play / CrossFade / Set parameter"| AnimHandler["AnimHandler"]
  AnimHandler --> Animator["Animator"]
  Animator -->|"StateMachineBehaviour callbacks"| AnimEventSmb["AnimEventSmb"]
  AnimEventSmb -->|"normalized-time event"| AnimHandler
  AnimHandler -->|"presentation notification only"| ProjectListener["Project presentation listener"]
  Injector["AnimEventSmbInjector"] -->|"editor-time injection"| AnimatorController["Animator Controller"]
  Editor["AnimEventSmbEditor"] -->|"event list editing"| AnimEventSmb
```

## Components

| Component | Responsibility |
|---|---|
| `AnimHandler` | Thin Animator facade for `Play`, `CrossFadeInFixedTime`, parameter writes, layer weight, speed, and SMB event relay. |
| `AnimEventSmb` | `StateMachineBehaviour` that triggers named events when normalized animation time reaches configured thresholds. It stores per-Animator state so shared SMB assets do not leak trigger flags between instances. |
| `AnimEventSmbInjector` | Editor window that injects `AnimEventSmb` into all states of an Animator Controller, with an option to clear existing instances first. |
| `AnimEventSmbEditor` | Custom inspector for editing `AnimEventSmb` event names and trigger times. |

## Legacy 0.3.9 Runtime Usage

Place `AnimHandler` beside the character `Animator`:

```text
PlayerRoot
  Visual
    Model
      Animator
      AnimHandler
```

The retained 0.3.9 runtime allows project gameplay code to call the small facade
below. This example is documentation for existing transition code only. New 0.4
StateLogic must use the Animation Operation boundary to be introduced in Pre11
and must not resolve `AnimHandler` directly.

```csharp
animHandler.CrossFadeAnimation("Move", 0.1f);
animHandler.SetFloat("MoveSpeed", speed);
animHandler.SetBool("IsGrounded", isGrounded);
```

For animation-authored events, subscribe to `OnSpecificFrameEvent`,
`OnAnimStateEnter`, or `OnAnimStateExit` from project-side code. CoCoFlow keeps
the event payload as a string so projects can route it into their own gameplay
contracts without the Animation module learning business-specific semantics.
These callbacks may update presentation-local state or enqueue data for a later
Operation/Source boundary. They may not call StateGraph or make a write visible
inside the current Tick.

## Editor Workflow

Open `CoCoFlow/AssetPipeline/SMB 注入器`, assign an Animator Controller, then run
the injection. The tool traverses Animator layers and nested state machines,
adding `AnimEventSmb` to states that do not already contain one. When the clear
option is enabled, existing `AnimEventSmb` behaviours are removed before
reinjection.

After injection, configure each state's event list in the `AnimEventSmb`
inspector. `Trigger Time` is normalized state time in the `0..1` range.

## Boundaries

- Current SMB callbacks are one-way presentation notifications; there is no
  callback edge into StateGraph.
- Pre11 owns the Playable graph, animation Operation contracts, transition
  animation, combo timing, and root-motion ownership decisions.
- Does not create a Rigging State Layer.
- Does not extend `CharacterContext`.
- Does not include Foot IK, hand IK, weapon mounts, full-body animation,
  retargeting, or network synchronization.
- Does not add Unity Animation Rigging or Final IK as a package dependency.
- Projects can still use external rig or IK tools behind their own State Layer
  operation scripts.
