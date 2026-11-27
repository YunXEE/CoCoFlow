"""Compile the StateFlow public API consumer against the package's source assemblies."""

import argparse
import json
import os
import re
import subprocess
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CHECK = Path(__file__).with_name("stateflow-api")
CONSUMER = "CoCoFlow.CI.ExternalStateFlowConsumer"
CORE_CONSUMER = "CoCoFlow.CI.CoreConsumer"
GAME_CONSUMER = "CoCoFlow.CI.GameplayConsumer"
# Each expression is checked in an otherwise compiling external Host consumer.
DENIED = (
    ("StateGraphProjectBindings.ResetForTests()", "CS0117", "ResetForTests"),
    ("StateGraphRuntime.TryCreate(null, default, null, null, null, out _, out _)",
     "CS0122", "StateGraphRuntime"),
    ("ContextFrameReadView.TryCreate(default, out _)", "CS0117", "TryCreate"),
    ("_ = new StateGraphAssetCompiler()", "CS0122", "StateGraphAssetCompiler"),
    ("_ = new StateGraphCompiler()", "CS0122", "StateGraphCompiler"),
    ("_ = default(StateExecutionContext).Operations", "CS1061", "Operations"),
    ("_ = new StateExecutionContext()", "CS1729", "StateExecutionContext"),
    ("_ = new TransitionHandle(new object(), 0, 0, default, default, default, 0)", "CS1729", "TransitionHandle"),
    ("default(StateEventOutboxWriter).Publish<string>(string.Empty)", "CS8377", "string"),
    ("_ = new StateGraphSource(0, 0, default, null, null)", "CS1729", "StateGraphSource"),
    ("_ = typeof(OperationReader)", "CS0122", "OperationReader"),
    ("_ = typeof(OperationViewContext<>)", "CS0122", "OperationViewContext"),
    ("_ = typeof(IOperationViewFactory<>)", "CS0122", "IOperationViewFactory"),
    ("_ = typeof(ActorClock)", "CS0122", "ActorClock"),
    ("_ = typeof(IntentConsumeAttribute)", "CS0246", "IntentConsumeAttribute"),
    ("_ = typeof(OperationProvideAttribute)", "CS0246", "OperationProvideAttribute"),
    ("_ = typeof(ConditionAttribute)", "CS0246", "ConditionAttribute"),
    ("_ = typeof(OperationSectionAttribute)", "CS0246", "OperationSectionAttribute"),
    ("_ = RawIntents.Player", "CS0103", "RawIntents"),
    ("_ = typeof(CoCoFlow.Runtime.Input.Contracts.InputIntent)", "CS0234", "InputIntent"),
    ("_ = typeof(CoCoFlow.Runtime.Animation.Contracts.AnimFeedbackIntent)", "CS0234", "Animation"),
    ("default(FinalizedContextCommit).Commit()", "CS1061", "Commit"),
    ("default(ContextFrameArena).TryPrepare(default, default, out _, out _)", "CS1061", "TryPrepare"),
    ("default(FinalizedContextCommit).CommitNoFailUnchecked()", "CS1061", "CommitNoFailUnchecked"),
)
ENGINE = "UnityEngine.CoreModule"
RUNTIME = (
    "CoCoFlow.Runtime.Core",
    "CoCoFlow.Runtime.StateGraph.Contracts",
    "CoCoFlow.Runtime.Input.Contracts",
    "CoCoFlow.Runtime.StateGraph",
)


def runtime_sources(root=ROOT):
    """Group checked-in C# sources by their nearest Unity assembly definition."""
    directories = {}
    definitions = {}
    for path in sorted((root / "Runtime").rglob("*.asmdef")):
        definition = json.loads(path.read_text(encoding="utf-8-sig"))
        name = definition["name"]
        directories[path.parent] = name
        definitions[name] = definition
    for path in (root / "Runtime").rglob("*.asmref"):
        reference = json.loads(path.read_text(encoding="utf-8-sig"))["reference"]
        if reference.startswith("GUID:"):
            for definition in (root / "Runtime").rglob("*.asmdef"):
                if f"guid: {reference[5:]}" in definition.with_suffix(".asmdef.meta").read_text().splitlines():
                    reference = directories[definition.parent]
                    break
        directories[path.parent] = reference

    sources = {name: [] for name in RUNTIME}
    for path in sorted((root / "Runtime").rglob("*.cs")):
        owner = next((directories[parent] for parent in path.parents if parent in directories), None)
        if owner in sources:
            sources[owner].append(path)
    pure = "CoCoFlow.Runtime.StateGraph.Contracts"
    unity = "CoCoFlow.Runtime.StateGraph"
    pure_dirs = {"State", "Graph", "Instance", "Temporal", "Contracts"}
    owners = {path: name for name, files in sources.items() for path in files}
    for module in ("StateGraph", "StateGraphHost"):
        module_path = root / "Runtime" / module
        for path in sorted(module_path.rglob("*.cs")):
            relative = path.relative_to(module_path)
            expected = pure if module == "StateGraph" and relative.parts[0] in pure_dirs else unity
            if path.name in ("StateConfig.cs", "FieldConfig.cs", "IEventInboxAdapter.cs"):
                expected = pure
            if owners.get(path) != expected:
                raise ValueError(f"{path.relative_to(root)}: expected {expected}, compiled in {owners.get(path)}")
    return definitions, sources


