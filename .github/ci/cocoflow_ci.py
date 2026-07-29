#!/usr/bin/env python3
"""Small, dependency-free CI entry points for the CoCoFlow Unity package."""

from __future__ import annotations

import argparse
import io
import json
import os
import platform
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import xml.etree.ElementTree as ET
from dataclasses import asdict, dataclass
from pathlib import Path, PurePosixPath
from typing import Any, Dict, Iterable, List, Optional, Sequence, Set, Tuple


ROOT = Path(__file__).resolve().parents[2]
PACKAGE_NAME = "com.yunxee.cocoflow"
UNITY_VERSIONS = ("6000.3.20f1", "6000.5.5f1")
JSON_SUFFIXES = {".json", ".asmdef", ".asmref", ".inputactions"}
PACKAGE_ROOT_FILES = {
    "CHANGELOG.md",
    "LICENSE",
    "README.md",
    "Third Party Notices.md",
    "ValidationExceptions.json",
    "package.json",
}
FORBIDDEN_ROOTS = {"library", "logs", "obj", "temp", "usersettings"}
FORBIDDEN_ENDINGS = (
    ".bak",
    ".csproj",
    ".dll",
    ".mdb",
    ".orig",
    ".pdb",
    ".rej",
    ".sln",
    ".tmp",
    ".user",
    ".vc.db",
)
MAX_TRACKED_FILE_BYTES = 20 * 1024 * 1024
GUID_PATTERN = re.compile(r"^guid:\s*([0-9a-fA-F]{32})\s*$", re.MULTILINE)
SEMVER_PATTERN = re.compile(
    r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)"
    r"(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?"
    r"(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$"
)
UNITY_VERSION_PATTERN = re.compile(r"^\d+\.\d+(?:\.\d+)?$")
STANDARD_BUILTIN_MODULES = (
    "ai",
    "androidjni",
    "animation",
    "assetbundle",
    "audio",
    "cloth",
    "director",
    "imageconversion",
    "imgui",
    "jsonserialize",
    "particlesystem",
    "physics",
    "physics2d",
    "screencapture",
    "terrain",
    "terrainphysics",
    "tilemap",
    "ui",
    "uielements",
    "umbra",
    "unityanalytics",
    "unitywebrequest",
    "unitywebrequestassetbundle",
    "unitywebrequestaudio",
    "unitywebrequesttexture",
    "unitywebrequestwww",
    "vehicles",
    "video",
    "wind",
    "xr",
)


class DuplicateJsonKey(ValueError):
    pass


@dataclass
class Finding:
    level: str
    code: str
    message: str
    path: Optional[str] = None
    line: Optional[int] = None


def _reject_duplicate_keys(pairs: Sequence[Tuple[str, Any]]) -> Dict[str, Any]:
    result: Dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise DuplicateJsonKey("duplicate JSON key: {0}".format(key))
        result[key] = value
    return result


def load_json_strict(path: Path) -> Any:
    with path.open(encoding="utf-8-sig") as handle:
        return json.load(handle, object_pairs_hook=_reject_duplicate_keys)


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )


def run_command(
    command: Sequence[str], cwd: Path, capture: bool = True
) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        list(command),
        cwd=str(cwd),
        text=True,
        stdout=subprocess.PIPE if capture else None,
        stderr=subprocess.PIPE if capture else None,
        check=False,
    )


def git_output(root: Path, *arguments: str) -> str:
    result = run_command(("git",) + arguments, root)
    if result.returncode:
        raise RuntimeError((result.stdout + result.stderr).strip())
    return result.stdout.strip()


