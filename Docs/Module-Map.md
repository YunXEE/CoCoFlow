# Module: Map Region Fidelity

> Contract status: `0.4.0-pre.10` · Updated 2026-07-25
>
> Verification status: `UNVERIFIED` until the Pre10 Unity-host, package,
> Player-build, and Package Validation Suite evidence is recorded.

Map no longer means “push a Scene when a trigger fires.” It is the runtime
authority that resolves overlapping gameplay demand into a transactional
fidelity plan for logical Regions and their optimization Chunks.

## Region and Chunk

A **Region** is one logical unit whose fidelity can be requested and observed:
the continuous wilderness, a castle, a chapel, or a mine may each be a Region.
A Region may have Region-global participants, such as its state service or
representation shell.

A **Chunk** is an optimization partition owned by one Region. It narrows where
scene residency, renderers, colliders, simulation, or project-specific
participants need to run. A Chunk does not invent another policy layer and is
not the unit that demand owners lease.

For a 2 km × 2 km wilderness, the wilderness remains one Region even when its
terrain is compiled into many Chunks. A castle, chapel, and mine can be
independent Regions with their own Profiles and Chunks. Overlapping demand may
keep the wilderness represented while the castle is full-fidelity, and a
high-fidelity request covering one castle Chunk does not raise its sibling
Chunks.

Each managed Scene has exactly one owning Region/Chunk/participant slot.
Logical overlap therefore does not imply ambiguous Scene ownership.

## Capabilities and Profiles

`RegionCapabilityId` is a stable string value. The `cocoflow.*` namespace is
reserved for the ordered standard capabilities:

1. `Represented`
2. `Background`
3. `Enterable`
4. `Full`

Projects and TA packages register namespaced custom capabilities, modes,
participant types, and immutable plan/configuration builders through an
explicit catalog provider. Player builds do not discover these types by
reflection.

`RegionCapabilitySet` can contain standard and custom IDs. Unsupported
capabilities return `UnsupportedCapability`; Map never silently selects a lower
tier.

`CoCoRegionProfile.CurrentSchemaVersion` is `1`. Every Profile serializes a
stable `RegionProfileId`; Editor authoring derives it from the asset GUID, so
copying an asset creates a new identity while moving or renaming it preserves
identity. Player code never uses `AssetDatabase` to recover or rewrite it.

The package supplies an editable five-tier baseline with fixed `RegionTierId`
values:

- `off` has no capability and disables every participant;
- `represented`, `background`, `enterable`, and `full` are the remaining
  default identities;
- each later tier adds fidelity through a strict capability superset;
- standard capabilities keep their fixed order, but may be added together in
  one tier;
- the final tier contains all four standard capabilities;
- custom capabilities may be inserted where the project requires them.

Each participant defines Slot, Type, Required/Optional, phase, order, and
dependencies once. Its Participant-by-Tier matrix then stores exactly one
`RegionParticipantTierSetting` per tier: Enabled, Mode, and
`[SerializeReference]` configuration. Compilation fails closed when a cell is
missing or duplicated, an enabled cell lacks an exact Type/Mode/config
registration, a same-tier dependency is disabled, or a required participant
depends on an optional participant.

A catalog must register its configuration freezer, exact sealed immutable plan
type, concrete candidate type, participant mode, and participant type
explicitly.
Registration snapshots those types. TA plans use pure readonly fields and
`RegionImmutableArray<T>` for copied collections; raw arrays, collection
wrappers over mutable backing stores, Unity Objects, runtime authority, tasks,
and delegates fail compilation. Compilation also validates the strict-superset
ladder, dependency DAG, stable Region/Chunk/slot identity, required bindings,
duplicate `ContentId` ownership, and canonical Scene locators. Compiled plans
are immutable and cached by deterministic input identity; they retain no Unity
Object or runtime Lease.

`CoCoRegionBinding` binds a compiled Profile to project content.
`RegionChunkBinding` is its serialized Chunk schema.
`CoCoRegionChunkAnchor` is the metadata-only root in a managed Chunk Scene.

## Coverage and demand ownership

`RegionCoverage` is either:

- `All`, expanded to every owning Chunk compiled for the Region; or
- a non-empty set of known Chunk IDs.

An unknown or externally owned Chunk rejects the whole create/update operation.
Map preserves the capability-and-Coverage pair of every demand:

- Region-global nodes merge the capabilities from all live demands;
- a Chunk merges only demands whose Coverage contains that Chunk;
- a Chunk's `Full` demand never propagates to a sibling.

Demand uses explicit Scope/Lease ownership:

