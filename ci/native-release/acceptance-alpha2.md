# Alpha.2 local verification — 16 November 2026

P4-01 (#46). These checks exercised the published `cli-v0.2.0-alpha.2` archives on an existing Windows 11 x64 development machine, build 26300, and Ubuntu 24.04.2 LTS x86_64 in Windows Subsystem for Linux (WSL) 2. They are preliminary local acceptance evidence, not fresh-machine or macOS acceptance.

Both archive SHA-256 values matched the downloaded `SHA256SUMS`. GitHub CLI verified repository attestations for both archives and for the checksum manifest.

| Check | Windows | Ubuntu WSL |
|---|---|---|
| Version and help | Exit 0; engine 0.2.0 | Exit 0; engine 0.2.0 |
| Synthetic positive scan | Exit 1; exactly one `local.egress.probe` finding in `probe.txt` | Same |
| Clean scan | Exit 0; no findings | Same |
| Repeated clean scan | Byte-identical evidence | Byte-identical evidence |
| Cross-platform evidence | Positive and clean evidence agree with Linux | Positive and clean evidence agree with Windows |
| Installer with development-only signature | Installed exact original executable bytes; version and clean scan passed | Same |

The Windows archive carried browser-origin metadata, which was preserved on the extracted executable for testing. Its Windows code-signing status was `NotSigned`. Smart App Control was off as reported by the owner; no security setting was changed, and enforcement-mode acceptance is not claimed. Windows operating-system metadata was obtained through the runtime because the sandbox denied the later system-management metadata lookup; the executable assertions had already passed.

Archive SHA-256 values:

- `gatekeeper-win-x64.zip`: `901ccac616df346bcbffed0be4a54c0d26d78dc4452547c7fee7e61bd476bb28`.
- `gatekeeper-linux-x64.tar.gz`: `86738f92ade3b194297977f5abd3d42be4e09686a5c657480f91a945ff22f699`.

The installer acceptance packages were separate local copies of the verified downloads. Their manifests were signed only with the public development seed from RFC 8032. No production key was created or used, and the original downloads were unchanged. These tests establish installer behaviour, not production release authenticity. The owner approved Node.js 26 or later for installation; the Ubuntu tests used a checksum-verified temporary Node.js 26 runtime without changing the machine's existing installation.

Outstanding: configure the production Ed25519 signer, distribute the independently trusted public key, publish a signed manifest, complete SignPath onboarding and Windows signing, and repeat the documented installation on fresh Windows, Linux and macOS environments. The current unsigned alpha is intentionally rejected by the verified installers.
