# Repository dogfood integration

P1-17 (issue #27) runs the existing command-line interface against a repository's own work tree. It uses its normal configuration, exceptions and severity policy. It does not add detection rules or change the scanner. Node.js 26 and its built-in modules run the harness; no dependency is added.

## Current acceptance status

Detection acceptance is pending until an approved detection pack is supplied. The owner approved preparing this integration before the Phase 2 bundled pack exists. A synthetic probe cannot establish personal-data coverage. The workflow explicitly reports that no scan ran when `.ndpa-check/dogfood.pack.json` is absent. Its successful readiness check is not evidence of a clean scan.

Supply the approved unsigned local pack at that path, using `local.` rule identifiers and the rule-pack schema. The committed configuration references it and leaves history off. Do not use a probe pack as the detection pack. Pin a published CLI release tag in `GATEKEEPER_DOGFOOD_VERSION` and its Linux x64 archive SHA-256 digest in `GATEKEEPER_DOGFOOD_SHA256`, or pass the corresponding workflow inputs. No CLI release is published yet. The workflow verifies the pinned digest before extraction and adds no scanner build. It then runs scans automatically on source pull requests and master pushes. Manual runs require an explicit evaluation date; other runs use the checked-out commit's civil date, which is reproducible and does not claim to evaluate exceptions as of today.

## Reuse and local execution

Other repositories can call `.github/workflows/dogfood.yml` using a full commit identifier of this repository, with `scanner-ref` set to the same identifier for the integration scripts and schemas. The scanner itself comes from the separately pinned published release. The workflow checks out the caller's repository and executes locally. The caller needs the same `.ndpa-check/ndpa-check.toml`, its approved pack and the release pins. Do not put private repository names or findings into this public module.

```sh
node --test ci/dogfood/scan.test.mjs
dotnet build cli -c Release -p:RestoreLockedMode=true
node --test ci/dogfood/scan.integration.test.mjs
node ci/dogfood/scan.mjs cli/bin/Release/net10.0/gatekeeper-ndpa-check.dll /path/to/repository 2026-11-05 /path/to/new-output
```

The runner also accepts a published native executable. Output must be a new directory outside the scanned repository. Two scans must have byte-identical evidence, diagnostics and exit codes. A tool error, malformed evidence, no loaded packs, history scanning or a mismatched evaluation date fails. Exit codes retain the CLI contract: 0 pass, 1 violations, 2 tool error. Findings fail the scan job but valid evidence is still retained for review. Timeouts bound each invocation to 30 seconds; output is bounded to 16 MiB. Diagnostics are compared in memory and are never printed or uploaded. Console output carries counts only. The workflow validates evidence against the existing schema before uploading it for seven days, in the repository where the workflow ran.

## Review and false positives

The owner reviews every finding using the evidence rule identifier and location, inspecting source locally. Record each disposition privately where appropriate. Every confirmed false positive needs a small synthetic positive or near-miss case under `tests/corpus/`, generated using its documented script, before a detection change. Do not copy source, identifiers or matched values from the scanned repository into a case or review comment. Do not blanket-baseline findings or disable rules to obtain a green check. A clean work-tree scan does not establish that Git history was scanned. The three-repository finding review remains open until approved-pack runs and their dispositions exist.
