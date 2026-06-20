# Network Context Sync Plan

This document is the implementation plan for the new Network Samples. It intentionally replaces the old NetworkSamples approach.

## Core Idea

CoCoFlow already routes behavior through intent and context:

```text
InputReader or AI Provider
  -> CoCoInputIntent / CharacterContext / CharacterNavigationContext
  -> CoCoStateMachineController
  -> State scripts
  -> Locomotion, Navigation, Animation, Combat
```

The Fusion layer should preserve that shape. It should bridge network data into existing CoCoFlow interfaces instead of creating a parallel player/enemy controller hierarchy.

## Required Bridges

| Script | Interface | Responsibility |
|---|---|---|
| `NetInputReaderBridge` | `ICoCoIntentSource<CoCoInputIntent>` | Convert Fusion input into CoCoFlow input intent. |
| `NetCharacterContextBridge` | `ICoCoContextProvider<CharacterContext>` | Synchronize health, lifecycle, identity, motion, perception ids, and character intent facts. |
| `NetCharacterNavigationContextBridge` | `ICoCoContextProvider<CharacterNavigationContext>` | Synchronize navigation mode, destination, route progress, warp requests, owner facts, and desired speed. |
| `NetEntityReferenceResolver` | optional helper | Resolve stable ids or Fusion object refs into local transforms. |

## Player Flow

```text
Local InputReader
  -> Fusion input
  -> NetInputReaderBridge on StateAuthority
  -> CharacterInputDriver
  -> CharacterContext.Intent
  -> CoCoStateMachineController
```

## Enemy Flow

```text
EnemyBrain and EnemySpline on StateAuthority
  -> CharacterContext / CharacterNavigationContext
  -> CoCoStateMachineController
  -> State scripts
  -> CharacterNavigationMotor / CharacterLocomotion
```

Proxies should not run `EnemyBrain` or `EnemySpline`; they apply synchronized Context snapshots.

## Boundaries

- No Fusion dependency in package Runtime.
- No dedicated network state machine.
- No direct synchronization of Unity `Transform` references.
- No direct network ownership of Animator, Locomotion, or Combat scripts.
- Context bridges may be separate per Context. A character can hold multiple Contexts and multiple state machine systems.
