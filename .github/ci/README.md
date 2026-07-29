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

- strict JSON and required package metadata;
- case-insensitive path collisions and forbidden tracked artifacts;
- Unity `.meta`/GUID integrity;
- asmdef/asmref names, references, and Runtime-to-Editor assembly boundaries;
- `git diff --check` when a base commit is supplied.

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
then runs EditMode and PlayMode separately. A missing, malformed, or empty NUnit
result is a failure even if Unity exits with code 0.

Logs, NUnit XML, package locks, and the SHA-bound summary are written to:

```text
.ci-artifacts/<head-sha>/<os>/<unity-version>/
```

The host is deleted by default. `--keep-host` copies it beside the evidence for
debugging. The runner never copies, activates, or stores a Unity license.
