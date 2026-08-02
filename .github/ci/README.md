# CoCoFlow CI baseline

PR 15.01 has two deliberately small responsibilities:

1. GitHub Actions runs deterministic package checks on Ubuntu and Windows.
2. The maintainer can run the real Unity matrix from a clean snapshot of the
   exact checked-out commit.

Release version/tag/notes policy is not implemented here. PR 17.05 owns the
release workflow, where that policy can be tested against a real release
candidate. PR 15.08 owns required-check rulesets.

## Automatic PR checks

```bash
python3 -m unittest discover -s .github/ci -p "test_*.py" -v
python3 .github/ci/cocoflow_ci.py static
```

The static command reads `git ls-files` and checks:

- strict JSON, including duplicate keys and non-finite constants, and required
  package metadata;
- case-insensitive path collisions and forbidden tracked artifacts;
- Unity `.meta`/GUID integrity;
- asmdef/asmref names, references, and Runtime-to-Editor assembly boundaries;
- `git diff --check` when a base commit is supplied.

A PR must have its comparison base locally; an unavailable PR base is an error.
For a push, an unreachable historical `before` commit is a notice because the
complete repository checks still run and a force-pushed commit may no longer be
fetchable. Normal pushes still run `git diff --check` against their available
base.

It intentionally does not parse C# preprocessor expressions. The Unity compiler
and package tests are the authority for source-level compilation.

`CI Static / gate` is the stable aggregate check name. The workflow uses
read-only permissions, exact action SHAs, cancellation for superseded runs, and
short artifact retention.

## Local Unity clean-host matrix

macOS:

```bash
python3 .github/ci/cocoflow_ci.py unity-matrix
```

Windows:

```powershell
py -3 .github/ci/cocoflow_ci.py unity-matrix
```

The exact maintained Editors are `6000.3.20f1` and `6000.5.5f1`. For
non-standard Unity Hub locations:

```bash
python3 .github/ci/cocoflow_ci.py unity-matrix \
  --editor 6000.3.20f1=/path/to/Unity \
  --editor 6000.5.5f1=/path/to/Unity
```

The runner exports `git archive HEAD` into a temporary package snapshot, so
dirty or untracked local files cannot contaminate evidence labelled with the
commit SHA. For each Editor it creates a disposable host, imports the package,
then runs EditMode and PlayMode separately. Before each mode it removes the old
result at the SHA-bound path, so a rerun cannot consume stale XML. `PASS` requires
Unity exit code 0, a new structurally valid NUnit `test-run`, `result="Passed"`,
zero Failed, and zero Inconclusive. Missing, malformed, empty, failed, or
inconclusive results fail the matrix.

Logs, NUnit XML, package locks, and the SHA-bound summary are written to:

```text
.ci-artifacts/<head-sha>/<os>/<unity-version>/
```

The host is deleted by default. `--keep-host` copies it beside the evidence for
debugging, including import and compilation failures. The runner never copies,
activates, or stores a Unity license.
