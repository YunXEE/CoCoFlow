# Changelog

All notable changes to CoCoFlow are documented in this file.

The project uses `0.4.0-pre.N` for prerelease packages. The 0.4 line targets new
projects and does not include a migration runtime for 0.3.9 projects.

## [0.4.0-pre.1] - 2026-07-13

### Added

- Engine-independent Core contract domains for graph/runtime identity, time,
  lifecycle, diagnostics, and StateLogic dependency roles.
- Getter-only, reference-free Context Section requirements and declared
  Operation Port entries; Operation commands cross the State boundary as
  unmanaged values with no synchronous gameplay-result channel.
- Contract and architecture gates that keep Core independent from Gameplay,
  presentation modules, Editor code, Animator, and Playables.
- A pull-request checklist for Pre scope, package surface, Unity host validation,
  and contract changes.

### Changed

- Froze Graph, runtime, timeline, and clock identities as immutable Runtime
  values; Unity asset serialization schemas and ID authoring remain owned by
  the Pre3 StateGraph Asset/Compiler work.
- Tightened Context Section validation to accept only parameterless abstract
  instance getters and reject indexers, default/static members, fields, native
  handles, and by-ref-like fact values.
- Made failed TimelinePosition creation return an invalid sentinel, rejected
  malformed Diagnostic domain/code values, and allowed Created Runtime values
  to Dispose explicitly before their first Run.
- Assigned Port/Command affinity, handle-free Command shape validation, and
  StateLogic authoring dependency enforcement to their owning compiler and
  Operation Pres instead of overstating what the Pre1 marker types guarantee.
- Reframed Context execution as
  `Source -> Frozen Frame -> Independent Layers -> Operation -> Next Frame`.
- Set the package version to `0.4.0-pre.1` and marked the package as a
  Core-contract prerelease.
- Updated public documentation to distinguish frozen 0.4 contracts from
  transitional 0.3.9 module implementations.

### Removed

- The 0.3.9 Samples and Add-on import surface from package metadata and public
  setup/documentation.
- The 0.3.9 read-only state graph inspection workflow from the 0.4 public
  authoring surface.

### Transitional

- The 0.3.9 CCS Runtime remains temporarily to preserve compilation and
  regression evidence during the contract freeze. Pre4 replaces it; its APIs and
  Unity-lifecycle behavior are not 0.4 compatibility guarantees.
- Existing Camera, Animation, Persistence, Gameplay, and Editor implementations
  remain transitional until their owning Pres replace or retire them.

## [0.3.9] - 2026-07-01

- Historical CCS Runtime line. Existing projects should remain pinned to this
  revision family rather than mixing 0.3.9 behavior with 0.4 prereleases.
