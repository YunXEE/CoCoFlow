#!/usr/bin/env python3
"""Small, dependency-free CI entry points for the CoCoFlow Unity package."""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from dataclasses import asdict, dataclass
from pathlib import Path, PurePosixPath
from typing import Any, Dict, Iterable, List, Optional, Sequence, Set, Tuple


ROOT = Path(__file__).resolve().parents[2]
PACKAGE_NAME = "com.yunxee.cocoflow"
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
ASSEMBLY_GUID_REFERENCE_PATTERN = re.compile(r"^GUID:[0-9a-fA-F]{32}$")
SEMVER_PATTERN = re.compile(
    r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)"
    r"(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?"
    r"(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$"
)
UNITY_VERSION_PATTERN = re.compile(r"^\d+\.\d+$")


class DuplicateJsonKey(ValueError):
    pass


class InvalidJsonConstant(ValueError):
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


def _reject_json_constant(value: str) -> None:
    raise InvalidJsonConstant("non-finite JSON constant: {0}".format(value))


def load_json_strict(path: Path) -> Any:
    with path.open(encoding="utf-8-sig") as handle:
        return json.load(
            handle,
            object_pairs_hook=_reject_duplicate_keys,
            parse_constant=_reject_json_constant,
        )


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

    def validate(
        self,
        base_ref: Optional[str],
        missing_base: str = "error",
        diff_mode: str = "direct",
    ) -> List[Finding]:
        self.check_required_files()
        self.check_json_files()
        self.check_package_metadata()
        self.check_paths_and_forbidden_files()
        self.check_meta_and_guids()
        self.check_assemblies()
        if base_ref is not None:
            self.check_diff(base_ref, missing_base, diff_mode)
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
            except (
                OSError,
                UnicodeError,
                json.JSONDecodeError,
                DuplicateJsonKey,
                InvalidJsonConstant,
            ) as error:
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
            self.add("error", "package-unity", "unity must be numeric major.minor", "package.json")
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
            parts = PurePosixPath(relative).parts
            if parts and parts[0].casefold() in FORBIDDEN_ROOTS:
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
            owner = relative[:-5]
            owner_prefix = owner.rstrip("/") + "/"
            has_tracked_owner = owner in self.tracked_set or any(
                tracked.startswith(owner_prefix) for tracked in self.tracked
            )
            if not has_tracked_owner:
                self.add(
                    "error",
                    "orphan-meta",
                    "tracked .meta has no matching tracked asset or existing directory",
                    relative,
                )
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

        def reference_form(reference: str, relative: str) -> Optional[str]:
            if not reference or reference != reference.strip():
                self.add(
                    "error",
                    "assembly-reference-syntax",
                    "assembly reference must be a non-empty trimmed string",
                    relative,
                )
                return None
            if reference.casefold().startswith("guid:"):
                if not ASSEMBLY_GUID_REFERENCE_PATTERN.fullmatch(reference):
                    self.add(
                        "error",
                        "assembly-reference-syntax",
                        "GUID reference must use GUID: followed by 32 hexadecimal characters",
                        relative,
                    )
                    return None
                return "guid"
            return "name"

        def resolve(reference: str, relative: str, form: str) -> Optional[str]:
            if form == "guid":
                return guid_to_name.get(reference[5:].lower())
            if reference.casefold().startswith("cocoflow."):
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
            forms: Set[str] = set()
            resolved: List[str] = []
            for reference in references:
                folded = reference.casefold()
                if folded in seen:
                    self.add("error", "asmdef-duplicate-reference", "duplicate assembly reference {0!r}".format(reference), relative)
                seen.add(folded)
                form = reference_form(reference, relative)
                if form is None:
                    continue
                forms.add(form)
                target = resolve(reference, relative, form)
                if target:
                    resolved.append(target)
            if len(forms) > 1:
                self.add(
                    "error",
                    "asmdef-mixed-reference-format",
                    "references must use either assembly names or GUIDs, not both",
                    relative,
                )
            if name not in editor_assemblies:
                for target in resolved:
                    if target in editor_assemblies:
                        self.add("error", "runtime-editor-reference", "non-Editor assembly references Editor assembly {0}".format(target), relative)
                precompiled = data.get("precompiledReferences", [])
                if isinstance(precompiled, list) and any(
                    isinstance(value, str) and "unityeditor" in value.casefold()
                    for value in precompiled
                ):
                    self.add("error", "runtime-editor-precompiled", "non-Editor assembly includes a UnityEditor precompiled reference", relative)

        for relative in (path for path in self.tracked if path.lower().endswith(".asmref")):
            try:
                data = load_json_strict(self.root / relative)
            except Exception:
                continue
            reference = data.get("reference") if isinstance(data, dict) else None
            if not isinstance(reference, str):
                self.add("error", "asmref-reference", "asmref reference must be a string", relative)
                continue
            form = reference_form(reference, relative)
            if form is None:
                continue
            target = resolve(reference, relative, form)
            if PurePosixPath(relative).parts[0] == "Runtime" and target in editor_assemblies:
                self.add("error", "runtime-editor-asmref", "Runtime asmref targets an Editor assembly", relative)

    def check_diff(
        self,
        base_ref: str,
        missing_base: str = "error",
        diff_mode: str = "direct",
    ) -> None:
        base_ref = base_ref.strip()
        if not base_ref or re.fullmatch(r"0+", base_ref):
            self.add("notice", "diff-check", "base ref is unavailable; repository checks still ran")
            return
        exists = run_command(["git", "cat-file", "-e", base_ref + "^{commit}"], self.root)
        if exists.returncode:
            self.add(
                missing_base,
                "diff-base",
                "base commit is not present locally: {0}".format(base_ref),
            )
            return
        separator = "..." if diff_mode == "merge-base" else ".."
        result = run_command(
            ["git", "diff", "--check", base_ref + separator + "HEAD"], self.root
        )
        if result.returncode:
            self.add("error", "diff-check", (result.stdout + result.stderr).strip() or "git diff --check failed")