```csharp
RegionId.TryCreate("world.wilderness", out RegionId regionId);
RegionDemandOwnerId.TryCreate(
    "player.streaming",
    out RegionDemandOwnerId ownerId);
RegionCapabilitySet.TryCreate(
    new[]
    {
        RegionCapabilityId.Represented,
        RegionCapabilityId.Background,
        RegionCapabilityId.Enterable,
        RegionCapabilityId.Full
    },
    out RegionCapabilitySet full);

if (!mapHost.TryCreateDemandScope(
        ownerId,
        out RegionDemandScope scope,
        out CoCoDiagnostic diagnostic))
{
    return;
}

using (scope)
{
    if (!scope.TryDemand(
            regionId,
            full,
            RegionCoverage.All,
            out RegionDemandLease lease,
            out RegionDemandRevision revision,
            out diagnostic))
    {
        return;
    }

    using (lease)
    {
        RegionReadinessResult readiness =
            await lease.WaitUntilReadyAsync(revision, cancellationToken);
        if (readiness.Status == RegionReadinessStatus.Ready)
        {
            // The requested capability/Coverage is committed.
        }
    }
}
```

Use `RegionCoverage.TryCreateChunks(...)` instead of `All` for a non-empty
explicit Chunk set. `TryCreateDemandScope`, `TryDemand`, `TryUpdate`, and
`TryRetryRegion` are main-thread mutations and reject participant-callback
reentry. Lease/Scope disposal is idempotent; disposal requested from a
participant callback is deferred until that callback has returned.

Each lease revision is independent from internal transition generations.
Readiness returns `Ready`, `Cancelled`, `Superseded`, `Failed`, or `Disposed`.
Another owner changing demand may start a new generation but does not supersede
this lease's revision. Only updating or releasing the same lease supersedes its
older revision.

## Cross-Region dependencies

`RegionDependencyRule` declares one source Capability trigger and one target
Region demand with non-empty target Capabilities and `All` or explicit target
Coverage. It has no hand-authored ID; the normalized source/target/capability/
Coverage tuple is its stable fingerprint.

`CompileAll` validates every target Region, target Profile capability, target
Chunk, duplicate rule, self-edge, and cycle in the global Region DAG. It also
enforces globally unique `RegionChunkId` ownership across all Regions.

The Host owns a reserved dependency demand Scope and a distinct Lease for each
active rule. A target must become `Ready` before its source can Prepare or
Commit. Replacements are make-before-break: the prior dependency Lease remains
owned until the source's old-node cleanup completes. Independent Leases preserve
multi-source sharing, and the compiled DAG expands transitive dependencies.
Failure, cancellation, supersede, blocked cleanup, commit fault, and shutdown
release only the dependency ownership permitted by that exact transition state.

## Compiled nodes and transactional transitions

A plan-node identity is:

```text
(RegionId, optional RegionChunkId, ParticipantSlotId)
```

Runtime resolves the first Profile tier whose cumulative capabilities cover the
demand, independently for Region-global nodes and each Chunk. Compiled nodes
store immutable per-tier variants. Only a Mode/config/plan fingerprint change
creates a candidate; a Tier ID rename alone does not. A capability-sensitive
plan additionally fingerprints the resolved effective capabilities. An
unchanged node is reused across transition generations, including its stable
committed resources such as a Pool Scope.

Participants execute in deterministic phase order:

1. Residency
2. Services
3. Simulation
4. Presentation

Within a phase, explicit order and then `ParticipantSlotId` break ties. Cleanup
is the complete reverse order. Region-global nodes cannot depend on Chunk
nodes; Chunk nodes may depend only on Region-global nodes or nodes in the same
Chunk; required nodes cannot depend on optional nodes.

Map owns every candidate before `PrepareAsync` begins and cleans it exactly
once after success, failure, cancellation, replacement, removal, or Host
shutdown. An optional Prepare failure produces `Absent + OptionalDegraded`; it
does not retain a mixed old optional state.

A Prepare failure remains explicitly retryable. A Commit exception enters the
terminal, non-retryable `FaultedCommit` state, stops remaining commits, and
keeps old/candidate ownership for Host shutdown. Cleanup uses a 30-second
unscaled-time default. A timeout enters `BlockedCleanup`, continues observing
the late completion, and requires explicit retry to resolve blocked cleanup
before another transition.

## Content and cold-start Scenes

The built-in Content participant is the only Additive Scene lease authority.
Public participant context does not expose raw `ContentScope`; a project or TA
integration that loads the same managed Scene outside this contract is
unsupported.

A Map-managed Chunk Scene must cold-start with exactly one metadata-only
`CoCoRegionChunkAnchor` root. Every other managed root starts inactive. The
anchor resolves the fragment only after Content returns the exact Scene lease,
and Map scans only that leased Scene. Direct Scenes use a full asset path;
Addressables authoring must resolve to exactly one Scene asset.

The base Map assembly supplies Content, GameObject, Collider, Renderer,
Animator, Particle, and Behaviour participants. A project can register its own
participant and mode implementations without receiving Content release
authority.

Addressables installation only supplies Content's optional loading backend.
Map intentionally does not guess an address-to-Scene mapping. A project using
Addressable Region bindings must provide an
`IRegionAddressableSceneResolver` component on `CoCoMapHost` for Player/runtime
compilation and register the equivalent Editor resolver through
`CoCoMapEditorCatalogProvider.AddressableSceneResolverProvider` for Inspector
and build validation. Both paths must resolve the address to one canonical
Scene asset path; otherwise the complete Binding compilation fails.

## Pooling boundary

