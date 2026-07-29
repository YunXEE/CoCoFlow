#!/usr/bin/env python3
"""CoCoFlow repository, release, and local Unity validation entry point."""

from __future__ import annotations

import argparse
import json
import os
import platform
import re
import shutil
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET
from dataclasses import asdict, dataclass
from pathlib import Path, PurePosixPath
from typing import Any, Dict, Iterable, List, Optional, Sequence, Set, Tuple


SCRIPT_PATH = Path(__file__).resolve()
DEFAULT_ROOT = SCRIPT_PATH.parents[2]
DEFAULT_POLICY = SCRIPT_PATH.with_name("policy.json")
JSON_SUFFIXES = {".json", ".asmdef", ".asmref", ".inputactions"}
SEMVER_PATTERN = re.compile(
    r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)"
    r"(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?"
    r"(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$"
)
UNITY_VERSION_PATTERN = re.compile(r"^\d+\.\d+(?:\.\d+)?$")
GUID_PATTERN = re.compile(r"^guid:\s*([0-9a-fA-F]{32})\s*$", re.MULTILINE)


class DuplicateJsonKey(ValueError):
    """Raised when a JSON object repeats a key."""


@dataclass(frozen=True)
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
    with path.open("r", encoding="utf-8-sig") as stream:
        return json.load(stream, object_pairs_hook=_reject_duplicate_keys)


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )


def run_command(
    command: Sequence[str],
    cwd: Path,
    timeout: Optional[int] = None,
) -> subprocess.CompletedProcess:
    return subprocess.run(
        list(command),
        cwd=str(cwd),
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        timeout=timeout,
        check=False,
    )


def git_output(root: Path, *arguments: str) -> str:
    result = run_command(["git", *arguments], root)
    if result.returncode != 0:
        raise RuntimeError(
            "git {0} failed: {1}".format(" ".join(arguments), result.stderr.strip())
        )
    return result.stdout.strip()


