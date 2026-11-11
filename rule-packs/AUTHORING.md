# Rule authoring guide

This draft guide implements P2-01 (issue #28). A rule is data validated against [the content schema](../schemas/rulepack.schema.json) and executed by the existing matcher. Read [the rule-pack contract](../docs/specs/rule-pack-v1.md) for the exact supported shapes. Do not add detection logic to the engine to make one rule work.

## Start with the expected behaviour

Write a small positive case and a near-miss before writing a detection rule. Each case lives under `tests/corpus/<case-name>/`, with its input files in `repo/` and an `expected.json` alongside. An expected finding records `ruleId`, repository-relative `path`, 1-based `line` and `column`; a near-miss expects `[]`. Sort by path, line, column and rule identifier. Add the case to the corpus README. Use `node tests/corpus/New-CorpusValues.mjs` as the source of synthetic personal identifiers, following [the corpus instructions](../tests/corpus/README.md). Never copy a finding's matched value or customer source into a test.

Use stable dotted lowercase identifiers such as `pii.bvn.log`; describe a distinct detection purpose rather than a file extension. Unsigned local rules must use `local.` identifiers and cannot replace bundled rules. The template uses `local.authoring.marker`. Invenio packs use calendar versions; local packs use positive integers. Keep `sinceVersion` in the same form as the enclosing pack and declare the minimum engine version.

## Choose severity and citation deliberately

`error` is a high-confidence finding that meets the default build-failure threshold. `warning` asks for attention and `note` supplies context. IQ-12 assigns labelled BVN/NIN values and unsafe field handling to `error`, and unlabelled eleven-digit values surviving exclusions to `pii.ng-11-digit-id` at `note`. A shape alone cannot establish which identifier it is. Processor-catalogue findings use `note`. Record lower-severity counts separately; do not lower a rule's severity just to pass the false-positive gate.

Each production rule needs the exact approved citation from the P2-02 register. Its `citation` has a plain-language `title` and the applicable `ndpa` and `gaid` references. Authors may propose wording for review; they must not invent references, approve their own legal wording or describe a match as proof of a legal breach. Until the register is approved, production identifier rules remain pending. The synthetic template below makes no legal claim and is not a detection pack.

## Express matching in pack data

Use exactly one `matcher.pattern` or `matcher.list`. A pattern is a sequence of bounded shape parts, with the supported validator, boundary and exclusions where needed. At most one shape part sets `emit: true` to locate the reported span. `keywords` requires a bounded `window`; it is a nearby text condition, not an abstract-syntax or data-flow analysis. A list contains exact `package` or `hostname` tokens; test that a shorter token does not match inside a longer one.

`location.paths` narrows eligible paths using the documented glob subset. `location.kind` labels the resulting finding; it does not recognise a logging call or prove that a migration is unencrypted. Encode any extra context in the supported shape and keywords, with positive and near-miss cases for each launch stack. Keep profiles explicit when a rule is intended only for `deep`. `redaction.optional` defaults to false; do not change it as a way to hide a finding.

## Exercise a throwaway rule end to end

Create an empty directory outside this checkout, with `.gatekeeper/` inside it. Copy [templates/local.pack.json](templates/local.pack.json) to `.gatekeeper/local.json` there. Write this `.gatekeeper/gatekeeper.toml`:

```toml
schemaVersion = 1
[packs]
local = [".gatekeeper/local.json"]
```

Write `example.txt` containing `gatekeeper-authoring-probe` followed by a newline. Build the CLI from this checkout with `dotnet build cli -c Release -p:RestoreLockedMode=true`. From the throwaway directory, run `dotnet <absolute-checkout-path>/cli/bin/Release/net10.0/gatekeeper.dll scan --json --evaluated-on 2026-11-05`. Expect exit 1 and exactly one `local.authoring.marker` finding at `example.txt`, line 1, column 1. The result must not contain the marker's value. Replace the input with `prefix-gatekeeper-authoring-probe-suffix` and expect exit 0 with no findings. Do not copy this probe into a production pack.

The automated acceptance test performs both scans through the real loader, matcher, policy and results writer:

```sh
dotnet test tests/Corpus.Tests -c Release --filter FullyQualifiedName~RuleAuthoringTests
dotnet test tests/Corpus.Tests -c Release
dotnet test -c Release
```

The corpus report currently marks detection recall unmeasured because it has no production pack. P2-07 (#33) supplies the complete false-positive measurement command; do not treat today's empty report as measured precision. That command must measure every rule and the overall total on synthetic and pinned validation corpora, with no exceptions, baseline or disabled rules. The gate is `20 × false positives <= true positives + false positives` for `error` findings, per rule and overall; unresolved triage counts as false positive. Report `warning` and `note` findings separately and record recall on synthetic positives. See [the quality contract](../docs/specs/quality-budgets.md).

## Review before requesting approval

- Confirm that positive and near-miss tests failed before the rule and pass afterwards, with exact locations and no matched content in results.
- Validate the pack against the schema and load it through the real loader. A JSON syntax check alone is insufficient.
- Use the approved citation verbatim, check versions and severity, and record the measured precision and recall or explicitly state why they remain unmeasured.
- Run relevant corpus, conformance, glossary, spelling, zero-egress and native checks. A new matcher capability needs cross-stack vectors and owner-approved scope; a rule must not silently add one.
- Include the work package, card, requirement identifiers, known gaps and a Handoff section in the pull request. Keep the change small and explain how to fix the detected risk or raise a justified exception through the existing configuration contract.
