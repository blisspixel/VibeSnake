"""Exercise publisher recovery without GitHub credentials or network calls."""

import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

import pytest


ROOT = Path(__file__).resolve().parents[2]
SHA = "a" * 40
FAKE_GH = r"""
import json, os, pathlib, shutil, sys
root = pathlib.Path(os.environ["FAKE_RELEASE_ROOT"])
args = sys.argv[1:]
mode = os.environ.get("FAKE_RELEASE_MODE", "")
state_path = root / "state.json"
state = json.loads(state_path.read_text())
with (root / "calls").open("a") as log:
    log.write(json.dumps(args) + "\n")
def save():
    state_path.write_text(json.dumps(state))
if mode == "api" and args[0] == "api":
    sys.exit(1)
if args[0] == "api":
    route = next(a for a in args if a.startswith("repos/"))
    if route.endswith("git/refs"):
        if state["sha"]:
            sys.exit(1)
        state["sha"] = "a" * 40
        save()
    elif "/git/ref/" in route:
        print("commit\t" + state["sha"])
    elif "releases?" in route:
        if state["release"]:
            print(json.dumps(state["release"]))
    elif "/releases/tags/" in route:
        release = state["release"]
        print(str(not release["draft"] and release["immutable"]).lower())
    else:
        sys.exit(3)
elif args[:2] == ["release", "create"]:
    if state["release"]:
        sys.exit(1)
    state["release"] = {"tag_name":args[2], "draft":True, "prerelease":True, "immutable":False}
    save()
elif args[:2] == ["release", "upload"]:
    assert state["release"]["draft"]
    if mode == "upload":
        sys.exit(1)
    for source in args[3:args.index("--clobber")]:
        source = pathlib.Path(source)
        shutil.copyfile(source, root / "remote" / source.name)
elif args[:2] == ["release", "download"]:
    dest = pathlib.Path(args[args.index("--dir") + 1])
    for source in (root / "remote").iterdir():
        if mode != "missing" or source.name != "player.zip":
            shutil.copyfile(source, dest / source.name)
    if mode == "extra":
        (dest / "unexpected").write_text("extra")
    if mode == "corrupt":
        (dest / "player.zip").write_text("changed")
elif args[:2] == ["release", "edit"]:
    state["release"]["draft"] = False
    state["release"]["immutable"] = mode != "mutable"
    save()
else:
    sys.exit(3)
"""


@pytest.mark.skipif(os.name == "nt", reason="Publisher runs on hosted Linux only")
@pytest.mark.parametrize(
    "mode", ["fresh", "draft", "published", "conflict", "missing", "extra", "corrupt", "api", "upload", "mutable"]
)
def test_publisher_requires_exact_bytes_and_never_rewrites_published_assets(tmp_path: Path, mode: str) -> None:
    binaries = tmp_path / "bin"
    binaries.mkdir()
    fake = binaries / "gh"
    fake.write_text(f"#!{sys.executable}\n{FAKE_GH}", encoding="utf-8")
    fake.chmod(0o700)
    assets = tmp_path / "assets"
    assets.mkdir()
    (assets / "player.zip").write_bytes(b"qualified bytes")
    digest = hashlib.sha256(b"qualified bytes").hexdigest()
    (assets / "SHA256SUMS.txt").write_text(f"{digest}  player.zip\n", encoding="utf-8")
    notes = tmp_path / "notes"
    notes.write_text("qualified source", encoding="utf-8")
    remote = tmp_path / "remote"
    remote.mkdir()
    release = None
    if mode in {"draft", "published"}:
        release = {
            "tag_name": "player-123-1",
            "draft": mode == "draft",
            "prerelease": True,
            "immutable": mode == "published",
        }
        if mode == "published":
            for asset in assets.iterdir():
                (remote / asset.name).write_bytes(asset.read_bytes())
    state_path = tmp_path / "state.json"
    state_path.write_text(
        json.dumps({"sha": "b" * 40 if mode == "conflict" else SHA if release else None, "release": release}),
        encoding="utf-8",
    )
    environment = {
        **os.environ,
        "PATH": f"{binaries}{os.pathsep}{os.environ['PATH']}",
        "FAKE_RELEASE_ROOT": str(tmp_path),
        "FAKE_RELEASE_MODE": mode,
        "GITHUB_REPOSITORY": "fixture/repo",
        "GITHUB_RUN_ID": "123",
        "GITHUB_RUN_ATTEMPT": "1",
        "QUALIFIED_SHA": SHA,
    }
    result = subprocess.run(
        ["bash", str(ROOT / "scripts/publish_source_release.sh"), str(assets), str(notes)],
        env=environment,
        capture_output=True,
        text=True,
        timeout=20,
        check=False,
    )
    successful = mode in {"fresh", "draft", "published"}
    assert (result.returncode == 0) == successful, result.stdout + result.stderr
    state = json.loads(state_path.read_text(encoding="utf-8"))
    if successful:
        assert state["release"]["immutable"] and not state["release"]["draft"]
    elif mode == "mutable":
        assert not state["release"]["immutable"]
        assert not state["release"]["draft"]
        assert "Published verified immutable snapshot" not in result.stdout
    elif state["release"]:
        assert state["release"]["draft"]
    calls = [json.loads(line) for line in (tmp_path / "calls").read_text(encoding="utf-8").splitlines()]
    assert not any("DELETE" in call or "force=true" in call for call in calls)
    assert not any("immutable-releases" in arg for call in calls for arg in call)
    if mode == "published":
        assert not any(call[:2] in [["release", "upload"], ["release", "edit"]] for call in calls)
