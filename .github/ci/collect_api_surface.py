"""Collect API and compiler XML from the Unity Editor compilation in an existing CI host."""

import argparse
import hashlib
import json
import re
import shutil
import subprocess
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
MODULES = {
    "Core": ("CoCoFlow.Runtime.Core", "CoCoFlow.Editor.Core"),
    "StateGraph": (
        "CoCoFlow.Runtime.StateGraph.Contracts", "CoCoFlow.Runtime.StateGraph",
        "CoCoFlow.Editor.StateGraph", "CoCoFlow.Editor.PlayerMetadata",
    ),
}


def git(*args):
    return subprocess.check_output(["git", *args], cwd=HERE, text=True).strip()


def collect(host, scripting, package, output, reader):
    output.mkdir(parents=True)
    apis = {}
    compiler, = scripting.rglob("DotNetSdkRoslyn/csc.dll")
    runtime = compiler.parent.parent / "NetCoreRuntime"
    unity_dotnet = runtime / ("dotnet.exe" if (runtime / "dotnet.exe").exists() else "dotnet")
    for module, assemblies in MODULES.items():
        for name in assemblies:
            product = host / "Library/ScriptAssemblies" / (name + ".dll")
            payload = product.read_bytes()
            matches = [
                path for path in (host / "Library/Bee/artifacts").glob(f"*.dag/{name}.rsp")
                if path.with_suffix(".dll").is_file() and path.with_suffix(".dll").read_bytes() == payload
            ]
            if len(matches) != 1:
                raise ValueError(f"{name}: expected one Bee response matching the final Editor DLL, found {len(matches)}")
            original_rsp, = matches
            extra_rsp = original_rsp.with_suffix(".rsp2")
            with tempfile.TemporaryDirectory(prefix="cocoflow-api-replay-") as scratch:
                scratch = Path(scratch)
                replay = scratch / (name + ".dll")
                document = output / (name + ".xml")
                rsp = scratch / original_rsp.name
                lines = original_rsp.read_text(encoding="utf-8-sig").splitlines()
                lines = [line for line in lines if not re.match(r"^[-/](out|refout|pdb|doc):", line, re.I)]
                rsp.write_text("\n".join([*lines, f'-out:"{replay}"', f'-doc:"{document}"', "-nowarn:1591"]) + "\n", encoding="utf-8")
                subprocess.run([unity_dotnet, "exec", compiler, "/nostdlib", "/noconfig",
                                "@" + str(rsp), "@" + str(extra_rsp)], cwd=host, check=True)
                snapshot = scratch / "surface.json"
                subprocess.run(["dotnet", reader, host, name, product, replay, original_rsp, snapshot],
                               check=True)
                entries = json.loads(snapshot.read_text(encoding="utf-8-sig"))
            for entry in entries:
                source = entry["Source"]
                entry["Source"] = Path(source).resolve().relative_to(package).as_posix() if source else None
            (output / (name + ".json")).write_text(json.dumps(entries, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
            shutil.copy2(product, output)
            apis[name] = {
                "module": module,
                "inputs": {p.name: hashlib.sha256(p.read_bytes()).hexdigest()
                           for p in (product, original_rsp, extra_rsp, document, output / (name + ".json"))},
                "defines": [line.partition(":")[2] for line in lines if line.startswith("-define:")],
            }
            print(f"{name}: verified replay metadata and collected {len(entries)} public records", flush=True)
    version = (host / "ProjectSettings/ProjectVersion.txt").read_text(encoding="utf-8").splitlines()[0].split(": ", 1)[1]
    provenance = {
        "sha": git("rev-parse", "HEAD"), "tree": git("rev-parse", "HEAD^{tree}"),
        "unity": version, "assemblies": apis,
    }
    (output / "inputs.json").write_text(json.dumps(provenance, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for argument in ("host", "scripting", "package", "output", "reader"):
        parser.add_argument(argument, type=Path)
    args = parser.parse_args()
    collect(*(value.resolve() for value in vars(args).values()))
