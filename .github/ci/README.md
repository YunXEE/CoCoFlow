# CoCoFlow CI baseline

This directory contains the license-free repository checks used by GitHub
Actions and the local clean-host Unity test entry point. The policy is explicit:
GitHub-hosted runners run deterministic static checks, while Unity tests run on
a maintainer-controlled machine with an already activated Editor.

## Static validation

From the package root:

```bash
python3 .github/ci/cocoflow_ci.py static
python3 -m unittest discover -s .github/ci -p "test_*.py" -v
```

On Windows, `py -3` can replace `python3`. The validator reads the package
surface from `git ls-files`; local caches and unrelated untracked files are not
part of the result.

The static report covers strict JSON parsing, package and validation-exception
metadata, case-insensitive path collisions, forbidden generated/binary files,
Unity `.meta`/GUID integrity, asmdef/asmref references, Editor platform
isolation, and guarded `UnityEditor` use in Runtime source.

`policy.json` is the single maintenance point for the exact Unity Editor pins.
Changing either pin requires a dedicated maintenance PR and a new clean-host
baseline. The frozen `6000.3` package minimum remains report-only in PR 15.01;
PR 15.07 owns the metadata change and PR 17.05 owns final verification.

## Local Unity clean-host matrix

macOS:

```bash
python3 .github/ci/cocoflow_ci.py unity-matrix
```

Windows:

```powershell
py -3 .github/ci/cocoflow_ci.py unity-matrix
```

The command discovers the conventional Unity Hub installation paths for
`6000.3.20f1` and `6000.5.5f1`. A non-standard installation can be supplied
without changing policy:

```bash
python3 .github/ci/cocoflow_ci.py unity-matrix \
  --editor 6000.3.20f1=/path/to/Unity \
  --editor 6000.5.5f1=/path/to/Unity
```

For each Editor, the command creates a disposable project under the operating
system temporary directory, references this checkout through a local `file:`
dependency, enables the standard built-in Unity modules, adds the package to
`testables`, imports/compiles, then runs EditMode and PlayMode separately. Test
XML, logs, and a SHA-bound summary are written under:

```text
.ci-artifacts/<final-head>/<os>/<unity-version>/
```

The disposable host is removed by default. `--keep-host` is available only for
debugging a failed import or test run. The command never copies, activates, or
stores a Unity license.

## Release metadata

The release command is normally invoked by `release-policy.yml` for a PR into
`master`:

```bash
python3 .github/ci/cocoflow_ci.py release \
  --head-ref dev/0.4.0 \
  --base-ref master \
  --head-repository YunXEE/CoCoFlow \
  --repository YunXEE/CoCoFlow
```

It rejects unrelated/fork heads, prerelease package versions, branch/version
mismatches, missing dated CHANGELOG entries, stale validation exceptions, and
an already-existing immutable release tag.

## GitHub checks and evidence

- `CI Static / gate` aggregates the Ubuntu and Windows static jobs.
- `Release Metadata / gate` exists only on PRs targeting `master`.
- Reports are retained briefly as workflow artifacts.
- Local Unity results are evidence, not GitHub status checks.

Repository rulesets are intentionally not configured by PR 15.01. PR 15.08
will make the stable check names required after the workflows have run
successfully on real PRs.
