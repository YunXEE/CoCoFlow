# CoCoFlow CI baseline

PR 15.01 has two deliberately small responsibilities:

1. GitHub Actions runs deterministic package checks on Ubuntu and Windows.
2. The maintainer runs the real Unity matrix locally in manually created,
   persistent validation projects and records evidence for the exact checked-out
   commit.

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

A PR must have its comparison base and enough upstream history locally; an
unavailable PR base is an error. PRs run `git diff --check` from the merge base,
so changes made only on an updated target branch are not attributed to the PR.
For a push, an unreachable historical `before` commit is a notice because the
complete repository checks still run and a force-pushed commit may no longer be
fetchable. Normal pushes compare the `before` and `HEAD` trees directly.

It intentionally does not parse C# preprocessor expressions. The Unity compiler
and package tests are the authority for source-level compilation.

`CI Static / gate` is the stable aggregate check name. The workflow uses
read-only permissions, exact action SHAs, cancellation for superseded runs, and
short artifact retention.

## Local Unity validation projects

Unity projects are created and maintained by the user, not by this repository or
the Python validator. Each project uses its matching Editor patch, references the
CoCoFlow checkout as a local package, and lists `com.yunxee.cocoflow` in the
manifest's `testables` array.

The maintained local projects on macOS are:

```bash
HOST_6000_3=/Users/UnityDev/CoCoFlow_Test_6000_3
HOST_6000_5=/Users/UnityDev/CoCoFlow_Test_6000_5
```

Before collecting evidence, confirm that the package checkout is on the intended
Final Head, has no package-visible local changes, and that the two project files
pin `6000.3.20f1` and `6000.5.5f1`. Use Unity CLI first:

```bash
HEAD_SHA=$(git rev-parse HEAD)
RESULT_ROOT="$PWD/.ci-artifacts/$HEAD_SHA/manual-hosts-cli"
mkdir -p "$RESULT_ROOT/6000.3.20f1" "$RESULT_ROOT/6000.5.5f1"

rm -f "$RESULT_ROOT/6000.3.20f1/editmode.xml"
unity test "$HOST_6000_3" --mode EditMode \
  --editor-version 6000.3.20f1 \
  --output "$RESULT_ROOT/6000.3.20f1/editmode.xml" --timeout 1800
python3 .github/ci/cocoflow_ci.py unity-result \
  "$RESULT_ROOT/6000.3.20f1/editmode.xml"

rm -f "$RESULT_ROOT/6000.3.20f1/playmode.xml"
unity test "$HOST_6000_3" --mode PlayMode \
  --editor-version 6000.3.20f1 \
  --output "$RESULT_ROOT/6000.3.20f1/playmode.xml" --timeout 1800
python3 .github/ci/cocoflow_ci.py unity-result \
  "$RESULT_ROOT/6000.3.20f1/playmode.xml"

rm -f "$RESULT_ROOT/6000.5.5f1/editmode.xml"
unity test "$HOST_6000_5" --mode EditMode \
  --editor-version 6000.5.5f1 \
  --output "$RESULT_ROOT/6000.5.5f1/editmode.xml" --timeout 1800
python3 .github/ci/cocoflow_ci.py unity-result \
  "$RESULT_ROOT/6000.5.5f1/editmode.xml"

rm -f "$RESULT_ROOT/6000.5.5f1/playmode.xml"
unity test "$HOST_6000_5" --mode PlayMode \
  --editor-version 6000.5.5f1 \
  --output "$RESULT_ROOT/6000.5.5f1/playmode.xml" --timeout 1800
python3 .github/ci/cocoflow_ci.py unity-result \
  "$RESULT_ROOT/6000.5.5f1/playmode.xml"
```

`unity test` refreshes its output file. If Unity CLI is unavailable or fails
because of CLI infrastructure rather than package compilation/tests, remove the
old XML first and use the matching Editor's official `-batchmode -runTests`
arguments as a fallback. Do not fall back merely because a package test or
compilation failed.

Example fallback for one mode:

```bash
rm -f "$RESULT_ROOT/6000.3.20f1/editmode.xml"
"/Applications/Unity/Hub/Editor/6000.3.20f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -nographics -projectPath "$HOST_6000_3" \
  -runTests -testPlatform EditMode \
  -testResults "$RESULT_ROOT/6000.3.20f1/editmode.xml" \
  -logFile "$RESULT_ROOT/6000.3.20f1/editmode.log"
python3 .github/ci/cocoflow_ci.py unity-result \
  "$RESULT_ROOT/6000.3.20f1/editmode.xml"
```

The Unity command itself and `unity-result` must both exit zero. The XML validator
requires a structurally valid NUnit `test-run`, `result="Passed"`, zero Failed,
zero Inconclusive, non-negative counters, and at least one test. Missing,
malformed, empty, failed, or inconclusive results are not PASS. Compilation
failure with no fresh XML is recorded as a failure, not as an unverified success.

The projects are reusable validation hosts, so they are not evidence of a fresh
package install by themselves. PR 15.08 and the release gate still require a
separately prepared clean-host run at their frozen checkpoints.
