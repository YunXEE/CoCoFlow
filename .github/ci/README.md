# CoCoFlow CI baseline

CoCoFlow 0.4.0 has one automatic repository gate and one local Unity
verification process. They prove different things.

## Automatic pull request gate

Every pull request runs one Ubuntu job named `CI Static / gate`. It checks:

- `git diff --check` for whitespace errors and conflict markers;
- whether a tracked file is excluded by `.gitignore`;
- the unit tests for the local NUnit XML result reader.

The job does not validate package metadata, asmdef/asmref semantics, Unity
compilation, Unity tests, or Windows compatibility. Those require a real Unity
Editor. PR 15.01 is the only CoCoFlow 0.4.0 CI construction PR.

## Local Unity projects

The maintainer-created projects are:

| Editor | Project |
| --- | --- |
| `6000.3.20f1` | `/Users/UnityDev/CoCoFlow_Test_6000_3` |
| `6000.5.5f1` | `/Users/UnityDev/CoCoFlow_Test_6000_5` |

Each project must:

- use its matching Editor patch;
- reference `file:/Users/GitProjects/CoCoFlow`;
- include `com.yunxee.cocoflow` in the manifest `testables` array.

Before recording evidence, confirm the package checkout is on the intended Head
and has no package-visible local changes.

## Run EditMode and PlayMode

Use Unity CLI first. The following shell helper runs one mode, removes any old
XML first, and validates the newly produced result:

```zsh
HEAD_SHA="$(git rev-parse HEAD)"
RESULT_ROOT="$PWD/.ci-artifacts/$HEAD_SHA/manual-hosts-cli"

run_unity_test() {
  host="$1"
  version="$2"
  mode="$3"
  result="$4"

  mkdir -p "$(dirname "$result")"
  rm -f "$result"

  unity test "$host" --mode "$mode" \
    --editor-version "$version" \
    --output "$result" --timeout 1800
  unity_status=$?

  python3 .github/ci/cocoflow_ci.py unity-result "$result"
  result_status=$?

  [[ $unity_status -eq 0 && $result_status -eq 0 ]]
}

matrix_status=0

run_unity_test /Users/UnityDev/CoCoFlow_Test_6000_3 6000.3.20f1 EditMode \
  "$RESULT_ROOT/6000.3.20f1/editmode.xml" || matrix_status=1
run_unity_test /Users/UnityDev/CoCoFlow_Test_6000_3 6000.3.20f1 PlayMode \
  "$RESULT_ROOT/6000.3.20f1/playmode.xml" || matrix_status=1
run_unity_test /Users/UnityDev/CoCoFlow_Test_6000_5 6000.5.5f1 EditMode \
  "$RESULT_ROOT/6000.5.5f1/editmode.xml" || matrix_status=1
run_unity_test /Users/UnityDev/CoCoFlow_Test_6000_5 6000.5.5f1 PlayMode \
  "$RESULT_ROOT/6000.5.5f1/playmode.xml" || matrix_status=1

[[ $matrix_status -eq 0 ]]
```

Both the Unity command and the result reader must exit zero. The reader accepts
only a non-empty NUnit `test-run` with `result="Passed"`, zero Failed, and zero
Inconclusive. Missing, malformed, empty, failed, or inconclusive results fail.

If Unity CLI itself is unavailable or broken, use the matching Editor's official
`-batchmode -runTests` command after deleting the old XML. Do not use Batchmode
as a fallback for package compilation or test failures.

These projects are persistent daily validation hosts. They are not clean-host
installation evidence.
