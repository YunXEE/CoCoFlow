# Network Context Sync Plan

This document is the implementation plan for the new Network Samples. It intentionally replaces the old NetworkSamples approach.

## Core Idea

CoCoFlow already routes behavior through intent and context:

```text
InputReader / AI / Spline / Network Source
  -> CharacterContextProvider or NetCharacterContextBridge
  -> CoCoInputIntent / CharacterContext facts
  -> CoCoStateController
  -> ordered State Layers
  -> State scripts
  -> Locomotion, Navigation, Animation, Combat
```

The Fusion layer should preserve that shape. It should bridge network data into existing CoCoFlow interfaces instead of creating a parallel player/enemy controller hierarchy.

## Required Bridges

| Script | Interface | Responsibility |
|---|---|---|
| `NetInputReaderBridge` | `ICoCoIntentSource<CoCoInputIntent>` | Convert Fusion input into CoCoFlow input intent. |
| `NetCharacterContextBridge` | `ICoCoContextProvider<CharacterContext>` | Replace or feed the local `CharacterContextProvider` by synchronizing identity, lifecycle, semantic/action state, motion, resources, perception ids, character intent facts, and `CharacterContext.Navigation` facts. |
| `NetEntityReferenceResolver` | optional helper | Resolve stable ids or Fusion object refs into local transforms. |

## Player Flow

```text
Local InputReader
  -> Fusion input
  -> NetInputReaderBridge on StateAuthority
  -> CharacterInputDriver
  -> CharacterContext.Intent
  -> CoCoStateController
```

## Enemy Flow

```text
EnemyBrain and EnemySpline on StateAuthority
  -> CharacterContext
  -> CoCoStateController
  -> State scripts
  -> CharacterNavigationMotor / CharacterLocomotion
```

Proxies should not run `EnemyBrain` or `EnemySpline`; they apply synchronized Context snapshots.

## Boundaries

- No Fusion dependency in package Runtime.
- No dedicated network State controller.
- No direct synchronization of Unity `Transform` references.
- No direct network ownership of Animator, Locomotion, or Combat scripts.
- A character should expose one synchronized `CharacterContext`; navigation facts are part of that payload, not a second State context.
- A character state system should keep one `CoCoStateController` with explicit ordered State Layers. Network adapters should not create parallel controller trees.
- Animation Rigging and IK data should enter through explicit Context facts or operation components only after the business project stabilizes that contract.
