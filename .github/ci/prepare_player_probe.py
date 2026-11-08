#!/usr/bin/env python3
"""Add a Windows Mono or IL2CPP probe to the existing complete CI host."""
import argparse
import hashlib
import json
import shutil
from pathlib import Path

import prepare_unity_host as host


def prepare(work, editor_data, backend="mono", preserve_metadata=False, suite="probe"):
    if suite == "all" and preserve_metadata:
        raise ValueError("Full Player runs must use the unmodified preservation configuration")
    metadata = host.prepare(work, editor_data=editor_data)
    ci = Path(__file__).parent
    config = "player-probe-il2cpp.json" if backend == "il2cpp" else "player-probe.json"
    metadata["playerProbe"] = json.loads((ci / config).read_text())
    metadata["playerProbe"]["scope"] = suite
    if suite == "all":
        metadata["playerProbe"]["expectedCases"] = ["CoCoFlow.CI.PlayerProbeTests.RunsInWindowsPlayer"]
        metadata["expectedPlayerAssemblies"] = metadata["expectedTestAssemblies"]["playmode"] + [
            "CoCoFlow.CI.Tests.Player"]
    backend_value, stripping_value = (1, 2) if backend == "il2cpp" else (0, 0)
    settings = work / "host/ProjectSettings/ProjectSettings.asset"
    settings.write_text(settings.read_text() +
                        f"  scriptingBackend:\n    Standalone: {backend_value}\n"
                        f"  managedStrippingLevel:\n    Standalone: {stripping_value}\n")
    (work / "host/CiPlayerProbeRequirements.json").write_text(json.dumps(metadata["playerProbe"], indent=2) + "\n")
    shutil.copy2(ci / "CoCoFlowCiPlayerBuild.cs", work / "host/Assets/Editor")
    fixture = work / "host/Assets/PlayerProbe"
    fixture.mkdir()
    if preserve_metadata:
        source = ci / "player-probe-metadata.link.xml"
        # Unity discovers only the exact link.xml filename in Assets.
        shutil.copy2(source, fixture / "link.xml")
        metadata["diagnosticPreservation"] = {"file": "link.xml",
            "sha256": hashlib.sha256(source.read_bytes()).hexdigest()}
    (fixture / "CoCoFlowCiPlayerProbe.cs").write_text(
        (ci / "CoCoFlowCiPlayerProbe.cs").read_text().replace(
            "__COCOFLOW_EXPECTED_BACKEND__", metadata["playerProbe"]["backend"]))
    (fixture / "CoCoFlow.CI.Tests.Player.asmdef").write_text(json.dumps({
        "name": "CoCoFlow.CI.Tests.Player", "references": [],
        "optionalUnityReferences": ["TestAssemblies"]}, indent=2) + "\n")
    (work / "prepared.json").write_text(json.dumps(metadata, indent=2) + "\n")
    return metadata


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--work", type=Path, default=host.ROOT / ".ci-work")
    parser.add_argument("--editor-data", type=Path, required=True)
    parser.add_argument("--backend", choices=("mono", "il2cpp"), default="mono")
    parser.add_argument("--diagnostic-preserve-metadata", action="store_true")
    parser.add_argument("--suite", choices=("probe", "all"), default="probe")
    args = parser.parse_args()
    print(json.dumps(prepare(args.work, args.editor_data, args.backend,
                             args.diagnostic_preserve_metadata, args.suite), indent=2))
