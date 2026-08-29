# Module: Camera

> **Maturity: Mature** · Documentation baseline: `0.4.0` · Updated 2026-08-29
>
> The module originated in 0.3.9. Its current Runtime API is stable and usable;
> that history does not make it transitional or scheduled for replacement.

Camera is a local presentation module for Unity 6 and Cinemachine. It gathers
`CameraRig` instances, selects one available rig by priority, and applies that
rig's priority to its current Cinemachine virtual camera. It does not replace
Cinemachine's orbit, damping, collision, composition, blend, or Timeline
features.

## Runtime model

```text
project code / presentation adapter
        ├─ CameraRig.SetMode(modeId)
        ├─ CameraRig.SetPriority(value)
        └─ CameraRig.SetActive(value)
                       ↓
CameraDirector: active + enabled + current camera
                       ↓
highest priority; newest registration wins ties
                       ↓
winner.CurrentCamera.Priority = winner.Priority
all other registered rig cameras = 0
                       ↓
CinemachineBrain
```

| Type | Responsibility |
|---|---|
| `CameraDirector` | Registers rigs, arbitrates the winner, clears stale runtime priorities, exposes the active rig/camera, and can suspend scheduling. |
| `ICameraDirector` | Stable interface for registration, refresh, activation, priority, scheduling suspension, and `ActiveRigChanged`. |
| `CameraRig` | Owns a rig ID, active flag, priority, current mode ID, and a mode-to-virtual-camera table. |
| `CameraRigCameraEntry` | Maps one project-defined string mode ID to one `CinemachineVirtualCameraBase`. |
| `CameraAimCoupler` | Reads a bound Look action through `InputReader`, applies yaw/pitch to an AimCore transform, and optionally synchronizes another transform. |

## Rig arbitration

`CameraDirector` considers a rig only when it is active, enabled, alive, and
has a camera for its current mode. Higher priority wins. If two candidates have
the same priority, the one registered later wins. Every refresh clears the
configured cameras of all registered rigs to priority `0`, then assigns the
winning camera its rig priority.

`SetSchedulingSuspended(true)` clears all rig priorities and reports no active
rig. This is the handoff point for Timeline or another authored camera system.
Resuming reruns normal arbitration from the current rig state.

Mode IDs are ordinary project strings such as `Explore`, `Aim`, `Dialogue`, or
`Spectate`; CoCoFlow assigns no gameplay semantics to them. `SetCamera` may add
or replace a mapping at runtime. `SetMode` clears the previous camera's runtime
priority when the selected camera changes.

## Registration

A rig can reference an explicit `ICameraDirector` component through
`SetCameraDirector`. When no explicit component is assigned, it resolves the
director registered as `ICameraDirector` in `CoCoServices`; if that service is
not ready, it waits for registration. `registerOnEnable` controls automatic
registration, and disabling a rig unregisters it and clears its camera
priorities.

This service fallback is part of the Camera module's current behavior. It does
not extend the Core Engine maturity declaration to every older
`Runtime/Core/*.cs` service facility.

## Aim coupling

`CameraAimCoupler` requires explicit `InputReader` and Look
`InputActionReference` bindings. It accumulates yaw and clamped pitch using
`Time.deltaTime`, then writes the local rotation of its own transform.

When coupling is enabled:

- a non-ancestor target receives the AimCore's full world rotation;
- an ancestor target receives only the horizontal yaw delta, after which the
  AimCore world aim is restored and its cached local angles are rebased;
- a missing input reader/action simply produces no input update;
- a missing sync target leaves the AimCore rotating without synchronizing
  another object.

There is no automatic search for input, targets, rigs, or Cinemachine cameras.

## Minimal scene assembly

```text
Main Camera
  Camera
  CinemachineBrain

CameraSystem
  CameraDirector

Player
  CameraRig
  AimCore
    CameraAimCoupler
  VCam_Explore
  VCam_Aim

CutsceneAnchor / SpectateAnchor
  CameraRig
  VCam_Cutscene
```

1. Configure Follow/LookAt and camera behavior on each Cinemachine camera.
2. Add mode entries to each `CameraRig` and choose a valid current mode.
3. Bind a director explicitly or let the scene director register as the
   `ICameraDirector` service.
4. Change mode, active state, and priority from local presentation code.

## Boundaries

- Camera state is local presentation state, not network or gameplay authority.
- `CameraRig` does not write Cinemachine Follow/LookAt targets.
- The module does not implement orbit, collision, damping, occlusion, shake,
  Timeline blending, IK, or weapon alignment.
- Priority bands are project conventions; only numeric comparison and
  registration-order tie-breaking are framework behavior.
- “Mature” refers to the current Runtime API and known behavior, not to a claim
  of optimal scheduling performance or complete Camera Editor tooling.
