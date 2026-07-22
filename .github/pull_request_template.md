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
- [ ] New 0.4 public API, serialized schemas, Editor, and user documentation use
      Graph/Layer/State/Transition concepts rather than Machine/Node; retained
      0.3.9 Controller names remain transitional and are not migrated by this PR.
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
      It is the sole retainable/restorable Actor commit record; Graph, Clock, and
      Claim caches are mirrors or can be rebuilt uniquely from it.
- [ ] Every non-Derived Context Slot has exactly one Graph-state, Graph-value,
      canonical-Claim, Operator-Outcome, or Actor-binding producer; Derived Slots
      remain exclusively owned by their declared rebuilders.
- [ ] Project Context bindings supply the actual Layout defaults. Their semantic
      fingerprints are trusted Manifest-compatibility declaration tokens, not
      framework-computed canonical hashes of `defaultValue`.
- [ ] Missing or invalid graph-internal Intent requirements, Operation provides,
      and ContextFrame state requirements reject compilation with a structured
      diagnostic; actual project runtime binding coverage rejects Host startup.
- [ ] A Graph compile with any Error produces no partial/pruned compiled graph;
      Warnings remain non-blocking and location-capable.
- [ ] Compiled graph data is immutable and shared only as read-only topology;
      per-Host activation, lifecycle, Inbox, and Context state is never cached in
      the Asset result.
- [ ] The only required Actor component is `CoCoStateGraphHost` with one
      `CoCoStateGraphAsset`; Runtime, Clock, Inbox, Router, Logic, Condition, and
      Memory are not components, and Host performs no scene/child/legacy scan.
- [ ] Runtime binding coverage is immutable and exact for every State,
      Condition, Memory, Intent Source, and Event Adapter; a mismatch leaves the
      Host in Created with zero callback, Tick, or Router registration.
- [ ] Context/Operator/Claim/Actor/Outbox transaction preflight completes before
      Clock or Runtime creation; invalid setup invokes no Logic, Condition,
      Memory factory/reset/fingerprint, Graph capture, Operator, or Actor callback.
- [ ] Event Adapter execution follows Asset declaration-list order preserved by
      the compiled manifest; the binding Provider cannot reorder semantics.
- [ ] Start only selects initial leaves. Enter is parent-to-child, mandatory
      Update is root-to-leaf, Exit is leaf-to-parent, and a Transition keeps its
      source path effective until the target enters on the next accepted Tick.
- [ ] `Created` cannot Stop; Host public `TryDispose` accepts only `Created` or
      `Stopped`; Runtime `Dispose()` and Unity destruction force live cleanup
      internally through `Stopped`. Startup/Tick lifecycle reentry is rejected,
      and destruction cannot publish or commit an unresolved candidate.
- [ ] Transition endpoints are leaves, every outgoing Priority is explicit and
      unique per source leaf, and Update can request only predeclared handles.
      Completion and InterruptPolicy are not present.
- [ ] Within one Activation, ActionProgress is finite and monotonically
      non-decreasing; equal values may stall, while a decrease cancels the
      candidate and latches Fault. Rollback restores committed authority and
      never permits progress to move backwards.
- [ ] Operation writes use fixed Layer/path-depth composition rank, Finalize
      consumes no sequence or LastTick, and only the composite Actor commit
      barrier may accept staged path/memory/clock/context/claim/sequence state.
- [ ] The explicit Host Operator order is validated before Running; its deduplicated
      requirements exactly cover Graph Operation-provides, and null, destroyed,
      non-interface, duplicate, or nested-Host-crossing entries are rejected.
- [ ] Claims are arbitrated before every real Operator callback; multi-Claim
      Operators win all-or-none, and `ClaimDenied` performs no callback or write
      without faulting an otherwise valid Tick.
- [ ] Outcome writers expire after their callback and can write only the owning
      Operator's declared non-Derived Context Slots. The first Tick reads layout
      defaults and the first successful Context commit is Revision 1.
- [ ] Unchanged Graph/content/catalog/schema keys share both successful and
      failed compile-result identities; failed results keep `Graph == null`, and
      a throwing cache factory is evicted.
