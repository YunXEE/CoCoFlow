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

## StateGraph reading and control

Add CameraIntent and CameraOperator under the controlling Host, and assign the
same Director to both. Bind them in Host Intent Sources / Operators. The Director
needs an explicit CinemachineBrain; CameraIntent reads that Brain's OutputCamera.
CameraOperator is the StateGraph adapter, not the sole camera controller. Project
code and other presentation systems can also control the Director, Rigs, or Vcams;
the project coordinates those writers. Other Hosts may read the same output.

On Director, configure Rig Mappings: each stable numeric RigId references an
existing CameraRig; that entry's CameraIds map to the Rig's existing string ModeIds.
RigIds and Rig references are unique, as are CameraIds and ModeIds within each Rig.
Do not use list indices or Unity InstanceIDs as persistent identifiers. Zero is a
valid explicitly configured ID. Rig/Vcam registration remains owned by Director.

Author Logic references `CoCoFlow.Runtime.Camera.Contracts`, an engine-free
assembly; MonoBehaviour sources and operators remain in the Camera module.

```csharp
using CoCoFlow.Runtime.Camera.Contracts;

[Intent] public CameraIntentData Camera { get; set; }
[Operation] public CameraOperation CameraControl { get; set; }

// Inside a State callback: write all five for each request.
CameraControl.RigId = 1001;
CameraControl.Active = true;
CameraControl.Priority = 60;
CameraControl.CameraId = 2003;
CameraControl.SchedulingEnabled = true;
```

CameraIntentData exposes world PositionX/Y/Z and RotationX/Y/Z (Euler degrees).
CameraOperation is a discrete five-property request, not a collection of Slots.
Write all five properties: omitted properties do not retain the previous Tick's
values. A Tick with no writes leaves scheduling alone and still captures state.
Multiple writers follow existing per-field coalescing; changing RigId repeatedly
in one Tick does not submit multiple Rig commands. Missing targets reject the
request with a diagnostic and never create cameras. SchedulingEnabled controls
the entire Director; Active only controls the selected Rig.

## Durable save and restore

Put PersistenceContext on the Host object, bind CameraOperator, and enable its
Host persistence-restore switch. Use the existing PersistenceSaveLoadSystem
SaveGame/LoadGame path. CameraOperator implements Durable restoration only;
there is no Camera Temporal switch. Each successful Tick writes Director.GetState
to its owned Outcome slot. Saving encodes the last committed Context; it does not
recapture the Camera at the instant the save button is pressed.

CamSnapshotState stores world position/Quaternion as seven floats, scheduling enabled, and up to
64 Rig records (RigId, Active, Priority, CameraId) in registration order. Its fixed
layout is 1060 bytes: 36 header + 64 * 16. Count is explicit; unused data is zeroed.
A 65th registered Rig or missing mapping rejects the entire capture. These are
64 saved Rigs, not 64 operation Slots, and not a limit on cameras within one Rig.

Restore forwards the saved value to Director.SetState. It validates the complete
pose, mapping and same registered Rig set before applying parameters, restores
relative tie order, refreshes once, resets the Brain blend, forces the selected
Vcam pose, then writes OutputCamera's Transform. No Rig/Vcam is created or removed.
FOV/Lens remain authored on the Rig's cameras. Blends and exact sub-Tick continuity
are not preserved; ordinary save/load accepts a Tick of presentation difference.
Following targets and world state remain the existing save system's responsibility.

### Restore limitation: scheduling suspension

The supported save/load scenario keeps Director scheduling enabled. For a
suspend/resume interval, assume camera state does not change between suspension
and resumption. CameraOperator does not lock out external camera controllers.

Suspending the Director clears its selected Rig; it does not stop the
CinemachineBrain or take ownership of another controller's state. If camera state
changes during that interval, restoring a suspended snapshot can write the saved
OutputCamera Transform immediately, but a later Brain update can overwrite it.
That scenario is outside this version's restore guarantee. The module does not
snapshot or restore the lifecycle/state of external camera controllers.

The snapshot is Durable-only and is excluded from Temporal history. Context frame
buffers are reused; capturing does not add an unbounded per-Tick snapshot list.
Changing the fixed capacity changes the schema and needs explicit save migration.

## Boundaries

- Camera state is local presentation state, not network or gameplay authority.
- `CameraRig` does not write Cinemachine Follow/LookAt targets.
- The module does not implement orbit, collision, damping, occlusion, shake,
  Timeline blending, IK, or weapon alignment.
- Priority bands are project conventions; only numeric comparison and
  registration-order tie-breaking are framework behavior.
- “Mature” refers to the current Runtime API and known behavior, not to a claim
  of optimal scheduling performance or complete Camera Editor tooling.
