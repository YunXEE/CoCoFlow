#!/usr/bin/env bash
# Official return command; retain the narrowly verified v0.1.72 compatibility guard.
set -uo pipefail
return_log="$RUNNER_TEMP/unity-license-return.log"
set +e
game-ci return-license .ci-work/host \
  --engine=unity --engineVersion=6000.3.27f2 \
  --targetPlatform=StandaloneLinux64 \
  --unityLicensingMethod=personal --licenseRetryMaxAttempts=2 \
  2>&1 | tee "$return_log"
return_pipeline_status=("${PIPESTATUS[@]}")
set -e
if [[ "${return_pipeline_status[1]}" -ne 0 ]]; then
  echo "::error::Could not preserve the Unity license return log."
  exit 1
fi
if ! grep -Fq '[Licensing::Module] Successfully returned the entitlement license' "$return_log"; then
  echo "::error::Unity did not confirm that the Personal seat was returned."
  exit 1
fi
if [[ "${return_pipeline_status[0]}" -eq 0 ]]; then
  exit 0
fi
if [[ "${return_pipeline_status[0]}" -eq 1 ]] &&
   [[ "$(grep -c '^\[ERROR\]' "$return_log" || true)" -eq 1 ]] &&
   grep -Fxq '[ERROR] Error: There was an error building the project. Please read the logs for details.' "$return_log" &&
   grep -Eq '^[[:space:]]+at validateBuild \(' "$return_log"; then
  echo "::notice::Unity returned the Personal seat; accepted the known GameCI v0.1.72 post-return validation error."
  exit 0
fi
echo "::error::Unexpected GameCI return failure (exit ${return_pipeline_status[0]})."
exit "${return_pipeline_status[0]}"
