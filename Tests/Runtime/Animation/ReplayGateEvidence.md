# Pre11 Animator Exact-Replay Gate Evidence

Date: 2026-07-25  
Unity Editor: 6000.3.20f1

## Frozen rule

Exact replay can ship only when G1, G2, and G3 are all demonstrated as `GO`.
An approximate `Play(normalizedTime)` restore is not an accepted fallback.

## Gate result

| Gate | Result | Evidence |
|---|---|---|
| G1: positive per-Tick replay | `UNVERIFIED` | `AnimatorControllerPlayable` has the required positive commands and `PlayableGraph.Evaluate(float)`, but API presence does not prove identical controller and pose state. The runtime comparison did not execute. |
| G2: bounded rebuild anchor | `NO-GO` | ACP exposes readers for current, next, and transition state, but no public inverse/snapshot contract that can restore their composite hidden state. A journal from controller creation is not bounded by Temporal history capacity. |
| G3: isolated targetless candidate and zero-delta swap | `UNVERIFIED` | `AnimationPlayableOutput.SetTarget` and `Evaluate(0)` exist, but no executed fixture proved that a null-target controller advances or that binding/swap is side-effect-free. |

Overall result: **DEFER exact Animator replay in Pre11**. The forward
`AnimatorControllerPlayable` Operator may proceed, but it must not register an
exact Temporal capability or silently substitute approximate restoration.

## Attempted runtime proof

The following batch command was attempted against the existing CoCoLab host:

```text
/Applications/Unity/Hub/Editor/6000.3.20f1/Unity.app/Contents/MacOS/Unity
  -batchmode -nographics -quit
  -projectPath /Users/UnityDev/CoCoLab
  -executeMethod CoCoFlow.Tests.Editor.Animation.ReplayFixtureAssetBuilder.Generate
  -logFile /private/tmp/pre11-replay-fixture-host.log
```

The Editor repeatedly failed the LicensingClient handshake with:

```text
ResponseCode: 505
ResponseStatus: Unsupported protocol version '1.18.1'.
```

The process was stopped before fixture generation or Gate execution. No
generated controller/clip assets are committed because unvalidated fixtures
would falsely imply runtime proof.

## Reopen conditions

Reopen exact replay only when all of the following can be tested:

1. Two separately created controller graphs receive the same retained journal
   and produce identical state, transition, parameter, clip-weight, root-delta,
   and pose fingerprints at every Tick.
2. An exact anchor can be captured/restored with memory bounded by the retained
   Temporal window, without a hidden Animator clone or a custom Animator
   replacement.
3. A replay candidate can advance without touching the live Animator; binding
   and `Evaluate(0)` emit no gameplay event/root/completion, and the next
   positive Tick stays identical.