- [ ] Cross-Object gameplay input follows
      `EventPacket -> Router -> EventInbox -> Event-to-Intent Adapter`; no raw
      callback enters StateLogic.
- [ ] Pre3 compiles graph-level Event-to-Intent static declarations into the
      Intent manifest without Adapter instances; Pre4 owns missing, extra,
      duplicate, and type-exact runtime coverage and binding.
- [ ] One Graph uses at most one EventDomain; no declarations create no
      Inbox/Router, local ingress bypasses Router, and cross-Actor routing uses
      atomic `CoCoEventPacket<TEvent>` rather than `PublishWithEnvelope`.
- [ ] ContextFrame commit succeeds before final EventSequence allocation and
      EventOutbox publication. Failure produces zero Event and zero cross-Actor
      side effect.
- [ ] All Event types of one GraphInstance/Epoch share one contiguous committed
      EventSequence range and publish in Host Operator plus append order. Trace
      entries contain no payload, Unity Object, mutable Frame, Router, or Inbox.
- [ ] Trace records accepted Transition Candidates in compiled order and the
      Winner separately; value-only Frame references carry exact Layout identity
      and never retain a ContextFrame or invent first-Tick Revision 0.
- [ ] Pre5 Restore validation is pure and complete for Context, Clock, Graph, and
      Claims. Its internal prepare/apply seam swaps Context and cache mirrors
      without callbacks; public history, world correction, Resume, and new-Epoch
      orchestration remain owned by Pre6.
- [ ] No compatibility runtime, dual execution path, or automatic 0.3.9 migration
      layer was introduced.
- [ ] Any Sample change is explicitly classified as removal of the 0.3.9 legacy
      surface or as new Pre15/Pre16 0.4 content.
- [ ] This Pre PR targets `dev/0.4.0`, never `master` directly.
- [ ] Every accepted Tick has a finite positive delta; Pause/Suspend produces
      zero Tick and zero Intent sampling, and rewind does not use negative delta.
- [ ] Actor TimeScale is finite and positive; Unity Update/FixedUpdate schedules
      at most one CoCoTick per frame, while each Manual call is one independent
      Tick without accumulator or catch-up.

### Pre7 StateGraph Editor and debugger

- [ ] New 0.4 Editor and public documentation use only Graph, Layer, State, and
      Transition vocabulary. Transitions are same-Layer leaf-to-leaf and expose
      Conditions, one Window, and a source-unique Priority; Interrupt is not an
      authoring field.
- [ ] Every topology write uses the Editor authoring-operation boundary and one
      Unity Undo group per accepted gesture. Rejected operations do not dirty
      serialized data or create Undo history.
- [ ] State-subtree deletion removes every incident Transition. Deleting an
      initial State requires an explicit valid surviving sibling when one
      exists; with no survivor, the user must explicitly confirm clearing the
      reference and leaving a compiler-invalid draft.
- [ ] Copy/paste is limited to the same Asset, assigns new State/Transition IDs,
      remaps only copied internal references, and drops external Transitions.
      Cross-Asset and cross-session clipboard transfer remain deferred.
- [ ] State positions use stable State IDs in a separately versioned Editor
      layout. Layout and session state are excluded from runtime schema v1,
      compiler snapshots, content fingerprints, and compilation-cache keys;
      opening/importing an Asset does not synthesize layout or dirty it.
- [ ] Layer/breadcrumb/foldout/pan/zoom/search/selection state survives Domain
      Reload through session storage only. Diagnostics are recomputed, and only
      a still-valid selected diagnostic location is restored.
- [ ] The deterministic internal Catalog overlay exposes exactly the existing
      Intent, Graph Operation, and ContextFrame State manifests. Simple creates
      two generic leaf States and one same-Layer Transition; Combo creates
      `Step1 -> Step2 -> Step3 -> Step4 -> Exit`; neither adds gameplay logic,
      Samples, public category metadata, or a fourth Manifest.
- [ ] Host stores user-confirmed ordered scene references for Intent Sources,
      Event Adapters, Operators, Actor Context, and Restore, while the Project
      Provider remains authoritative for Catalog/types/factories/Codecs/defaults
      and AOT construction. Suggestions are unsaved until confirmed and Running
      configuration is read-only.
