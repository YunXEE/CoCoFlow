## Pre scope

- Pre number/topic:
- Source branch: `pre/NN-topic`
- Target branch: `dev/0.4.0`
- Package version:
- Notion plan/spec page:

## Contract or boundary change

Describe the architectural contract being frozen, preserved, or deliberately
changed. List affected identity, time, lifecycle, Context, StateLogic, Operation,
module, serialization, and package boundaries.

If this PR changes a previously frozen contract, explain why and identify every
downstream Pre that must be updated.

## Scope checks

- [ ] The PR implements only the named Pre and does not pull later-Pre runtime or
      editor work forward.
- [ ] Core dependency direction remains one-way; Core does not reference
      Gameplay, presentation modules, Editor, or project code.
- [ ] Public API, serialized schemas, and user documentation contain no
      Machine/Node vocabulary.
- [ ] There is no cross-Layer reference, transition, signal, call, message, or
      state-query entry point.
- [ ] StateLogic remains pure C# and does not expose Unity objects, Animator, or
      Playable types.
- [ ] StateLogic reads Frozen Context only; side effects and write-back cross an
      explicitly declared Operation boundary.
- [ ] Operation results do not feed back into State or decide a Transition in
      the same Tick; write-back is visible no earlier than the next Tick.
- [ ] State submits Operation commands as value-only payloads through a declared
      Port requirement; no callback or shared mutable result crosses Submit.
- [ ] Missing Required Context or Operation bindings reject startup with a
      structured diagnostic; unchanged Context remains a valid Step input.
- [ ] No compatibility runtime, dual execution path, or automatic 0.3.9 migration
      layer was introduced.
- [ ] Any Sample change is explicitly classified as removal of the 0.3.9 legacy
      surface or as new Pre15/Pre16 0.4 content.
- [ ] This Pre PR targets `dev/0.4.0`, never `master` directly.
- [ ] Every accepted Tick has a finite positive delta; Suspend/Pause produces
      zero Tick and zero Context sampling, and rewind does not use negative delta.

## Serialization and rollback

- Serialized schema/field changes (including stable ID impact):
- Existing asset/prefab impact:
- Rollback or revert path:

- [ ] If this PR owns a serialized stable-ID schema, IDs survive asset copy,
      save, reload, and domain reload without runtime regeneration; otherwise
      Evidence marks this gate N/A and names the owning Pre.
- [ ] This PR can be reverted without leaving a partially migrated package or
      silently changing serialized identity.

## Package surface

- [ ] `package.json` is valid JSON and uses the expected `0.4.0-pre.N` version.
- [ ] Package dependencies changed only when this Pre owns the affected module.
- [ ] No obsolete `samples` manifest property or `CoCoFlow.Runtime.Addon.*`
      assembly is exposed.
- [ ] Public README and module docs distinguish frozen contracts from
      transitional implementations.
- [ ] `CHANGELOG.md` describes user-visible contract, package, or migration
      consequences.
- [ ] New Unity-visible files include their `.meta` files; GUIDs are unique.

## Verification

### Static

- [ ] `git diff --check`
- [ ] All changed JSON and asmdef files parse successfully.
- [ ] Dead-link and obsolete-term scans were reviewed.
- [ ] No unintended generated files, imported project assets, or unrelated user
      changes are included.

### Unity 6 clean host

- Unity version:
- Host project/revision:
- Package reference/tag/commit:

- [ ] Clean package import completed without Console compile errors.
- [ ] Core contract EditMode tests passed.
- [ ] Architecture/package-boundary EditMode tests passed.
- [ ] Relevant existing EditMode tests passed.
- [ ] Relevant PlayMode tests passed.
- [ ] Unity Package Validation Suite passed for the package.
- [ ] Test result files or logs are linked below.

## Evidence

Paste concise command output, Unity test totals, screenshots, logs, or links that
allow a reviewer to verify the checks above.

## Deferred work

List intentionally deferred behavior and its owning Pre. Do not use this section
to silently waive a required acceptance criterion.
