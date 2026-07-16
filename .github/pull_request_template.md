## Pre scope

- Pre number/topic:
- Source branch: `pre/NN-topic`
- Target branch: `dev/0.4.0`
- Package version:
- Notion plan/spec page:

## Contract or boundary change

Describe the architectural contract being frozen, preserved, or deliberately
changed. List affected identity, time, lifecycle, IntentFrame, OperationFrame,
ContextFrame, Section, Mailbox, Operator, serialization, and package boundaries.

If this PR changes a previously frozen contract, explain why and identify every
downstream Pre that must be updated. Historical Pre release notes must remain
intact even when their proposed model is superseded.

## Scope checks

- [ ] The PR implements only the named Pre and does not pull later-Pre runtime or
      editor work forward.
- [ ] Core dependency direction remains one-way; Core does not reference
      Gameplay, presentation modules, Editor, or project code.
- [ ] Public API, serialized schemas, and user documentation contain no
      Machine/Node vocabulary.
- [ ] There is no cross-Layer reference, transition, signal, call, message, or
      state-query entry point.
- [ ] StateLogic remains pure C# and does not expose Unity objects, Animator,
      Playable, EventBus, EventAgent, EventEnvelope, EventRouter, or EventInbox
      types.
- [ ] StateGraph reads only the current frozen IntentFrame and Previous
      ContextFrame; it cannot observe an Outcome produced during the same Tick.
- [ ] IntentFrame does not reuse OperationFrame Sections and does not enter
      Temporal or Durable storage.
- [ ] OperationFrame is a complete read-only execution guide; identical Section
      identities deduplicate, shape-equal identities remain distinct, and
      Section-to-Section inheritance is rejected.
- [ ] Every compiled Operation provide contains the complete Section Shape, and
      Catalog/Registry binding compares every field rather than treating the
      Shape fingerprint as correctness proof.
- [ ] FrozenConfig snapshots are framework-owned canonical Schema values:
      fields are exact and complete, arrays are defensively copied, writers are
      sealed, and authors cannot supply arbitrary frozen objects or hashes.
- [ ] ContextFrame is the complete committed state of one Actor and contains no
      Inbox, IntentFrame, raw Envelope, unpublished Outbox, or Unity Object graph.
- [ ] Missing or invalid graph-internal Intent requirements, Operation provides,
      and ContextFrame state requirements reject compilation with a structured
      diagnostic; actual Host/scene binding coverage rejects startup in Pre4.
- [ ] A Graph compile with any Error produces no partial/pruned compiled graph;
      Warnings remain non-blocking and location-capable.
- [ ] Compiled graph data is immutable and shared only as read-only topology;
      per-Host activation, lifecycle, Inbox, and Context state is never cached in
      the Asset result.
- [ ] Unchanged Graph/content/catalog/schema keys share both successful and
      failed compile-result identities; failed results keep `Graph == null`, and
      a throwing cache factory is evicted.
- [ ] Cross-Object gameplay input follows
      `EventPacket -> Router -> EventInbox -> Event-to-Intent Adapter`; no raw
      callback enters StateLogic.
- [ ] Pre3 compiles graph-level Event-to-Intent static declarations into the
      Intent manifest without Adapter instances; Pre4 owns missing, extra,
      duplicate, and type-exact runtime coverage and binding.
- [ ] ContextFrame commit succeeds before final EventSequence allocation and
      EventOutbox publication. Failure produces zero Event and zero cross-Actor
      side effect.
- [ ] No compatibility runtime, dual execution path, or automatic 0.3.9 migration
      layer was introduced.
- [ ] Any Sample change is explicitly classified as removal of the 0.3.9 legacy
      surface or as new Pre15/Pre16 0.4 content.
- [ ] This Pre PR targets `dev/0.4.0`, never `master` directly.
- [ ] Every accepted Tick has a finite positive delta; Pause/Suspend produces
      zero Tick and zero Intent sampling, and rewind does not use negative delta.