- [ ] The debugger snapshot is an internal immutable point-in-time copy of the
      latest committed authority and is distinct from identity-only Trace.
      Trace capacity defaults to zero and cannot change while Running; neither
      surface exposes payload, mutable Frame, retained Context handles, Inbox,
      Envelope, or reflected private fields.
- [ ] A healthy Suspended Host can execute exactly one ordinary forward Tick
      with an explicit finite positive delta under every Driver. Success returns
      to Suspended; faults and world-correction requirements remain real. This
      operation is not Rewind or authority-neutral Preview.

## Serialization and rollback

- ContextFrame Descriptor/Slot changes (including stable ID impact):
- StateGraph Editor layout/version/session-state impact:
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
- Final PR Head SHA under test:

- [ ] Clean package import completed without Console compile errors.
- [ ] Core contract EditMode tests passed.
- [ ] State Flow Frame/Section/Descriptor EditMode tests passed.
- [ ] Mailbox, Intent projection, Commit protocol, and Restore contract tests
      passed for the scope owned by this Pre.
- [ ] Architecture/package-boundary EditMode tests passed.
- [ ] StateGraph compiler, validator, manifest, stable-ID serialization, copy,
      Layer/subtree duplication, cache, and diagnostic navigation EditMode tests
      passed.
- [ ] Pre7 Editor add/delete/rename/reorder/transition/Undo/Redo, initial-State
      replacement-or-confirmed-empty, same-Asset copy/paste, preset, and
      diagnostic-navigation smoke tests passed without raw Inspector mutation.
- [ ] State positions survived save/reload and identity remap; Domain Reload
      restored only session view state, recomputed diagnostics, and did not dirty
      the Asset or change content/cache fingerprints.
- [ ] Relevant existing EditMode and PlayMode tests passed.
- [ ] StateGraph Runtime EditMode and Host lifecycle/event PlayMode suites passed
      for this Pre's focused and full-package runs.
- [ ] Committed debugger snapshot, default-disabled/fixed-while-Running Trace,
      and Suspended one-step behavior passed for Update, FixedUpdate, and Manual
      Drivers, including rejection/fault/world-correction paths.
- [ ] After 100 warm-up and 10,000 measured iterations, normal Step, layered
      composition, Transition, Router-to-Inbox-to-Step, and Suspend/Resume meet
      the zero steady-state managed-allocation gate.
- [ ] Required generic Section, EventPacket, Adapter, Codec, StateGraph snapshot,
      descriptor, and manifest paths passed an IL2CPP/AOT smoke build with the
      configured stripping level.
- [ ] macOS Universal IL2CPP with High Managed Stripping completed as the smoke
      target for this Head.
- [ ] Unity Package Validation Suite passed without adding an unexplained
      exception, and Unity Package Validation Suite package/version metadata
      matches `package.json`.
- [ ] Test result files, allocation/performance evidence, build logs, or
      screenshots are linked below.

## Evidence

Paste concise command output, Unity test totals, allocation/benchmark summaries,
AOT build results, Unity Package Validation Suite output, screenshots, logs, or
links that allow a reviewer to verify the checks above.

- Reviewed final PR Head SHA:
- Clean UPM Host import/result:
- Focused EditMode/PlayMode totals:
- Full-package EditMode/PlayMode totals:
- Pre7 Editor/Domain Reload/debugger smoke result:
- Allocation summary (100 warm-up / 10,000 measured):
- macOS Universal IL2CPP + High Stripping artifact/log:
- Unity Package Validation Suite result/log:
- asmdef/JSON/GUID/dependency/hot-path audit summary:

If any fix changes the Head after evidence was captured, mark the affected
evidence stale and rerun it. A green run from an earlier commit is not final-head
evidence.

## Deferred work

List intentionally deferred behavior and its owning Pre. Do not use this section
to silently waive a required acceptance criterion. For State Flow work, call out
at least the relevant Pre3 Compiler, Pre4 Host/Router, Pre5 Operator/Commit,
Pre6 Temporal, Pre7 Editor/Debugger, Pre13 Persistence, or Pre16 certification
boundary.