def write_project(directory, name, framework, sources, projects=(), engine=None, unsafe=False, analyzer=False):
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
    if analyzer:
        dll = ROOT / "Runtime/StateGraph/Contracts/CoCoFlow.StateFlow.Generator.dll"
        ET.SubElement(items, "Analyzer", Include=str(dll.resolve(strict=True)))
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
        write_project(directory, CORE_CONSUMER, args.framework, [CHECK / "CoreApi.cs"], [RUNTIME[0]])
        declaration = directory / "Declaration.cs"
        declaration.write_text("", encoding="utf-8")
        write_project(directory, GAME_CONSUMER, args.framework, [CHECK / "GameplayApi.cs", declaration],
                      [*RUNTIME[:3], CORE_CONSUMER], analyzer=True)
        probe = directory / "Denied.cs"
        probe.write_text("", encoding="utf-8")
        consumer = write_project(
            directory, CONSUMER, args.framework, [CHECK / "RequiredApi.cs", probe],
            [*RUNTIME, GAME_CONSUMER] + ([] if engine else [ENGINE]), engine,
        )
        # Isolate CLI first-run files from the user's .dotnet directory.
        env = dict(os.environ, DOTNET_CLI_HOME=temporary, DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1")
        command = ["dotnet", "build", str(consumer), "--nologo", "-v", "quiet", "-warnaserror",
                   "--disable-build-servers", "-m:1"]
        build = subprocess.run(command, cwd=directory, env=env)
        if build.returncode:
            return build.returncode
        for expression, code, symbol in DENIED:
            probe.write_text(
                "using CoCoFlow.Runtime.StateGraph;\n"
                "using CoCoFlow.Runtime.Input.Contracts;\n"
                "class Denied { void Probe() {\n" + expression + ";\n} }\n", encoding="utf-8")
            build = subprocess.run([*command, "--no-restore"], cwd=directory, env=env,
                                   stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
            errors = {line.strip() for line in build.stdout.splitlines() if ": error " in line}
            expected = re.compile(r"Denied\.cs\(\d+,\d+\): error " + code + r": .*'" + symbol + r"(?:<[^']*>)?'")
            if not build.returncode or not errors or any(not expected.search(line) for line in errors):
                print(f"Expected only {code} for {symbol}.\n{build.stdout}")
                return 1
            print(f"Denied {symbol}: {code}", flush=True)
        probe.write_text("", encoding="utf-8")
        invalid = (
            ("[Operation] interface IBad : IOperation { int Value {get; set;} }", "declare Continuous or Discrete mode"),
            ("[Operation(Mode.None)] interface IBad : IOperation { int Value {get; set;} }", "declare Continuous or Discrete mode"),
            ("[Operation(Mode.Continuous)] interface IChild : IExternalOperation { }", "directly inherit IOperation"),
            ("[Operation((Mode)99)] interface IBad : IOperation { int Value {get; set;} }", "declare Continuous or Discrete mode"),
            ("class Bad { [Operation(Mode.Discrete)] public IExternalOperation Output {get; set;} }",
             "must use [Operation] without a mode"),
        )
        for source, reason in invalid:
            declaration.write_text("using CoCoFlow.Runtime.StateGraph;\nnamespace CoCoFlow.CI { " + source + " }",
                                   encoding="utf-8")
            build = subprocess.run([*command, "--no-restore"], cwd=directory, env=env,
                                   stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
            errors = {line for line in build.stdout.splitlines() if ": error " in line}
            if not build.returncode or not errors or any(
                not re.search(r"Declaration\.cs\(\d+,\d+\): error COCOOP001:", line) or reason not in line
                for line in errors
            ):
                print(f"Expected COCOOP001 with {reason}.\n{build.stdout}")
                return 1
            print(f"Denied {source}: COCOOP001", flush=True)
        return 0


if __name__ == "__main__":
    raise SystemExit(main())