def parse_unity_test_result(path: Path) -> Dict[str, Any]:
    if not path.is_file():
        return {"available": False, "valid": False, "parseError": "result XML is missing"}
    try:
        root = ET.parse(str(path)).getroot()
    except (ET.ParseError, OSError) as error:
        return {"available": True, "valid": False, "parseError": str(error)}
    if root.tag != "test-run":
        return {
            "available": True,
            "valid": False,
            "parseError": "result XML root must be test-run",
        }
    counts: Dict[str, Any] = {"available": True}
    try:
        for key in ("total", "passed", "failed", "inconclusive", "skipped"):
            counts[key] = int(root.attrib[key])
    except (KeyError, ValueError) as error:
        counts.update({"valid": False, "parseError": str(error)})
        return counts
    counts["result"] = root.attrib.get("result")
    counts["valid"] = (
        counts["total"] > 0
        and all(counts[key] >= 0 for key in ("passed", "failed", "inconclusive", "skipped"))
        and counts["passed"]
        + counts["failed"]
        + counts["inconclusive"]
        + counts["skipped"]
        == counts["total"]
    )
    if not counts["valid"]:
        counts["parseError"] = "result XML contains invalid test counts"
    return counts


def unity_test_passed(result: Dict[str, Any]) -> bool:
    return (
        result.get("valid") is True
        and result.get("result") == "Passed"
        and result.get("failed") == 0
        and result.get("inconclusive") == 0
    )


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
    static.add_argument(
        "--missing-base", choices=("error", "notice"), default="error"
    )
    static.add_argument(
        "--diff-mode", choices=("direct", "merge-base"), default="direct"
    )
    static.add_argument("--report", type=Path)
    unity_result = subparsers.add_parser(
        "unity-result", help="validate one NUnit XML file produced by a local Unity test run"
    )
    unity_result.add_argument("result", type=Path)
    return parser


def main(argv: Optional[Sequence[str]] = None) -> int:
    args = build_parser().parse_args(argv)
    root = args.root.resolve()
    try:
        if args.command == "static":
            validator = RepositoryValidator(root)
            findings = validator.validate(
                args.base_ref, args.missing_base, args.diff_mode
            )
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
        result_path = args.result if args.result.is_absolute() else root / args.result
        result = parse_unity_test_result(result_path)
        print(json.dumps(result, ensure_ascii=False, sort_keys=True))
        return 0 if unity_test_passed(result) else 1
    except (OSError, RuntimeError, ValueError) as error:
        print("ERROR: {0}".format(error), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
