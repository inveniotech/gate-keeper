# Validation corpus

P2-06 pins the immutable repository and licence selection in [repos.json](repos.json). The owner accepted that selection on 10 November 2026; each finding still needs owner triage once production detection rules exist. Every pin currently records triage as pending. No downloaded repository content belongs in this directory, and no empty record claims a clean scan.

The preparation tool and its tests are documented in [ci/validation-corpus](../../../ci/validation-corpus/README.md). It checks the existing corpus file-count and size bounds using the real engine enumerator and verifies exact licence bytes. The weekly workflow downloads snapshots independently of the zero-egress proof. A changed licence or an out-of-bounds checkout fails instead of changing the limits.

Once scans can run, owner triage goes under `records/` as one JSON list per repository. Entries carry only `ruleId`, forward-slash relative `path`, 1-based `line` and `column`, and `triage`. The label is `true-positive`, `false-positive` or `unresolved`; unresolved counts as false positive in the later measurement harness. Never commit matched values or source. False positives need synthetic golden-corpus cases before any rule change.
