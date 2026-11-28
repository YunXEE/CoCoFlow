"""Compare the candidate's observed module contracts and compiler XML with approved lists."""

import argparse
import hashlib
import json
import re
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

from collect_api_surface import MODULES, git


BASELINES = Path(__file__).resolve().parent / "api-baselines"
INITIAL_BASE = "2109648955b8b7da2fe744ad2d12be4048dc0e87"
INITIAL_BRANCH = "refactor/0.4.11-P007-core-tests"
PLACEHOLDER = re.compile(r"^(?:todo\b|tbd\b|fixme\b|placeholder\b|待补|占位)", re.I)


def read_docs(path, assembly):
    tree = ET.parse(path)
    if tree.findtext("assembly/name") != assembly:
        raise ValueError(f"{path}: XML identity is not {assembly}")
    entries = tree.findall("members/member")
    return unique((entry.attrib["name"], entry) for entry in entries)


def unique(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"Duplicate API/XML identity: {key}")
        result[key] = value
    return result


def normalize_doc(element):
    for node in element.iter():
        if node.tag != "code" and node.text:
            node.text = re.sub(r"\s+", " ", node.text).strip() if node.text.isspace() else re.sub(r"\s+", " ", node.text)
        if node.tail:
            node.tail = re.sub(r"\s+", " ", node.tail) if not node.tail.isspace() else ""
    element.tail = None
    return ET.canonicalize(ET.tostring(element, encoding="unicode"))


def documentation_error(key, all_docs, inherits, seen=()):
    if key in seen:
        return "inheritdoc cycle"
    document = all_docs.get(key)
    if document is None:
        return "missing XML member"
    summary = document.find("summary")
    if summary is not None:
        text = " ".join("".join(summary.itertext()).split())
        if PLACEHOLDER.match(text):
            return "placeholder summary"
        if text or any(node.get("cref") or node.get("name") for node in summary):
            return None
    inherit = document.find(".//inheritdoc")
    if inherit is None:
        return "empty or missing summary"
    targets = [inherit.get("cref")] if inherit.get("cref") else inherits.get(key, [])
    if len(targets) != 1:
        return "inheritdoc does not resolve uniquely"
    return documentation_error(targets[0], all_docs, inherits, (*seen, key))


def json_lines(path):
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines()]


def write_lines(path, rows):
    path.write_text("".join(json.dumps(row, ensure_ascii=False, separators=(",", ":")) + "\n" for row in rows),
                    encoding="utf-8")


def collect_lists(inputs, owners, verify_tree=True):
    provenance = json.loads((inputs / "inputs.json").read_text(encoding="utf-8"))
    if verify_tree and (provenance["sha"], provenance["tree"]) != (
        git("rev-parse", "HEAD"), git("rev-parse", "HEAD^{tree}"),
    ):
        raise ValueError("Unity API inputs were collected from a different checkout")
    records = {module: {} for module in MODULES}
    docs = {}
    all_docs = {}
    inherits = {}
    for module, assemblies in MODULES.items():
        for assembly in assemblies:
            fingerprints = provenance["assemblies"][assembly]["inputs"]
            for suffix in (".dll", ".xml", ".json"):
                path = inputs / (assembly + suffix)
                if hashlib.sha256(path.read_bytes()).hexdigest() != fingerprints[path.name]:
                    raise ValueError(f"{path}: artifact differs from the collection record")
            entries = json.loads((inputs / (assembly + ".json")).read_text(encoding="utf-8"))
            documents = read_docs(inputs / (assembly + ".xml"), assembly)
            all_docs.update(documents)
            for entry in entries:
                key = entry["Id"]
                group = module if module == "Core" else owners.get((assembly, entry["Owner"]))
                if group not in (module, "Host"):
                    raise ValueError(f"{assembly}: unclaimed declaring type {entry['Owner']} ({entry['Source']})")
                identity = assembly, key
                if identity in records[module]:
                    raise ValueError(f"{assembly}: duplicate {key}")
                docs[identity] = entry["NeedsDocs"] and group != "Host"
                inherits[key] = entry["Inherits"].splitlines()
                contract = normalize_doc(documents[key]) if key in documents else ""
                records[module][identity] = [assembly, key, group, entry["Signature"], contract]
    return records, docs, all_docs, inherits


def compare(label, expected, actual):
    errors = []
    for key in sorted(expected.keys() | actual.keys()):
        before = expected.get(key)
        after = actual.get(key)
        if before != after:
            kind = "added" if before is None else "missing" if after is None else "changed"
            errors.append(f"{label}: {kind} {key[0]} {key[1]}\n  expected: {before}\n  actual:   {after}")
    return errors


def approved_ref(base, head, head_branch, base_branch):
    if (base, head_branch, base_branch) == (INITIAL_BASE, INITIAL_BRANCH, "dev/0.4.11"):
        return None
    if base_branch == "master" and head_branch.startswith("dev/"):
        names = [path.relative_to(BASELINES.parents[2]).as_posix()
                 for path in (BASELINES / "Core.jsonl", BASELINES / "StateGraph.jsonl")]
        present = [subprocess.run(["git", "cat-file", "-e", f"{base}:{name}"],
                                  stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode == 0
                   for name in names]
        if not any(present):
            # The first release promotes lists already reviewed and merged into dev.
            return head
    return base


def check(args):
    owners = unique(((assembly, name), group) for assembly, name, group in json_lines(BASELINES / "StateGraph.types.jsonl"))
    records, docs, all_docs, inherits = collect_lists(args.inputs, owners)
    errors = []
    reference = approved_ref(args.base, args.head, args.head_branch, args.base_branch) if args.mode == "api" else None
    for module, actual in records.items():
        baseline = BASELINES / (module + ".jsonl")
        if args.mode == "record":
            target = args.output / baseline.name
            write_lines(target, (actual[key] for key in sorted(actual)))
            print(f"{module}: proposed {len(actual)} records in {target}")
            continue
        if args.mode == "api":
            approved = unique((tuple(row[:2]), row) for row in json_lines(baseline))
            errors.extend(compare(module, approved, actual))
            if reference:
                relative = baseline.relative_to(BASELINES.parents[2]).as_posix()
                target_rows = subprocess.check_output(["git", "show", f"{reference}:{relative}"], text=True)
                target = unique((tuple(row[:2]), row) for row in map(json.loads, target_rows.splitlines()))
                errors.extend(compare(module + " target", target, approved))
        else:
            for identity in actual:
                if docs[identity] and (error := documentation_error(identity[1], all_docs, inherits)):
                    errors.append(f"{module}: {identity[0]} {identity[1]}: {error}")
        print(f"{module}: {len(actual)} API entries, {sum(docs[key] for key in actual)} documented methods")
    print("\n".join(errors))
    return bool(errors)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("api", "docs", "record"))
    parser.add_argument("inputs", type=Path)
    parser.add_argument("--base")
    parser.add_argument("--head")
    parser.add_argument("--head-branch", default="")
    parser.add_argument("--base-branch", default="")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    if args.mode == "record" and args.output is None:
        parser.error("record requires --output")
    if args.mode == "api" and not (args.base and args.head):
        parser.error("api requires --base and --head")
    try:
        raise SystemExit(check(args))
    except (OSError, ValueError, ET.ParseError, subprocess.CalledProcessError) as error:
        parser.exit(1, f"API input error: {error}\n")
