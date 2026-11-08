#!/usr/bin/env python3
"""Add a Windows Mono probe to the existing clean, complete CI host."""
import argparse
import json
import shutil
from pathlib import Path

import prepare_unity_host as host


def prepare(work, editor_data):
    metadata = host.prepare(work, editor_data=editor_data)
    ci = Path(__file__).parent
    metadata["playerProbe"] = json.loads((ci / "player-probe.json").read_text())
    settings = work / "host/ProjectSettings/ProjectSettings.asset"
    settings.write_text(settings.read_text() +
                        "  scriptingBackend:\n    Standalone: 0\n"
                        "  managedStrippingLevel:\n    Standalone: 0\n")
    shutil.copy2(ci / "CoCoFlowCiPlayerBuild.cs", work / "host/Assets/Editor")
    fixture = work / "host/Assets/PlayerProbe"
    fixture.mkdir()
    shutil.copy2(ci / "CoCoFlowCiPlayerProbe.cs", fixture)
    (fixture / "CoCoFlow.CI.Tests.Player.asmdef").write_text(json.dumps({
        "name": "CoCoFlow.CI.Tests.Player", "references": [],
        "optionalUnityReferences": ["TestAssemblies"]}, indent=2) + "\n")
    (work / "prepared.json").write_text(json.dumps(metadata, indent=2) + "\n")
    return metadata


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--work", type=Path, default=host.ROOT / ".ci-work")
    parser.add_argument("--editor-data", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(prepare(args.work, args.editor_data), indent=2))
