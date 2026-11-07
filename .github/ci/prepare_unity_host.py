#!/usr/bin/env python3
"""Create CoCoFlow's clean CI project from an installed Editor's resources."""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import re
import shutil
import subprocess
import tarfile
import urllib.request
import zipfile
from pathlib import Path, PurePosixPath


ROOT = Path(__file__).resolve().parents[2]
CONFIG = Path(__file__).with_name("unity-host.json")


def checked_bytes(data: bytes, expected: str, label: str) -> bytes:
    if hashlib.sha256(data).hexdigest() != expected:
        raise ValueError(f"{label}: SHA-256 does not match the pinned input")
    return data


def expected_test_assemblies(package: Path) -> dict[str, list[str]]:
    """Inventory test-bearing assemblies; Unity/XML prove compilation and execution."""
    result: dict[str, set[str]] = {"editmode": set(), "playmode": set()}
    for source in (package / "Tests").rglob("*.cs"):
        if not re.search(r"\[(?:Test(?:Case|CaseSource)?|UnityTest|Theory)\b",
                         source.read_text(encoding="utf-8-sig")):
            continue
        parent = source.parent
        while parent != package:
            definitions = list(parent.glob("*.asmdef"))
            if definitions:
                definition = json.loads(definitions[0].read_text())
                mode = "editmode" if "Editor" in definition.get("includePlatforms", []) else "playmode"
                result[mode].add(definition["name"])
                break
            parent = parent.parent
    if not all(result.values()):
        raise ValueError("Both EditMode and PlayMode package assemblies are required")
    return {mode: sorted(names) for mode, names in result.items()}


def unpack_unitypackage(data: bytes, host: Path) -> None:
    with tarfile.open(fileobj=io.BytesIO(data), mode="r:gz") as archive:
        names = {entry.name for entry in archive.getmembers()}
        for name in sorted(names):
            if not name.endswith("/pathname"):
                continue
            asset_path = archive.extractfile(name).read().decode().strip()
            relative = PurePosixPath(asset_path)
            if relative.parts[0] != "Assets" or ".." in relative.parts:
                raise ValueError(f"Unexpected TMP resource path: {asset_path}")
            target = host.joinpath(*relative.parts)
            prefix = name.rsplit("/", 1)[0]
            target.parent.mkdir(parents=True, exist_ok=True)
            if prefix + "/asset" in names:
                target.write_bytes(archive.extractfile(prefix + "/asset").read())
            else:
                target.mkdir(exist_ok=True)
            if prefix + "/asset.meta" in names:
                Path(str(target) + ".meta").write_bytes(
                    archive.extractfile(prefix + "/asset.meta").read())