def tracked_files(root: Path) -> List[str]:
    result = subprocess.run(
        ["git", "ls-files", "-z"],
        cwd=str(root),
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if result.returncode != 0:
        raise RuntimeError(
            "git ls-files failed: {0}".format(
                result.stderr.decode("utf-8", errors="replace").strip()
            )
        )
    return [
        entry.decode("utf-8", errors="surrogateescape")
        for entry in result.stdout.split(b"\0")
        if entry
    ]


def github_escape(value: str) -> str:
    return (
        value.replace("%", "%25")
        .replace("\r", "%0D")
        .replace("\n", "%0A")
        .replace(":", "%3A")
        .replace(",", "%2C")
    )


def emit_annotations(findings: Iterable[Finding]) -> None:
    if os.environ.get("GITHUB_ACTIONS") != "true":
        return
    for finding in findings:
        command = {
            "error": "error",
            "warning": "warning",
            "notice": "notice",
        }.get(finding.level, "notice")
        properties: List[str] = []
        if finding.path:
            properties.append("file={0}".format(github_escape(finding.path)))
        if finding.line:
            properties.append("line={0}".format(finding.line))
        metadata = " {0}".format(",".join(properties)) if properties else ""
        print(
            "::{0}{1}::{2}".format(
                command,
                metadata,
                github_escape("[{0}] {1}".format(finding.code, finding.message)),
            )
        )


def is_unity_visible(path: str) -> bool:
    parts = PurePosixPath(path).parts
    if not parts:
        return False
    if any(part.startswith(".") for part in parts):
        return False
    if parts[0] == "Documentation~":
        return False
    if any(part.endswith("~") for part in parts[1:]):
        return False
    return True


def unity_parent_directories(path: str) -> Iterable[str]:
    parent = PurePosixPath(path).parent
    while str(parent) not in {"", "."}:
        value = parent.as_posix()
        if value in {"Samples~", "Documentation~"}:
            break
        yield value
        parent = parent.parent


def expression_implies_unity_editor(expression: str) -> bool:
    """Conservatively determine whether a preprocessor branch requires UNITY_EDITOR."""
    disjunctions = re.split(r"\|\|", expression)
    for disjunction in disjunctions:
        terms = re.split(r"&&", disjunction)
        has_positive_editor = False
        for term in terms:
            normalized = term.replace("(", " ").replace(")", " ").strip()
            if re.search(r"!\s*UNITY_EDITOR\b", normalized):
                continue
            if re.search(r"\bUNITY_EDITOR\b", normalized):
                has_positive_editor = True
        if not has_positive_editor:
            return False
    return True


def _strip_csharp_non_code(line: str, in_block_comment: bool) -> Tuple[str, bool]:
    output: List[str] = []
    index = 0
    quote: Optional[str] = None
    verbatim = False
    while index < len(line):
        if in_block_comment:
            end = line.find("*/", index)
            if end < 0:
                return "".join(output), True
            index = end + 2
            in_block_comment = False
            continue
        if quote:
            char = line[index]
            if verbatim and char == '"' and index + 1 < len(line) and line[index + 1] == '"':
                index += 2
                continue
            if char == quote:
                quote = None
                verbatim = False
                index += 1
                continue
            if not verbatim and char == "\\":
                index += 2
                continue
            index += 1
            continue
        if line.startswith("//", index):
            break
        if line.startswith("/*", index):
            in_block_comment = True
            index += 2
            continue
        if line.startswith('@"', index):
            quote = '"'
            verbatim = True
            index += 2
            continue
        if line[index] in {'"', "'"}:
            quote = line[index]
            index += 1
            continue
        output.append(line[index])
        index += 1
    return "".join(output), in_block_comment


def find_unguarded_unity_editor(source: str) -> List[int]:
    unguarded: List[int] = []
    current_guard = False
    stack: List[bool] = []
    in_block_comment = False
    for number, raw_line in enumerate(source.splitlines(), start=1):
        directive = re.match(r"^\s*#\s*(if|elif|else|endif)\b(.*)$", raw_line)
        if directive:
            keyword = directive.group(1)
            expression = directive.group(2).strip()
            if keyword == "if":
                stack.append(current_guard)
                current_guard = current_guard or expression_implies_unity_editor(
                    expression
                )
            elif keyword == "elif":
                parent_guard = stack[-1] if stack else False
                current_guard = parent_guard or expression_implies_unity_editor(
                    expression
                )
            elif keyword == "else":
                current_guard = stack[-1] if stack else False
            elif keyword == "endif":
                current_guard = stack.pop() if stack else False
            continue
        code, in_block_comment = _strip_csharp_non_code(
            raw_line, in_block_comment
        )
        if re.search(r"\bUnityEditor\b", code) and not current_guard:
            unguarded.append(number)
    return unguarded


class RepositoryValidator:
    def __init__(
        self,
        root: Path,
        policy: Dict[str, Any],
        tracked: Optional[Sequence[str]] = None,
    ) -> None:
        self.root = root
        self.policy = policy
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

    def validate(self, base_ref: Optional[str] = None) -> List[Finding]:
        self.check_required_files()
        self.check_json_files()
        self.check_package_metadata()
        self.check_paths_and_forbidden_files()
        self.check_meta_and_guids()
        self.check_assemblies()
        self.check_runtime_editor_boundary()
        if base_ref is not None:
            self.check_diff(base_ref)
        return self.findings

    def check_required_files(self) -> None:
        required = {
            "package.json",
            "ValidationExceptions.json",
            "README.md",
            "CHANGELOG.md",
            "LICENSE",
        }
        for path in sorted(required - self.tracked_set):
            self.add("error", "required-file", "required package file is not tracked", path)

    def check_json_files(self) -> None:
        for relative in self.tracked:
            if Path(relative).suffix.lower() not in JSON_SUFFIXES:
                continue
            try:
                load_json_strict(self.root / relative)
            except (OSError, UnicodeError, json.JSONDecodeError, DuplicateJsonKey) as error:
                line = getattr(error, "lineno", None)
                self.add("error", "json", str(error), relative, line)

    def check_package_metadata(self) -> None:
        package_path = self.root / "package.json"
        exceptions_path = self.root / "ValidationExceptions.json"
        try:
            package = load_json_strict(package_path)
        except Exception:
            return
        if package.get("name") != self.policy.get("packageName"):
            self.add(
                "error",
                "package-name",
                "expected package name {0!r}".format(self.policy.get("packageName")),
                "package.json",
            )
        version = package.get("version")
        if not isinstance(version, str) or not SEMVER_PATTERN.fullmatch(version):
            self.add("error", "package-version", "version is not valid SemVer", "package.json")
        unity = package.get("unity")
        if not isinstance(unity, str) or not UNITY_VERSION_PATTERN.fullmatch(unity):
            self.add(
                "error",
                "package-unity",
                "unity must be a numeric major.minor or major.minor.patch value",
                "package.json",
            )
        minimum = self.policy.get("packageMinimum", {})
        target = minimum.get("target")
        if isinstance(unity, str) and target and unity != target:
            self.add(
                "warning",
                "package-unity-target",
                "current package minimum is {0}; frozen target {1} remains report-only "
                "and is owned by {2}".format(
                    unity, target, minimum.get("owner", "a later PR")
                ),
                "package.json",
            )
        try:
            exceptions = load_json_strict(exceptions_path)
        except Exception:
            return
        for group in ("ErrorExceptions", "WarningExceptions"):
            entries = exceptions.get(group, [])
            if not isinstance(entries, list):
                self.add(
                    "error",
                    "validation-exceptions",
                    "{0} must be an array".format(group),
                    "ValidationExceptions.json",
                )
                continue
            for index, entry in enumerate(entries):
                if not isinstance(entry, dict):
                    self.add(
                        "error",
                        "validation-exceptions",
                        "{0}[{1}] must be an object".format(group, index),
                        "ValidationExceptions.json",
                    )
                elif entry.get("PackageVersion") != version:
                    self.add(
                        "error",
                        "validation-exception-version",
                        "{0}[{1}] targets {2!r}, expected {3!r}".format(
                            group, index, entry.get("PackageVersion"), version
                        ),
                        "ValidationExceptions.json",
                    )

    def check_paths_and_forbidden_files(self) -> None:
        lower_paths: Dict[str, str] = {}
        forbidden_roots = {
            value.casefold() for value in self.policy.get("forbiddenRootNames", [])
        }
        forbidden_extensions = {
            value.casefold() for value in self.policy.get("forbiddenExtensions", [])
        }
        maximum = int(self.policy.get("maxTrackedFileBytes", 20971520))
        for relative in self.tracked:
            folded = relative.casefold()
            previous = lower_paths.get(folded)
            if previous and previous != relative:
                self.add(
                    "error",
                    "case-collision",
                    "tracked paths collide on case-insensitive filesystems: "
                    "{0!r} and {1!r}".format(previous, relative),
                    relative,
                )
            else:
                lower_paths[folded] = relative
            parts = PurePosixPath(relative).parts
            if any(part.casefold() in forbidden_roots for part in parts):
                self.add(
                    "error",
                    "forbidden-root",
                    "generated or local-only root is tracked",
                    relative,
                )
            suffix = Path(relative).suffix.casefold()
            if suffix in forbidden_extensions:
                self.add(
                    "error",
                    "forbidden-extension",
                    "binary or generated artifact extension is not allowed",
                    relative,
                )
            path = self.root / relative
            try:
                size = path.stat().st_size
            except OSError as error:
                self.add("error", "tracked-file", str(error), relative)
                continue
            if size > maximum:
                self.add(
                    "error",
                    "oversized-file",
                    "tracked file is {0} bytes; limit is {1}".format(size, maximum),
                    relative,
                )

    def check_meta_and_guids(self) -> None:
        required_directories: Set[str] = set()
        for relative in self.tracked:
            if relative.endswith(".meta") or not is_unity_visible(relative):
                continue
            if "{0}.meta".format(relative) not in self.tracked_set:
                self.add(
                    "error",
                    "missing-meta",
                    "Unity-visible tracked file has no matching .meta",
                    relative,
                )
            required_directories.update(unity_parent_directories(relative))
        for directory in sorted(required_directories):
            if "{0}.meta".format(directory) not in self.tracked_set:
                self.add(
                    "error",
                    "missing-directory-meta",
                    "Unity-visible directory has no matching .meta",
                    directory,
                )
        guids: Dict[str, str] = {}
        for relative in self.tracked:
            if not relative.endswith(".meta"):
                continue
            try:
                text = (self.root / relative).read_text(
                    encoding="utf-8", errors="replace"
                )
            except OSError as error:
                self.add("error", "meta-read", str(error), relative)
                continue
            match = GUID_PATTERN.search(text)
            if not match:
                self.add(
                    "error",
                    "meta-guid",
                    ".meta file does not contain a 32-character hexadecimal GUID",
                    relative,
                )
                continue
            guid = match.group(1).lower()
            previous = guids.get(guid)
            if previous:
                self.add(
                    "error",
                    "duplicate-guid",
                    "GUID is also used by {0}".format(previous),
                    relative,
                )
            else:
                guids[guid] = relative

    def check_assemblies(self) -> None:
        assembly_paths = [
            path for path in self.tracked if path.lower().endswith(".asmdef")
        ]
        assemblies: Dict[str, Tuple[str, Dict[str, Any]]] = {}
        guid_to_name: Dict[str, str] = {}
        for relative in assembly_paths:
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
            if name in assemblies:
                self.add(
                    "error",
                    "asmdef-duplicate-name",
                    "assembly name is also declared by {0}".format(assemblies[name][0]),
                    relative,
                )
            else:
                assemblies[name] = (relative, data)
            meta = "{0}.meta".format(relative)
            if meta in self.tracked_set:
                match = GUID_PATTERN.search(
                    (self.root / meta).read_text(encoding="utf-8", errors="replace")
                )
                if match:
                    guid_to_name[match.group(1).lower()] = name
        for name, (relative, data) in assemblies.items():
            references = data.get("references", [])
            if not isinstance(references, list) or not all(
                isinstance(value, str) for value in references
            ):
                self.add(
                    "error",
                    "asmdef-references",
                    "references must be an array of strings",
                    relative,
                )
                references = []
            seen: Set[str] = set()
            resolved_references: List[str] = []
            for reference in references:
                folded = reference.casefold()
                if folded in seen:
                    self.add(
                        "error",
                        "asmdef-duplicate-reference",
                        "duplicate assembly reference {0!r}".format(reference),
                        relative,
                    )
                seen.add(folded)
                resolved = reference
                if reference.startswith("GUID:"):
                    resolved = guid_to_name.get(reference[5:].lower(), "")
                    if not resolved:
                        self.add(
                            "error",
                            "asmdef-guid-reference",
                            "local GUID reference cannot be resolved: {0}".format(
                                reference
                            ),
                            relative,
                        )
                elif reference.startswith("CoCoFlow.") and reference not in assemblies:
                    self.add(
                        "error",
                        "asmdef-local-reference",
                        "CoCoFlow assembly reference cannot be resolved: {0}".format(
                            reference
                        ),
                        relative,
                    )
                if resolved:
                    resolved_references.append(resolved)
            parts = PurePosixPath(relative).parts
            is_editor_path = "Editor" in parts
            include_platforms = data.get("includePlatforms", [])
            if is_editor_path and include_platforms != ["Editor"]:
                self.add(
                    "error",
                    "asmdef-editor-platform",
                    "Editor-path assembly must set includePlatforms to exactly ['Editor']",
                    relative,
                )
            if parts and parts[0] == "Runtime":
                for reference in resolved_references:
                    target = assemblies.get(reference)
                    if target and "Editor" in PurePosixPath(target[0]).parts:
                        self.add(
                            "error",
                            "runtime-editor-reference",
                            "Runtime assembly references Editor assembly {0}".format(
                                reference
                            ),
                            relative,
                        )
                precompiled = data.get("precompiledReferences", [])
                if isinstance(precompiled, list) and any(
                    isinstance(value, str)
                    and "unityeditor" in value.casefold()
                    for value in precompiled
                ):
                    self.add(
                        "error",
                        "runtime-editor-precompiled",
                        "Runtime assembly includes a UnityEditor precompiled reference",
                        relative,
                    )
        for relative in [
            path for path in self.tracked if path.lower().endswith(".asmref")
        ]:
            try:
                data = load_json_strict(self.root / relative)
            except Exception:
                continue
            reference = data.get("reference") if isinstance(data, dict) else None
            if not isinstance(reference, str) or not reference:
                self.add(
                    "error",
                    "asmref-reference",
                    "asmref reference must be a non-empty string",
                    relative,
                )
                continue
            resolved = reference
            if reference.startswith("GUID:"):
                resolved = guid_to_name.get(reference[5:].lower(), "")
                if not resolved:
                    self.add(
                        "error",
                        "asmref-guid-reference",
                        "asmref GUID cannot be resolved",
                        relative,
                    )
            elif reference.startswith("CoCoFlow.") and reference not in assemblies:
                self.add(
                    "error",
                    "asmref-local-reference",
                    "CoCoFlow asmref target cannot be resolved",
                    relative,
                )
            if (
                PurePosixPath(relative).parts
                and PurePosixPath(relative).parts[0] == "Runtime"
                and resolved in assemblies
                and "Editor" in PurePosixPath(assemblies[resolved][0]).parts
            ):
                self.add(
                    "error",
                    "runtime-editor-asmref",
                    "Runtime asmref targets an Editor assembly",
                    relative,
                )

    def check_runtime_editor_boundary(self) -> None:
        for relative in self.tracked:
            if not relative.startswith("Runtime/") or not relative.endswith(".cs"):
                continue
            try:
                source = (self.root / relative).read_text(
                    encoding="utf-8-sig", errors="replace"
                )
            except OSError as error:
                self.add("error", "runtime-source", str(error), relative)
                continue
            for line in find_unguarded_unity_editor(source):
                self.add(
                    "error",
                    "runtime-unity-editor",
                    "UnityEditor reference is not protected by a branch that "
                    "requires UNITY_EDITOR",
                    relative,
                    line,
                )

    def check_diff(self, base_ref: str) -> None:
        base_ref = base_ref.strip()
        if not base_ref or re.fullmatch(r"0+", base_ref):
            self.add(
                "notice",
                "diff-check",
                "base ref is unavailable; repository checks still ran",
            )
            return
        result = run_command(["git", "diff", "--check", "{0}..HEAD".format(base_ref)], self.root)
        if result.returncode != 0:
            self.add(
                "error",
                "diff-check",
                (result.stdout + result.stderr).strip() or "git diff --check failed",
            )


def stable_version(value: Any) -> bool:
    match = SEMVER_PATTERN.fullmatch(value) if isinstance(value, str) else None
    return bool(match and match.group(4) is None)


def release_findings(
    root: Path,
    policy: Dict[str, Any],
    head_ref: str,
    base_ref: str,
    head_repository: Optional[str] = None,
    repository: Optional[str] = None,
    existing_tags: Optional[Set[str]] = None,
) -> List[Finding]:
    findings: List[Finding] = []

    def add(
        code: str, message: str, path: Optional[str] = None
    ) -> None:
        findings.append(Finding("error", code, message, path))

    if base_ref != "master":
        add("release-base", "release policy only accepts base branch 'master'")
    if head_repository and repository and head_repository.casefold() != repository.casefold():
        add("release-fork", "master release PR must originate from the same repository")
    branch_match = re.fullmatch(r"dev/(\d+\.\d+\.\d+)", head_ref)
    if branch_match is None:
        branch_match = re.fullmatch(
            r"hotfix/(\d+\.\d+\.\d+)-[A-Za-z0-9][A-Za-z0-9._-]*", head_ref
        )
    expected = branch_match.group(1) if branch_match else None
    if expected is None:
        add(
            "release-head",
            "head must match dev/X.Y.Z or hotfix/X.Y.Z-topic",
        )
    try:
        package = load_json_strict(root / "package.json")
    except Exception as error:
        add("release-package", str(error), "package.json")
        return findings
    version = package.get("version")
    if not stable_version(version):
        add(
            "release-version",
            "package version must be stable SemVer without a prerelease suffix",
            "package.json",
        )
    if expected and version != expected:
        add(
            "release-version-branch",
            "package version {0!r} does not match source branch version {1!r}".format(
                version, expected
            ),
            "package.json",
        )
    changelog = (root / "CHANGELOG.md").read_text(
        encoding="utf-8-sig", errors="replace"
    )
    if isinstance(version, str):
        heading = re.compile(
            r"^## \[{0}\] - \d{{4}}-\d{{2}}-\d{{2}}$".format(re.escape(version)),
            re.MULTILINE,
        )
        if not heading.search(changelog):
            add(
                "release-changelog",
                "CHANGELOG.md needs a dated heading for version {0}".format(version),
                "CHANGELOG.md",
            )
        tags = (
            existing_tags
            if existing_tags is not None
            else set(git_output(root, "tag", "--list").splitlines())
        )
        if "v{0}".format(version) in tags:
            add(
                "release-tag",
                "tag v{0} already exists and must not be reused".format(version),
            )
    try:
        exceptions = load_json_strict(root / "ValidationExceptions.json")
    except Exception as error:
        add("release-exceptions", str(error), "ValidationExceptions.json")
        return findings
    for group in ("ErrorExceptions", "WarningExceptions"):
        for index, entry in enumerate(exceptions.get(group, [])):
            if entry.get("PackageVersion") != version:
                add(
                    "release-exception-version",
                    "{0}[{1}] does not target the release version".format(group, index),
                    "ValidationExceptions.json",
                )
            if "pre-release tag" in str(entry.get("ExceptionMessage", "")).casefold():
                add(
                    "release-prerelease-exception",
                    "prerelease-only validation exception must be removed",
                    "ValidationExceptions.json",
                )
    return findings


def local_package_uri(root: Path) -> str:
    normalized = root.resolve().as_posix()
    return "file:{0}".format(normalized)


def create_clean_host(host: Path, root: Path, package_name: str, unity_version: str) -> None:
    packages = host / "Packages"
    settings = host / "ProjectSettings"
    packages.mkdir(parents=True, exist_ok=True)
    settings.mkdir(parents=True, exist_ok=True)
    write_json(
        packages / "manifest.json",
        {
            "dependencies": {package_name: local_package_uri(root)},
            "testables": [package_name],
        },
    )
    (settings / "ProjectVersion.txt").write_text(
        "m_EditorVersion: {0}\n".format(unity_version),
        encoding="utf-8",
    )


def unity_executable(version: str, overrides: Dict[str, Path]) -> Optional[Path]:
    if version in overrides:
        return overrides[version]
    candidates: List[Path] = []
    system = platform.system()
    if system == "Darwin":
        candidates.append(
            Path(
                "/Applications/Unity/Hub/Editor/{0}/Unity.app/Contents/MacOS/Unity".format(
                    version
                )
            )
        )
    elif system == "Windows":
        candidates.append(
            Path(
                "C:/Program Files/Unity/Hub/Editor/{0}/Editor/Unity.exe".format(
                    version
                )
            )
        )
    else:
        candidates.extend(
            [
                Path("/opt/unity/{0}/Editor/Unity".format(version)),
                Path("/opt/Unity/Hub/Editor/{0}/Editor/Unity".format(version)),
            ]
        )
    return next((candidate for candidate in candidates if candidate.is_file()), None)


def unity_command(
    executable: Path,
    host: Path,
    log_path: Path,
    mode: Optional[str] = None,
    result_path: Optional[Path] = None,
) -> List[str]:
    command = [
        str(executable),
        "-batchmode",
        "-nographics",
        "-projectPath",
        str(host),
        "-logFile",
        str(log_path),
    ]
    if mode:
        command.extend(["-runTests", "-testPlatform", mode])
        if result_path is not None:
            command.extend(["-testResults", str(result_path)])
    else:
        command.append("-quit")
    return command


def parse_unity_test_result(path: Path) -> Dict[str, Any]:
    if not path.is_file():
        return {"available": False}
    try:
        root = ET.parse(str(path)).getroot()
    except (OSError, ET.ParseError) as error:
        return {"available": False, "parseError": str(error)}
    result: Dict[str, Any] = {"available": True}
    for key in ("total", "passed", "failed", "inconclusive", "skipped"):
        value = root.attrib.get(key)
        if value is not None:
            try:
                result[key] = int(value)
            except ValueError:
                result[key] = value
    failed_cases: List[str] = []
    inconclusive_cases: List[str] = []
    for case in root.iter("test-case"):
        outcome = str(case.attrib.get("result", "")).casefold()
        name = case.attrib.get("fullname") or case.attrib.get("name")
        if not name:
            continue
        if outcome in {"failed", "failure"}:
            failed_cases.append(name)
        elif outcome == "inconclusive":
            inconclusive_cases.append(name)
    result["failedCases"] = failed_cases
    result["inconclusiveCases"] = inconclusive_cases
    return result


def parse_editor_overrides(values: Sequence[str]) -> Dict[str, Path]:
    overrides: Dict[str, Path] = {}
    for value in values:
        if "=" not in value:
            raise ValueError("--editor must use VERSION=/path/to/Unity")
        version, path = value.split("=", 1)
        overrides[version] = Path(path).expanduser().resolve()
    return overrides


def run_unity_matrix(args: argparse.Namespace, root: Path, policy: Dict[str, Any]) -> int:
    versions = args.version or list(policy.get("unityEditors", []))
    overrides = parse_editor_overrides(args.editor)
    artifact_root = (
        root
        / policy.get("artifactRoot", ".ci-artifacts")
        / git_output(root, "rev-parse", "HEAD")
        / platform.system().lower()
    )
    artifact_root.mkdir(parents=True, exist_ok=True)
    matrix: Dict[str, Any] = {
        "schemaVersion": 1,
        "command": "unity-matrix",
        "sha": git_output(root, "rev-parse", "HEAD"),
        "os": platform.platform(),
        "startedAtUnix": int(time.time()),
        "versions": [],
    }
    any_failure = False
    for version in versions:
        editor = unity_executable(version, overrides)
        version_artifacts = artifact_root / version
        version_artifacts.mkdir(parents=True, exist_ok=True)
        entry: Dict[str, Any] = {
            "version": version,
            "editor": str(editor) if editor else None,
            "modes": {},
        }
        matrix["versions"].append(entry)
        if editor is None or not editor.is_file():
            entry["result"] = "UNVERIFIED"
            entry["reason"] = "Unity editor executable was not found"
            any_failure = True
            continue
        host = Path(
            tempfile.mkdtemp(prefix="cocoflow-ci-{0}-".format(version))
        ).resolve()
        entry["host"] = str(host)
        try:
            create_clean_host(
                host, root, policy["packageName"], version
            )
            import_log = version_artifacts / "import.log"
            import_command = unity_command(editor, host, import_log)
            entry["importCommand"] = import_command
            try:
                imported = run_command(
                    import_command, root, timeout=args.timeout_seconds
                )
                entry["importExitCode"] = imported.returncode
            except subprocess.TimeoutExpired:
                entry["importExitCode"] = None
                entry["importTimedOut"] = True
            if entry.get("importExitCode") != 0:
                entry["result"] = "FAIL"
                any_failure = True
                continue
            for mode in ("editmode", "playmode"):
                result_path = version_artifacts / "{0}-results.xml".format(mode)
                log_path = version_artifacts / "{0}.log".format(mode)
                command = unity_command(editor, host, log_path, mode, result_path)
                mode_entry: Dict[str, Any] = {"command": command}
                entry["modes"][mode] = mode_entry
                try:
                    completed = run_command(
                        command, root, timeout=args.timeout_seconds
                    )
                    mode_entry["exitCode"] = completed.returncode
                except subprocess.TimeoutExpired:
                    mode_entry["exitCode"] = None
                    mode_entry["timedOut"] = True
                mode_entry["tests"] = parse_unity_test_result(result_path)
                if mode_entry.get("exitCode") != 0:
                    any_failure = True
            entry["result"] = (
                "PASS"
                if all(
                    value.get("exitCode") == 0
                    for value in entry["modes"].values()
                )
                else "FAIL"
            )
        finally:
            if args.keep_host:
                entry["hostRetained"] = True
            else:
                shutil.rmtree(host, ignore_errors=True)
                entry["hostRetained"] = False
    matrix["result"] = "FAIL" if any_failure else "PASS"
    matrix["finishedAtUnix"] = int(time.time())
    write_json(artifact_root / "summary.json", matrix)
    markdown = [
        "# CoCoFlow Unity clean-host matrix",
        "",
        "- Final Head: `{0}`".format(matrix["sha"]),
        "- OS: `{0}`".format(matrix["os"]),
        "- Result: **{0}**".format(matrix["result"]),
        "",
        "| Unity | Import | EditMode | PlayMode | Result |",
        "|---|---:|---:|---:|---|",
    ]
    for entry in matrix["versions"]:
        markdown.append(
            "| {0} | {1} | {2} | {3} | {4} |".format(
                entry["version"],
                entry.get("importExitCode", "N/A"),
                entry.get("modes", {}).get("editmode", {}).get("exitCode", "N/A"),
                entry.get("modes", {}).get("playmode", {}).get("exitCode", "N/A"),
                entry.get("result", "UNVERIFIED"),
            )
        )
    (artifact_root / "summary.md").write_text(
        "\n".join(markdown) + "\n", encoding="utf-8"
    )
    print("\n".join(markdown))
    print("\nArtifacts: {0}".format(artifact_root))
    return 1 if any_failure else 0


def result_document(
    command: str,
    root: Path,
    findings: Sequence[Finding],
) -> Dict[str, Any]:
    try:
        sha = git_output(root, "rev-parse", "HEAD")
    except RuntimeError:
        sha = "UNVERIFIED"
    errors = sum(finding.level == "error" for finding in findings)
    warnings = sum(finding.level == "warning" for finding in findings)
    return {
        "schemaVersion": 1,
        "command": command,
        "sha": sha,
        "os": platform.platform(),
        "python": platform.python_version(),
        "result": "FAIL" if errors else "PASS",
        "counts": {
            "errors": errors,
            "warnings": warnings,
            "notices": sum(finding.level == "notice" for finding in findings),
        },
        "findings": [asdict(finding) for finding in findings],
    }


def print_findings(findings: Sequence[Finding]) -> None:
    for finding in findings:
        location = finding.path or "repository"
        if finding.line:
            location = "{0}:{1}".format(location, finding.line)
        print(
            "{0}: {1}: [{2}] {3}".format(
                finding.level.upper(), location, finding.code, finding.message
            )
        )


def load_policy(path: Path) -> Dict[str, Any]:
    policy = load_json_strict(path)
    if not isinstance(policy, dict) or policy.get("schemaVersion") != 1:
        raise ValueError("policy must be an object with schemaVersion 1")
    return policy


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--root",
        type=Path,
        default=DEFAULT_ROOT,
        help="repository root (default: inferred from this script)",
    )
    parser.add_argument(
        "--policy",
        type=Path,
        default=DEFAULT_POLICY,
        help="policy JSON path",
    )
    subparsers = parser.add_subparsers(dest="command", required=True)
    static = subparsers.add_parser("static", help="validate tracked package files")
    static.add_argument("--base-ref", default=None)
    static.add_argument("--report", type=Path)
    release = subparsers.add_parser(
        "release", help="validate a dev/hotfix PR into master"
    )
    release.add_argument("--head-ref", required=True)
    release.add_argument("--base-ref", default="master")
    release.add_argument("--head-repository")
    release.add_argument("--repository")
    release.add_argument("--report", type=Path)
    unity = subparsers.add_parser(
        "unity-matrix", help="run disposable clean-host Unity tests"
    )
    unity.add_argument("--version", action="append")
    unity.add_argument(
        "--editor",
        action="append",
        default=[],
        help="override editor path using VERSION=/path/to/Unity",
    )
    unity.add_argument("--keep-host", action="store_true")
    unity.add_argument("--timeout-seconds", type=int, default=3600)
    return parser


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    root = args.root.resolve()
    try:
        policy = load_policy(args.policy.resolve())
        if args.command == "static":
            findings = RepositoryValidator(root, policy).validate(args.base_ref)
        elif args.command == "release":
            findings = release_findings(
                root,
                policy,
                args.head_ref,
                args.base_ref,
                args.head_repository,
                args.repository,
            )
        elif args.command == "unity-matrix":
            return run_unity_matrix(args, root, policy)
        else:
            parser.error("unsupported command")
            return 2
    except (
        DuplicateJsonKey,
        OSError,
        RuntimeError,
        ValueError,
        json.JSONDecodeError,
    ) as error:
        print("INFRASTRUCTURE ERROR: {0}".format(error), file=sys.stderr)
        return 2
    emit_annotations(findings)
    print_findings(findings)
    document = result_document(args.command, root, findings)
    if getattr(args, "report", None):
        report = args.report
        if not report.is_absolute():
            report = root / report
        write_json(report, document)
        print("Report: {0}".format(report))
    print(
        "{0}: {1} error(s), {2} warning(s), {3} notice(s)".format(
            document["result"],
            document["counts"]["errors"],
            document["counts"]["warnings"],
            document["counts"]["notices"],
        )
    )
    return 1 if document["counts"]["errors"] else 0


if __name__ == "__main__":
    sys.exit(main())