## Serialization and rollback

- ContextFrame Descriptor/Slot changes (including stable ID impact):
- Temporal/Durable/Derived projection impact:
- Existing asset/prefab impact:
- Rollback or revert path:

- [ ] If this PR owns a serialized stable-ID schema, IDs survive asset copy,
      Layer/subtree duplication, save, reload, and domain reload without runtime
      regeneration; otherwise Evidence marks this gate N/A and names the owning
      Pre.
- [ ] Inbox, IntentFrame, EventAgent subscriptions, deduplication windows, and
      unpublished Outbox candidates are excluded from persistence.
- [ ] This PR can be reverted without leaving a partially migrated package or
      silently changing serialized identity.

## Package surface

- [ ] `package.json` is valid JSON and uses the expected `0.4.0-pre.N` version.
- [ ] Every `ValidationExceptions.json` entry targets the same package version;
      new exceptions are justified individually rather than added broadly.
- [ ] Package dependencies changed only when this Pre owns the affected module.
- [ ] No obsolete `samples` manifest property or `CoCoFlow.Runtime.Addon.*`
      assembly is exposed.
- [ ] Public README and module docs distinguish authoritative 0.4 contracts from
      transitional 0.3.9 implementations.
- [ ] `CHANGELOG.md` describes user-visible contract, package, migration, and
      deferred-work consequences without rewriting prior release history.
- [ ] New Unity-visible files include their `.meta` files; GUIDs are unique.

## Verification

### Static

- [ ] `git diff --check`
- [ ] All changed JSON and asmdef files parse successfully.
- [ ] Dead-link, obsolete-term, and forbidden-dependency scans were reviewed.
- [ ] Fixed Layout hot paths contain no runtime reflection or string-key lookup.
- [ ] Pure StateGraph compiler/validator assemblies have no Unity reference;
      Unity authoring and Editor tooling remain in one-way dependent asmdefs.
- [ ] Editor Analyze and Player build preflight validate the complete resolved
      dependency closure of every registered author type; direct-reference
      guards are not reported as proof of transitive isolation.
- [ ] No unintended generated files, imported project assets, or unrelated user
      changes are included.

### Unity 6 clean host

- Unity version:
- Host project/revision:
- Package reference/tag/commit:

- [ ] Clean package import completed without Console compile errors.
- [ ] Core contract EditMode tests passed.
- [ ] State Flow Frame/Section/Descriptor EditMode tests passed.
- [ ] Mailbox, Intent projection, Commit protocol, and Restore contract tests
      passed for the scope owned by this Pre.
- [ ] Architecture/package-boundary EditMode tests passed.
- [ ] StateGraph compiler, validator, manifest, stable-ID serialization, copy,
      Layer/subtree duplication, cache, and diagnostic navigation EditMode tests
      passed.
- [ ] Relevant existing EditMode and PlayMode tests passed.
- [ ] Warmed steady-state Frame access, Intent arbitration, Mailbox processing,
      and Codec paths meet the allocation gate owned by this Pre.
- [ ] Required generic Section, EventPacket, Adapter, Codec, StateGraph snapshot,
      descriptor, and manifest paths passed an IL2CPP/AOT smoke build with the
      configured stripping level.
- [ ] Unity Package Validation Suite passed for the package.
- [ ] Test result files, allocation/performance evidence, build logs, or
      screenshots are linked below.

## Evidence

Paste concise command output, Unity test totals, allocation/benchmark summaries,
AOT build results, Package Validation output, screenshots, logs, or links that
allow a reviewer to verify the checks above.

## Deferred work

List intentionally deferred behavior and its owning Pre. Do not use this section
to silently waive a required acceptance criterion. For State Flow work, call out
at least the relevant Pre3 Compiler, Pre4 Host/Router, Pre5 Operator/Commit,
Pre6 Temporal, Pre13 Persistence, or Pre16 certification boundary.