def prepare(work: Path, dotween_archive: Path | None = None,
            editor_data: Path | None = None) -> dict:
    config = json.loads(CONFIG.read_text())
    if editor_data is None:
        raise ValueError("The installed Unity Editor Data directory is required")
    if work.exists():
        raise ValueError(f"Refusing to reuse an existing CI workspace: {work}")
    package, host = work / "package", work / "host"
    package.mkdir(parents=True)
    tracked = subprocess.check_output(["git", "ls-files", "-z"], cwd=ROOT).decode().split("\0")
    package_digest = hashlib.sha256()
    for relative in sorted(path for path in tracked if path):
        source, target = ROOT / relative, package / relative
        if not source.is_file():
            continue
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
        package_digest.update(relative.encode() + b"\0" + source.read_bytes())
    for folder in ("Assets", "Packages", "ProjectSettings"):
        (host / folder).mkdir(parents=True, exist_ok=True)

    if dotween_archive:
        dotween_data = dotween_archive.read_bytes()
    else:
        with urllib.request.urlopen(config["dotween"]["url"], timeout=60) as response:
            dotween_data = response.read()
    checked_bytes(dotween_data, config["dotween"]["sha256"], "DOTween")
    vendor = host / "Assets/Plugins/Demigiant"
    vendor.mkdir(parents=True)
    with zipfile.ZipFile(io.BytesIO(dotween_data)) as archive:
        archive.extractall(vendor)
    (vendor / "DOTween/Modules/DOTween.Modules.asmdef").write_text(
        json.dumps({"name": "DOTween.Modules", "references": ["UnityEngine.UI"]}, indent=2) + "\n")

    resource = "Resources/PackageManager/BuiltInPackages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage"
    tmp_data = (editor_data / resource).read_bytes()
    checked_bytes(tmp_data, config["tmpResourceSha256"], "Bundled TMP resources")
    unpack_unitypackage(tmp_data, host)

    dependencies = dict(config["packages"])
    dependencies.update({"com.unity.modules." + name: "1.0.0"
                         for name in config["engineModules"]})
    dependencies["com.yunxee.cocoflow"] = "file:../../package"
    (host / "Packages/manifest.json").write_text(json.dumps({
        "dependencies": dependencies, "testables": ["com.yunxee.cocoflow"]
    }, indent=2) + "\n")
    (host / "ProjectSettings/ProjectVersion.txt").write_text(
        "m_EditorVersion: " + config["unity"] + "\n")
    (host / "ProjectSettings/ProjectSettings.asset").write_text(
        "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\n"
        "PlayerSettings:\n  m_ObjectHideFlags: 0\n  serializedVersion: 28\n"
        "  companyName: YunXEE\n  productName: CoCoFlow CI\n"
        "  activeInputHandler: 1\n  scriptingDefineSymbols:\n"
        "    Standalone: " + ";".join(config["defines"]) + "\n")

    expected = expected_test_assemblies(package)
    definitions = [json.loads(path.read_text()) for folder in ("Runtime", "Editor")
                   for path in (package / folder).rglob("*.asmdef")]
    required = sorted({entry["name"] for entry in definitions} |
                      set(expected["editmode"]) | set(expected["playmode"]) |
                      {"DOTween", "DOTween.Modules", "UniTask", "UniTask.DOTween",
                       "Unity.TextMeshPro", "Unity.InputSystem.TestFramework"})
    product = json.loads((package / "package.json").read_text())
    package_names = sorted(set(product["dependencies"]) | set(config["packages"]))
    requirements = {
        "unityVersion": config["unity"],
        "editorPlatform": config["editorPlatform"],
        "assemblies": required,
        "packages": package_names,
        "defines": config["defines"], "dotweenVersion": config["dotween"]["version"]
    }
    (host / "CiRequirements.json").write_text(json.dumps(requirements, indent=2) + "\n")
    fixture = host / "Assets/Editor/CoCoFlowCI"
    fixture.mkdir(parents=True)
    shutil.copy2(ROOT / ".github/ci/CoCoFlowCiDependencies.cs", fixture)
    (fixture / "CoCoFlow.CI.Tests.Editor.asmdef").write_text(json.dumps({
        "name": "CoCoFlow.CI.Tests.Editor", "includePlatforms": ["Editor"],
        "references": [], "optionalUnityReferences": ["TestAssemblies"]
    }, indent=2) + "\n")
    metadata = {
        "sourceSha": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
        "packageContentSha256": package_digest.hexdigest(),
        "unity": config["unity"], "runner": config["runner"],
        "editorPlatform": config["editorPlatform"],
        "editorInstaller": config["editorInstaller"],
        "gameci": config["gameci"],
        "dotweenSha256": config["dotween"]["sha256"],
        "tmpResourceSha256": config["tmpResourceSha256"],
        "expectedTestAssemblies": expected
    }
    (work / "prepared.json").write_text(json.dumps(metadata, indent=2) + "\n")
    return metadata


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--work", type=Path, default=ROOT / ".ci-work")
    parser.add_argument("--dotween-archive", type=Path)
    parser.add_argument("--editor-data", type=Path)
    args = parser.parse_args()
    print(json.dumps(prepare(args.work, args.dotween_archive, args.editor_data), indent=2))
