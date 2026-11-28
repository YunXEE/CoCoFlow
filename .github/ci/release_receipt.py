"""Bind successful release PR CI to the actual master tree and its version tag."""

import argparse
import json
import os
import re
import subprocess
import tempfile
from pathlib import Path

from cocoflow_ci import release_errors
from collect_api_surface import git


WORKFLOW = "ci-static.yml"
RELEASE_JOBS = {"static", "api", "api-doc", "win-mono", "win-il2cpp", "gate"}


def candidate():
    return {"sha": git("rev-parse", "HEAD"), "tree": git("rev-parse", "HEAD^{tree}")}


def write(path, data):
    path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")


def issue(players, path):
    tested = candidate()
    for backend in ("mono", "il2cpp"):
        identity = players / f"release-player-{backend}-{tested['sha']}-{os.environ['GITHUB_RUN_ATTEMPT']}" / "candidate.json"
        if json.loads(identity.read_text(encoding="utf-8")) != {**tested, "backend": backend}:
            raise ValueError(f"{backend} did not test the exact release candidate")
    pr = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text(encoding="utf-8"))["pull_request"]
    write(path, {
        **tested, "pr": pr["number"], "base": pr["base"]["sha"], "head": pr["head"]["sha"],
        "run": int(os.environ["GITHUB_RUN_ID"]), "attempt": int(os.environ["GITHUB_RUN_ATTEMPT"]),
    })


def api(path):
    return json.loads(subprocess.check_output(["gh", "api", path], text=True))


def matches(receipt, pull, run, merged):
    return receipt == {
        "pr": pull["number"], "head": pull["head"]["sha"], "base": merged["base"],
        "sha": receipt.get("sha"), "tree": merged["tree"],
        "run": run["id"], "attempt": run["run_attempt"],
    } and bool(re.fullmatch(r"[0-9a-f]{40}", receipt.get("sha", "")))


def verified_release(repo, merged):
    event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text(encoding="utf-8"))
    merged["base"] = event["before"]
    associated = api(f"repos/{repo}/commits/{merged['sha']}/pulls")
    matched = [
        candidate for candidate in associated
        if candidate["base"]["ref"] == "master" and candidate["merge_commit_sha"] == merged["sha"]
    ]
    if len(matched) != 1:
        raise ValueError("No unique release PR is associated with the actual master SHA")
    pr = api(f"repos/{repo}/pulls/{matched[0]['number']}")
    if (not pr["merged"] or pr["head"]["repo"]["full_name"] != repo
            or not pr["head"]["ref"].startswith("dev/")):
        raise ValueError("The actual master SHA did not merge the expected same-repository dev/* PR")

    head = pr["head"]["sha"]
    runs = api(f"repos/{repo}/actions/workflows/{WORKFLOW}/runs?event=pull_request&head_sha={head}&status=success&per_page=100")
    for run in runs["workflow_runs"]:
        run_id, attempt = run["id"], run["run_attempt"]
        if run["head_repository"]["full_name"] != repo or run["head_branch"] != pr["head"]["ref"]:
            continue
        jobs = api(f"repos/{repo}/actions/runs/{run_id}/attempts/{attempt}/jobs?per_page=100")["jobs"]
        if not RELEASE_JOBS <= {job["name"] for job in jobs if job["conclusion"] == "success"}:
            continue
        name = f"release-receipt-{attempt}"
        artifacts = api(f"repos/{repo}/actions/runs/{run_id}/artifacts?name={name}")["artifacts"]
        if len(artifacts) != 1 or artifacts[0]["expired"]:
            continue
        with tempfile.TemporaryDirectory(prefix="cocoflow-receipt-") as directory:
            subprocess.run(["gh", "run", "download", str(run_id), "--repo", repo, "--name", name,
                            "--dir", directory], check=True)
            receipt = json.loads((Path(directory) / "release-receipt.json").read_text(encoding="utf-8"))
        if not matches(receipt, pr, run, merged):
            continue
        tested = api(f"repos/{repo}/git/commits/{receipt['sha']}")
        if tested["tree"]["sha"] == merged["tree"] and [p["sha"] for p in tested["parents"]] == [merged["base"], head]:
            print(json.dumps(receipt, indent=2))
            return
    raise ValueError("No successful release CI receipt matches this merged PR, run attempt and tree")


def tag(repo, tested):
    checks = api(f"repos/{repo}/commits/{tested['sha']}/check-runs?check_name=gate")["check_runs"]
    if not any(check["app"]["id"] == 15368 and check["status"] == "completed"
               and check["conclusion"] == "success" and check["details_url"].split("?")[0].startswith(
                   f"https://github.com/{repo}/actions/runs/{os.environ['GITHUB_RUN_ID']}/")
               for check in checks):
        raise ValueError("No successful gate for this master SHA and workflow run")
    version = json.loads(Path("package.json").read_text(encoding="utf-8"))["version"]
    name = "v" + version
    event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text(encoding="utf-8"))
    previous = json.loads(git("show", event["before"] + ":package.json"))["version"]
    if errors := release_errors(Path.cwd(), previous=previous, tag=name):
        raise ValueError("\n".join(errors))
    ref = f"refs/tags/{name}"

    def remote_target():
        remote = git("ls-remote", "origin", ref, ref + "^{}").splitlines()
        refs = dict(line.split("\t")[::-1] for line in remote)
        return refs.get(ref + "^{}", refs.get(ref))

    target = remote_target()
    if target is None:
        subprocess.run(["git", "push", "origin", f"{tested['sha']}:{ref}"], check=False)
        target = remote_target()
    if target != tested["sha"]:
        raise ValueError(f"Remote {name} must target {tested['sha']}, observed {target}")
    print(f"Remote {name} targets {target}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    record = commands.add_parser("record-player")
    record.add_argument("backend", choices=("mono", "il2cpp"))
    record.add_argument("output", type=Path)
    receipt = commands.add_parser("issue")
    receipt.add_argument("players", type=Path)
    receipt.add_argument("output", type=Path)
    commands.add_parser("verify")
    commands.add_parser("tag")
    args = parser.parse_args()
    try:
        if args.command == "record-player":
            write(args.output, {**candidate(), "backend": args.backend})
        elif args.command == "issue":
            issue(args.players, args.output)
        elif args.command == "verify":
            verified_release(os.environ["GITHUB_REPOSITORY"], candidate())
        else:
            tag(os.environ["GITHUB_REPOSITORY"], candidate())
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        parser.exit(1, f"Release receipt error: {error}\n")