def tracked_files(root: Path) -> List[str]:
    result = subprocess.run(
        ["git", "ls-files", "-z"],
        cwd=str(root),
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if result.returncode:
        raise RuntimeError(result.stderr.decode(errors="replace").strip())
    paths = [
        value.decode("utf-8", errors="surrogateescape")
        for value in result.stdout.split(b"\0")
        if value
    ]
    return sorted(path for path in paths if (root / path).exists())


def is_strict_semver(value: Any) -> bool:
    if not isinstance(value, str):
        return False
    match = SEMVER_PATTERN.fullmatch(value)
    if not match:
        return False
    prerelease = match.group(4)
    if prerelease:
        for identifier in prerelease.split("."):
            if identifier.isdigit() and len(identifier) > 1 and identifier.startswith("0"):
                return False
    return True


def is_unity_visible(path: str) -> bool:
    parts = PurePosixPath(path).parts
    if not parts or path in PACKAGE_ROOT_FILES:
        return False
    if any(part.startswith(".") for part in parts):
        return False
    if parts[0] == "Documentation~":
        return False
    return not any(part.endswith("~") for part in parts[1:])


def unity_parent_directories(path: str) -> Iterable[str]:
    parent = PurePosixPath(path).parent
    while str(parent) not in {"", "."}:
        value = parent.as_posix()
        if value in {"Documentation~", "Samples~"}:
            break
        yield value
        parent = parent.parent


class RepositoryValidator:
    def __init__(self, root: Path, tracked: Optional[Sequence[str]] = None) -> None:
        self.root = root
        self.tracked = list(tracked) if tracked is not None else tracked_files(root)
        self.tracked_set = set(self.tracked)
        self.findings: List[Finding] = []

    def add(
        self,
        level: str,
        code: str,
        message: str,
        path: Optional[str] = None,
        line: Optional[int] = None,
    ) -> None:
        self.findings.append(Finding(level, code, message, path, line))

    def validate(self, base_ref: Optional[str]) -> List[Finding]:
        self.check_required_files()
        self.check_json_files()
        self.check_package_metadata()
        self.check_paths_and_forbidden_files()
        self.check_meta_and_guids()
        self.check_assemblies()
        if base_ref is not None:
            self.check_diff(base_ref)
        return self.findings

    def check_required_files(self) -> None:
        for path in sorted(
            {"package.json", "ValidationExceptions.json", "README.md", "CHANGELOG.md", "LICENSE"}
            - self.tracked_set
        ):
            self.add("error", "required-file", "required package file is not tracked", path)

    def check_json_files(self) -> None:
        for relative in self.tracked:
            if Path(relative).suffix.lower() not in JSON_SUFFIXES:
                continue
            try:
                load_json_strict(self.root / relative)
            except (OSError, UnicodeError, json.JSONDecodeError, DuplicateJsonKey) as error:
                self.add("error", "json", str(error), relative, getattr(error, "lineno", None))

    def check_package_metadata(self) -> None:
        try:
            package = load_json_strict(self.root / "package.json")
            exceptions = load_json_strict(self.root / "ValidationExceptions.json")
        except Exception:
            return
        if not isinstance(package, dict):
            self.add("error", "package", "package.json root must be an object", "package.json")
            return
        if package.get("name") != PACKAGE_NAME:
            self.add("error", "package-name", "expected package name {0!r}".format(PACKAGE_NAME), "package.json")
        version = package.get("version")
        if not is_strict_semver(version):
            self.add("error", "package-version", "version is not strict SemVer", "package.json")
        unity = package.get("unity")
        if not isinstance(unity, str) or not UNITY_VERSION_PATTERN.fullmatch(unity):
            self.add("error", "package-unity", "unity must be numeric major.minor or major.minor.patch", "package.json")
        elif unity != "6000.3":
            self.add(
                "warning",
                "package-unity-target",
                "current package minimum is {0}; PR 15.07 owns the frozen 6000.3 metadata change".format(unity),
                "package.json",
            )
        if not isinstance(exceptions, dict):
            self.add("error", "validation-exceptions", "root must be an object", "ValidationExceptions.json")
            return
        for group in ("ErrorExceptions", "WarningExceptions"):
            entries = exceptions.get(group, [])
            if not isinstance(entries, list):
                self.add("error", "validation-exceptions", "{0} must be an array".format(group), "ValidationExceptions.json")
                continue
            for index, entry in enumerate(entries):
                if not isinstance(entry, dict):
                    self.add("error", "validation-exceptions", "{0}[{1}] must be an object".format(group, index), "ValidationExceptions.json")
                elif entry.get("PackageVersion") != version:
                    self.add(
                        "error",
                        "validation-exception-version",
                        "{0}[{1}] targets {2!r}, expected {3!r}".format(group, index, entry.get("PackageVersion"), version),
                        "ValidationExceptions.json",
                    )

    def check_paths_and_forbidden_files(self) -> None:
        folded_paths: Dict[str, str] = {}
        for relative in self.tracked:
            folded = relative.casefold()
            previous = folded_paths.get(folded)
            if previous and previous != relative:
                self.add("error", "case-collision", "case-insensitive collision with {0!r}".format(previous), relative)
            else:
                folded_paths[folded] = relative
            if any(part.casefold() in FORBIDDEN_ROOTS for part in PurePosixPath(relative).parts):
                self.add("error", "forbidden-root", "generated or machine-local root is tracked", relative)
            if folded.endswith(FORBIDDEN_ENDINGS):
                self.add("error", "forbidden-ending", "generated or binary artifact is tracked", relative)
            try:
                size = (self.root / relative).stat().st_size
            except OSError as error:
                self.add("error", "tracked-file", str(error), relative)
                continue
            if size > MAX_TRACKED_FILE_BYTES:
                self.add("error", "oversized-file", "tracked file exceeds 20 MiB", relative)

    def check_meta_and_guids(self) -> None:
        required_directories: Set[str] = set()
        for relative in self.tracked:
            if relative.endswith(".meta") or not is_unity_visible(relative):
                continue
            if relative + ".meta" not in self.tracked_set:
                self.add("error", "missing-meta", "Unity-visible tracked file has no matching .meta", relative)
            required_directories.update(unity_parent_directories(relative))
        for directory in sorted(required_directories):
            if directory + ".meta" not in self.tracked_set:
                self.add("error", "missing-directory-meta", "Unity-visible directory has no matching .meta", directory)
        guids: Dict[str, str] = {}
        for relative in self.tracked:
            if not relative.endswith(".meta"):
                continue
            try:
                text = (self.root / relative).read_text(encoding="utf-8", errors="replace")
            except OSError as error:
                self.add("error", "meta-read", str(error), relative)
                continue
            match = GUID_PATTERN.search(text)
            if not match:
                self.add("error", "meta-guid", ".meta has no 32-character hexadecimal GUID", relative)
                continue
            guid = match.group(1).lower()
            previous = guids.get(guid)
            if previous:
                self.add("error", "duplicate-guid", "GUID is also used by {0}".format(previous), relative)
            else:
                guids[guid] = relative

    def check_assemblies(self) -> None:
        assemblies: Dict[str, Tuple[str, Dict[str, Any]]] = {}
        folded_names: Dict[str, str] = {}
        guid_to_name: Dict[str, str] = {}
        for relative in (path for path in self.tracked if path.lower().endswith(".asmdef")):
            try:
                data = load_json_strict(self.root / relative)
            except Exception:
                continue
            if not isinstance(data, dict):
                self.add("error", "asmdef", "asmdef root must be an object", relative)
                continue
            name = data.get("name")
            if not isinstance(name, str) or not name:
                self.add("error", "asmdef-name", "asmdef name is required", relative)
                continue
            folded = name.casefold()
            if folded in folded_names:
                self.add("error", "asmdef-duplicate-name", "case-insensitive assembly-name collision with {0!r}".format(folded_names[folded]), relative)
            else:
                folded_names[folded] = name
                assemblies[name] = (relative, data)
            meta = relative + ".meta"
            if meta in self.tracked_set:
                match = GUID_PATTERN.search((self.root / meta).read_text(encoding="utf-8", errors="replace"))
                if match:
                    guid_to_name[match.group(1).lower()] = name

        def resolve(reference: str, relative: str) -> Optional[str]:
            if reference.startswith("GUID:"):
                return guid_to_name.get(reference[5:].lower())
            if reference.startswith("CoCoFlow."):
                actual = folded_names.get(reference.casefold())
                if actual is None:
                    self.add("error", "asmdef-local-reference", "local assembly reference cannot be resolved: {0}".format(reference), relative)
                    return None
                if actual != reference:
                    self.add("error", "asmdef-reference-case", "assembly reference casing must be {0!r}".format(actual), relative)
                return actual
            return reference

        editor_assemblies: Set[str] = set()
        for name, (relative, data) in assemblies.items():
            parts = PurePosixPath(relative).parts
            include = data.get("includePlatforms", [])
            if "Editor" in parts and include != ["Editor"]:
                self.add("error", "asmdef-editor-platform", "Editor-path assembly must use includePlatforms ['Editor']", relative)
            if "Editor" in parts or include == ["Editor"]:
                editor_assemblies.add(name)

        for name, (relative, data) in assemblies.items():
            references = data.get("references", [])
            if not isinstance(references, list) or not all(isinstance(value, str) for value in references):
                self.add("error", "asmdef-references", "references must be an array of strings", relative)
                continue
            seen: Set[str] = set()
            resolved: List[str] = []
            for reference in references:
                folded = reference.casefold()
                if folded in seen:
                    self.add("error", "asmdef-duplicate-reference", "duplicate assembly reference {0!r}".format(reference), relative)
                seen.add(folded)
                target = resolve(reference, relative)
                if target:
                    resolved.append(target)
            if PurePosixPath(relative).parts[0] == "Runtime":
                for target in resolved:
                    if target in editor_assemblies:
                        self.add("error", "runtime-editor-reference", "Runtime assembly references Editor assembly {0}".format(target), relative)
                precompiled = data.get("precompiledReferences", [])
                if isinstance(precompiled, list) and any(
                    isinstance(value, str) and "unityeditor" in value.casefold()
                    for value in precompiled
                ):
                    self.add("error", "runtime-editor-precompiled", "Runtime assembly includes a UnityEditor precompiled reference", relative)

        for relative in (path for path in self.tracked if path.lower().endswith(".asmref")):
            try:
                data = load_json_strict(self.root / relative)
            except Exception:
                continue
            reference = data.get("reference") if isinstance(data, dict) else None
            if not isinstance(reference, str) or not reference:
                self.add("error", "asmref-reference", "asmref reference must be a non-empty string", relative)
                continue
            target = resolve(reference, relative)
            if PurePosixPath(relative).parts[0] == "Runtime" and target in editor_assemblies:
                self.add("error", "runtime-editor-asmref", "Runtime asmref targets an Editor assembly", relative)

    def check_diff(self, base_ref: str) -> None:
        base_ref = base_ref.strip()
        if not base_ref or re.fullmatch(r"0+", base_ref):
            self.add("notice", "diff-check", "base ref is unavailable; repository checks still ran")
            return
        exists = run_command(["git", "cat-file", "-e", base_ref + "^{commit}"], self.root)
        if exists.returncode:
            self.add("error", "diff-base", "base commit is not present locally: {0}".format(base_ref))
            return
        result = run_command(["git", "diff", "--check", base_ref + "..HEAD"], self.root)
        if result.returncode:
            self.add("error", "diff-check", (result.stdout + result.stderr).strip() or "git diff --check failed")


def local_package_uri(root: Path) -> str:
    return root.resolve().as_uri()


def create_clean_host(host: Path, package_root: Path, unity_version: str) -> None:
    (host / "Assets").mkdir(parents=True)
    (host / "Packages").mkdir()
    dependencies = {PACKAGE_NAME: local_package_uri(package_root)}
    dependencies.update(
        {"com.unity.modules." + module: "1.0.0" for module in STANDARD_BUILTIN_MODULES}
    )
    write_json(
        host / "Packages/manifest.json",
        {"dependencies": dependencies, "testables": [PACKAGE_NAME]},
    )
    project_version = host / "ProjectSettings/ProjectVersion.txt"
    project_version.parent.mkdir(parents=True, exist_ok=True)
    project_version.write_text("m_EditorVersion: {0}\n".format(unity_version), encoding="utf-8")


def materialize_head(root: Path, destination: Path) -> str:
    sha = git_output(root, "rev-parse", "HEAD")
    archive = subprocess.run(
        ["git", "archive", "--format=tar", "HEAD"],
        cwd=str(root),
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if archive.returncode:
        raise RuntimeError(archive.stderr.decode(errors="replace").strip())
    destination.mkdir(parents=True, exist_ok=True)
    with tarfile.open(fileobj=io.BytesIO(archive.stdout), mode="r:") as bundle:
        for member in bundle.getmembers():
            path = PurePosixPath(member.name)
            if path.is_absolute() or ".." in path.parts:
                raise RuntimeError("unsafe path in git archive: {0}".format(member.name))
        bundle.extractall(destination)
    return sha


def unity_executable(version: str, overrides: Dict[str, Path]) -> Optional[Path]:
    if version in overrides:
        return overrides[version]
    system = platform.system()
    if system == "Darwin":
        candidate = Path("/Applications/Unity/Hub/Editor") / version / "Unity.app/Contents/MacOS/Unity"
    elif system == "Windows":
        candidate = Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Unity/Hub/Editor" / version / "Editor/Unity.exe"
    else:
        candidate = Path.home() / "Unity/Hub/Editor" / version / "Editor/Unity"
    return candidate if candidate.is_file() else None


def unity_command(
    executable: Path,
    host: Path,
    log: Path,
    mode: Optional[str] = None,
    result: Optional[Path] = None,
) -> List[str]:
    command = [
        str(executable),
        "-batchmode",
        "-nographics",
        "-projectPath",
        str(host),
        "-logFile",
        str(log),
    ]
    if mode:
        command.extend(["-runTests", "-testPlatform", mode])
        if result:
            command.extend(["-testResults", str(result)])
    else:
        command.append("-quit")
    return command


def parse_unity_test_result(path: Path) -> Dict[str, Any]:
    if not path.is_file():
        return {"available": False, "valid": False, "parseError": "result XML is missing"}
    try:
        root = ET.parse(str(path)).getroot()
    except (ET.ParseError, OSError) as error:
        return {"available": True, "valid": False, "parseError": str(error)}
    counts: Dict[str, Any] = {"available": True}
    try:
        for key in ("total", "passed", "failed", "inconclusive", "skipped"):
            counts[key] = int(root.attrib.get(key, "0"))
    except ValueError as error:
        counts.update({"valid": False, "parseError": str(error)})
        return counts
    counts["result"] = root.attrib.get("result")
    counts["valid"] = counts["total"] > 0
    if not counts["valid"]:
        counts["parseError"] = "result XML contains no tests"
    return counts


def parse_editor_overrides(values: Sequence[str]) -> Dict[str, Path]:
    result: Dict[str, Path] = {}
    for value in values:
        if "=" not in value:
            raise ValueError("--editor must use VERSION=/path/to/Unity")
        version, path = value.split("=", 1)
        result[version] = Path(path).expanduser().resolve()
    return result


def run_unity_matrix(args: argparse.Namespace, root: Path) -> int:
    overrides = parse_editor_overrides(args.editor)
    artifact_root = root / args.artifact_root
    system = platform.system().lower()
    with tempfile.TemporaryDirectory(prefix="cocoflow-ci-") as temporary:
        workspace = Path(temporary)
        package_snapshot = workspace / "package"
        sha = materialize_head(root, package_snapshot)
        summary: Dict[str, Any] = {
            "head": sha,
            "source": "git archive HEAD",
            "dirtyWorkingTreeIgnored": True,
            "os": system,
            "versions": {},
        }
        all_passed = True
        for version in UNITY_VERSIONS:
            executable = unity_executable(version, overrides)
            version_artifacts = artifact_root / sha / system / version
            version_artifacts.mkdir(parents=True, exist_ok=True)
            version_result: Dict[str, Any] = {"editor": str(executable) if executable else None}
            summary["versions"][version] = version_result
            if executable is None or not executable.is_file():
                version_result["status"] = "FAIL"
                version_result["error"] = "Unity Editor is not installed"
                all_passed = False
                continue
            host = workspace / ("host-" + version)
            create_clean_host(host, package_snapshot, version)
            import_log = version_artifacts / "import.log"
            imported = run_command(unity_command(executable, host, import_log), root, capture=False)
            version_result["importExitCode"] = imported.returncode
            if imported.returncode:
                version_result["status"] = "FAIL"
                all_passed = False
                continue
            lock = host / "Packages/packages-lock.json"
            if lock.is_file():
                shutil.copy2(lock, version_artifacts / "packages-lock.json")
            mode_passed = True
            for mode in ("EditMode", "PlayMode"):
                result_path = version_artifacts / (mode.lower() + "-results.xml")
                log_path = version_artifacts / (mode.lower() + ".log")
                process = run_command(
                    unity_command(executable, host, log_path, mode.lower(), result_path),
                    root,
                    capture=False,
                )
                parsed = parse_unity_test_result(result_path)
                passed = process.returncode == 0 and parsed.get("valid") is True
                version_result[mode] = {
                    "exitCode": process.returncode,
                    "passed": passed,
                    "result": parsed,
                }
                mode_passed = mode_passed and passed
            version_result["status"] = "PASS" if mode_passed else "FAIL"
            all_passed = all_passed and mode_passed
            if args.keep_host:
                retained = artifact_root / sha / system / ("host-" + version)
                if retained.exists():
                    shutil.rmtree(retained)
                shutil.copytree(host, retained)
        write_json(artifact_root / sha / system / "summary.json", summary)
    return 0 if all_passed else 1


def github_escape(value: str) -> str:
    return value.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")


def emit_annotations(findings: Iterable[Finding]) -> None:
    if os.environ.get("GITHUB_ACTIONS") != "true":
        return
    for finding in findings:
        level = {"error": "error", "warning": "warning"}.get(finding.level, "notice")
        metadata: List[str] = []
        if finding.path:
            metadata.append("file=" + github_escape(finding.path))
        if finding.line:
            metadata.append("line=" + str(finding.line))
        suffix = " " + ",".join(metadata) if metadata else ""
        print("::{0}{1}::{2}".format(level, suffix, github_escape("[{0}] {1}".format(finding.code, finding.message))))


def print_findings(findings: Sequence[Finding]) -> None:
    for finding in findings:
        location = finding.path or "repository"
        if finding.line:
            location += ":" + str(finding.line)
        print("{0}: {1}: [{2}] {3}".format(finding.level.upper(), location, finding.code, finding.message))


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT)
    subparsers = parser.add_subparsers(dest="command", required=True)
    static = subparsers.add_parser("static", help="validate the tracked package")
    static.add_argument("--base-ref")
    static.add_argument("--report", type=Path)
    unity = subparsers.add_parser("unity-matrix", help="run clean-host Unity tests locally")
    unity.add_argument("--editor", action="append", default=[], metavar="VERSION=PATH")
    unity.add_argument("--artifact-root", type=Path, default=Path(".ci-artifacts"))
    unity.add_argument("--keep-host", action="store_true")
    return parser


def main(argv: Optional[Sequence[str]] = None) -> int:
    args = build_parser().parse_args(argv)
    root = args.root.resolve()
    try:
        if args.command == "static":
            validator = RepositoryValidator(root)
            findings = validator.validate(args.base_ref)
            print_findings(findings)
            emit_annotations(findings)
            if args.report:
                report = args.report if args.report.is_absolute() else root / args.report
                write_json(
                    report,
                    {
                        "head": git_output(root, "rev-parse", "HEAD"),
                        "findings": [asdict(finding) for finding in findings],
                    },
                )
            return 1 if any(finding.level == "error" for finding in findings) else 0
        return run_unity_matrix(args, root)
    except (OSError, RuntimeError, ValueError) as error:
        print("ERROR: {0}".format(error), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