`CoCoDefaultRegionCatalogProvider` contains only the seven base participants.
Pool integration is opt-in: a project catalog provider explicitly calls
`RegionBuiltInPoolCatalog.TryRegister(catalog, poolBinding, out diagnostic)`
from `CoCoFlow.Runtime.Modules.Map.Pooling`, where `poolBinding` owns the
project-specific `PoolRuntime`, candidate-Scope, Profile lookup, and committed
Scope publication seams.

A `PoolScope` belongs to a stable committed Map node, not to a transition
generation. An unchanged node reuses its Scope. A changed node prepares a
candidate Scope; after the replacement commits, Map closes the old Scope before
releasing the old Scene lease. Every Chunk-scoped Pool participant must declare
the same Chunk's owning Content slot as a direct dependency; compilation rejects
bindings that could clean Content before Pool. A Region-global Pool node has no
Chunk Scene owner and remains valid without that edge.

Map may force-close only a Scope it owns and only during terminal Host shutdown.
It never force-stops a shared `PoolRuntime`.

## Temporal boundary

The decorator chain is:

```text
Map -> optional Pool -> project restore binding
```

Add `CoCoMapTemporalBinding` to the same StateGraph Host boundary, assign its
`stateGraphHost` and `mapHost`, then make it the Host's Context Restore binding.
If Pool projection is used, assign `CoCoPoolTemporalBinding` as Map's
downstream; otherwise assign the project restore component directly. Pool's
downstream remains the project restore component.

Map captures committed effective capability and Coverage for retention and
availability barriers. It does not encode, restore, or replay Map state or Tier
identity. One internal barrier spans Preview, Confirm, and Cancel; Correction
holds the same barrier from Prepare through Finish. Entry rejects an active
real transition, fault, blocked cleanup, or an already pending flush.

While the barrier is held, demand Create, Update, and Dispose still update
logical demand, revision, and final resolution, but only mark Regions dirty.
They cannot load Content, prepare Pooling, or Prepare/Commit participants, and
Retry is rejected without side effects. When the callback stack has returned,
`CoCoMapHost.LateUpdate` deterministically dispatches only the final resolution
for each dirty Region and coalesces dependency recomputation. Branch-truncation
retention decreases therefore cannot invalidate the active restore chain.

The StateGraph Host inspects the internal decorator reference chain before
startup and rejects direct or indirect cycles such as `Map -> Pool -> Map`
without adding a Map-to-Pool product dependency.

## Host composition and shutdown

`CoCoMapHost` explicitly references its Content Host, bootstrap bindings, and
catalog provider. It does not use a singleton, `FindObjects*`, implicit
registration, or runtime Profile write-back. Unloaded Chunk definitions come
from the bootstrap binding; Scene anchors only resolve fragments after a lease
exists.

Retry first performs a synchronous acceptance check and only then starts an
unrejectable retry operation. A rejected retry cannot mark a waiter Pending or
publish a transition failure.

`OnDisable` begins one idempotent graceful shutdown. `OnDestroy` invokes
terminal fallback only when that shared shutdown task has not completed, and a
disabled Host cannot initialize itself again. Both paths first freeze new
operations, then dispose every demand Scope/Lease, terminal-clean transitions,
clear runtime dictionaries, and finally unregister the Content shutdown
participant. Pending released revisions settle as `Superseded`; subsequent
waits observe `Disposed`, and a previously Ready lease cannot return stale Ready
after shutdown.

Normal terminal order is source-first across the Region dependency DAG, then
Pool, then Content. Content-first shutdown remains an idempotent terminal
fallback that coordinates outstanding ownership; it is not the normal
composition path.

## Internal runtime monitor

The Editor monitor consumes one Map-internal immutable snapshot rather than
exposing mutable runtime ownership. It combines logical demand and revision
state with desired/committed Tier and effective capabilities per Region and
Chunk, participant phase and ownership role (`reused`, `candidate`, `retiring`,
blocked, or fault-retained), dependency blockers, Content ID and Scope/Lease
sequences, Temporal retention/dirty-flush state, degradation and faults, and
old-plus-candidate peak ownership. The snapshot never exposes a raw
`ContentScope`, `ContentLease`, candidate object, or Pool handle.

## Breaking migration

Pre10 removes `MapResourceManager`, `MapStreamTrigger`, and
`MapChunkLoadedEvent` with no compatibility layer, migration component, or
legacy script-GUID preservation.

- `DemandScene` becomes a retained Region demand lease.
- `ReleaseScene` becomes lease disposal.
- loaded events become revision readiness or immutable runtime snapshots.
- legacy Scene content must be reauthored into Profile/Binding/Anchor form.

External TA integrations should depend only on the public Map SDK, register
namespaced capabilities and explicit catalog entries, and keep project-specific
simulation behind participants. Direct Content/Scene ownership outside the
built-in participant contract is unsupported.

## Deferred

Pre10 validation reserves observations for warm transition, large Coverage,
overlapping Regions, old-plus-candidate peak, and cleanup time. Those
measurements remain `UNVERIFIED` until recorded. The runtime does not impose a
hidden performance budget, automatic fidelity downgrade, distance heuristic,
or universal streaming policy. Projects express those choices by creating and
updating demands.
