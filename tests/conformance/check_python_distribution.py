"""Verify the installed wheel without importing the source-tree fixture."""

from importlib.metadata import distribution
from importlib.resources import files

from inveniotech.gatekeeper.version import ADAPTER_VERSION


def main() -> None:
    """Check version, typing metadata, licence and absence of runtime dependencies."""
    installed = distribution("inveniotech-gate-keeper")
    assert installed.version == ADAPTER_VERSION
    assert not installed.requires
    assert files("inveniotech.gatekeeper").joinpath("py.typed").is_file()
    assert any(str(path).endswith("/licenses/LICENSE") for path in installed.files or ())
    print("Installed Python wheel metadata, typing and licence passed.")


if __name__ == "__main__":
    main()
