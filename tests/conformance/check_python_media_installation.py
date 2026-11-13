"""Build staged development-only signed media and verify an installed wheel offline."""

from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import tempfile
import zipfile

root = Path(__file__).resolve().parents[2]
source = root / "middleware/python"
fixture = root / "tests/vectors/pack"
with tempfile.TemporaryDirectory(prefix="gatekeeper-python-stage-") as scratch:
    temporary = Path(scratch)
    stage = temporary / "package"
    shutil.copytree(
        source,
        stage,
        ignore=shutil.ignore_patterns(
            "dist", "*.egg-info", "__pycache__", ".mypy_cache", ".ruff_cache"
        ),
    )
    embedded = stage / "src/inveniotech/gatekeeper/embedded"
    embedded.mkdir()
    for target, original in [
        ("free.json", "pack.pack.json"),
        ("free.manifest.armour", "pack.manifest.armour"),
        ("free.signature.hex", "pack.signature.hex"),
    ]:
        shutil.copyfile(fixture / original, embedded / target)
    (embedded / "unrelated.txt").write_text("not signed media")
    build = subprocess.run(
        [sys.executable, "-m", "build", "--no-isolation", "--sdist", "--wheel"],
        cwd=stage,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
    )
    if build.returncode:
        raise RuntimeError(build.stdout)
    artefacts = sorted((stage / "dist").iterdir())
    wheel = next(item for item in artefacts if item.suffix == ".whl")
    sdist = next(item for item in artefacts if item.suffix == ".gz")
    names = ["free.json", "free.manifest.armour", "free.signature.hex"]
    with zipfile.ZipFile(wheel) as archive:
        included = archive.namelist()
        assert not any(name.endswith("/embedded/unrelated.txt") for name in included)
        assert all(
            any(name.endswith("/embedded/" + part) for name in included)
            for part in names
        )
    with tarfile.open(sdist) as archive:
        included = archive.getnames()
        assert not any(name.endswith("/embedded/unrelated.txt") for name in included)
        assert all(
            any(name.endswith("/embedded/" + part) for name in included)
            for part in names
        )
    check = subprocess.run(
        [sys.executable, "-m", "twine", "check", "--strict", str(wheel), str(sdist)],
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
    )
    if check.returncode:
        raise RuntimeError(check.stdout)
    target = temporary / "installed"
    install = subprocess.run(
        [
            sys.executable,
            "-m",
            "pip",
            "install",
            "--no-index",
            "--no-deps",
            "--target",
            str(target),
            str(wheel),
        ],
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
    )
    if install.returncode:
        raise RuntimeError(install.stdout)
    probe = r"""
import json,sys
from pathlib import Path
from importlib.metadata import version
sys.path.insert(0,sys.argv[1])
from inveniotech.gatekeeper import Installation
fixture=Path(sys.argv[2])
keys={item['keyId']:bytes.fromhex(item['publicKeyHex']) for item in json.loads((fixture/'development-public-key.json').read_text())['keys']}
runtime=Installation.from_package(keys).runtime
assert runtime.versions()['packs'][0]['id']=='invenio.ng.example.p1'
assert runtime.redact('12345678901')=='[REDACTED:pii.bvn]'
assert version('inveniotech-gate-keeper')=='0.1.0'
"""
    run = subprocess.run(
        [sys.executable, "-I", "-c", probe, str(target), str(fixture)],
        cwd=temporary,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
    )
    if run.returncode:
        raise RuntimeError(run.stdout)
    print(
        "Staged sdist and wheel include signed media, exclude unrelated files, and pass strict metadata, isolated identity and redaction checks."
    )
