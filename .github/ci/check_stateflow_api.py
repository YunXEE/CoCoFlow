"""Compile the StateFlow public API consumer against the package's source assemblies."""

import argparse
import json
import os
import subprocess
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CHECK = Path(__file__).with_name("stateflow-api")
CONSUMER = "CoCoFlow.CI.ExternalStateFlowConsumer"
ENGINE = "UnityEngine.CoreModule"
RUNTIME = (
    "CoCoFlow.Runtime.Core",
    "CoCoFlow.Runtime.Core.Contracts",
    "CoCoFlow.Runtime.Core.StateFlow",
    "CoCoFlow.Runtime.Core.StateGraph",
    "CoCoFlow.Runtime.Core.StateGraphAuthoring",
    "CoCoFlow.Runtime.StateGraphHost",
)


def runtime_sources():
    """Group checked-in C# sources by their nearest Unity assembly definition."""
    directories = {}
    definitions = {}
    for path in sorted((ROOT / "Runtime").rglob("*.asmdef")):
        definition = json.loads(path.read_text(encoding="utf-8-sig"))
        name = definition["name"]
        directories[path.parent] = name
        definitions[name] = definition
    for path in (ROOT / "Runtime").rglob("*.asmref"):
        reference = json.loads(path.read_text(encoding="utf-8-sig"))["reference"]
        if reference.startswith("GUID:"):
            for definition in (ROOT / "Runtime").rglob("*.asmdef"):
                if f"guid: {reference[5:]}" in definition.with_suffix(".asmdef.meta").read_text().splitlines():
                    reference = directories[definition.parent]
                    break
        directories[path.parent] = reference

    sources = {name: [] for name in RUNTIME}
    for path in sorted((ROOT / "Runtime").rglob("*.cs")):
        owner = next((directories[parent] for parent in path.parents if parent in directories), None)
        if owner in sources:
            sources[owner].append(path)
    return definitions, sources


def write_project(directory, name, framework, sources, projects=(), engine=None, unsafe=False):
    project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    props = ET.SubElement(project, "PropertyGroup")
    ET.SubElement(props, "TargetFramework").text = framework
    ET.SubElement(props, "EnableDefaultCompileItems").text = "false"
    ET.SubElement(props, "AllowUnsafeBlocks").text = str(unsafe).lower()
    ET.SubElement(props, "DisableTransitiveProjectReferences").text = "true"
    ET.SubElement(props, "LangVersion").text = "9.0"
    # Unity fills serialized fields and does not report the SDK's serialization obsoletion.
    ET.SubElement(props, "NoWarn").text = "CS0649;SYSLIB0050"
    items = ET.SubElement(project, "ItemGroup")
    for path in sources:
        ET.SubElement(items, "Compile", Include=str(path))
    for reference in projects:
        ET.SubElement(items, "ProjectReference", Include=f"../{reference}/{reference}.csproj")
    if engine:
        assembly = ET.SubElement(items, "Reference", Include=ENGINE)
        ET.SubElement(assembly, "HintPath").text = str(engine)
    path = directory / name / f"{name}.csproj"
    path.parent.mkdir()
    ET.indent(project)
    ET.ElementTree(project).write(path, encoding="utf-8")
    return path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--framework", default="net10.0")
    parser.add_argument("--unity-core-module", type=Path, help="Use an installed Unity CoreModule in place of the CI reference shim.")
    args = parser.parse_args()
    engine = args.unity_core_module.resolve(strict=True) if args.unity_core_module else None
    definitions, sources = runtime_sources()
    with tempfile.TemporaryDirectory(prefix="cocoflow-stateflow-api-") as temporary:
        directory = Path(temporary)
        (directory / "NuGet.Config").write_text(
            '<?xml version="1.0"?><configuration><packageSources><clear /></packageSources></configuration>',
            encoding="utf-8",
        )
        if not engine:
            write_project(directory, ENGINE, args.framework, [CHECK / "UnityReference.cs"], unsafe=True)
        for name in RUNTIME:
            definition = definitions[name]
            if not sources[name]:
                raise ValueError(f"{name}: no source files found")
            references = definition["references"]
            if not set(references) <= set(RUNTIME):
                raise ValueError(f"{name}: unexpected reference outside the StateFlow build: {references}")
            uses_engine = not definition["noEngineReferences"]
            print(f"{name}: {len(sources[name])} C# files; references {', '.join(references) or 'none'}", flush=True)
            write_project(
                directory, name, args.framework, sources[name],
                references + ([ENGINE] if uses_engine and not engine else []),
                engine if uses_engine else None,
                unsafe=definition["allowUnsafeCode"],
            )
        consumer = write_project(
            directory, CONSUMER, args.framework, [CHECK / "RequiredApi.cs"],
            (*RUNTIME, ENGINE) if not engine else RUNTIME, engine,
        )
        # Isolate CLI first-run files from the user's .dotnet directory.
        env = dict(os.environ, DOTNET_CLI_HOME=temporary, DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1")
        return subprocess.run(
            ["dotnet", "build", str(consumer), "--nologo", "-v", "quiet", "-warnaserror", "--disable-build-servers"],
            cwd=directory, env=env, check=False,
        ).returncode


if __name__ == "__main__":
    raise SystemExit(main())
