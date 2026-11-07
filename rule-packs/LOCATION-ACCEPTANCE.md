# Location rule acceptance

P2-04 (issue #31), card 01, supplies the unsigned `invenio.locations` source pack. Its rules use the existing structured shapes, keywords, validators and path scopes. No matcher capability or public schema changes. The [generated reference](../docs-data/rules/locations.md) lists each rule and its exact approved-in-principle citation; legal review remains pending.

## Severity and composition

All five identifier families use `error` in logs, fixtures, configuration, migrations and API models. Labelled BVN/NIN values retain the IQ-12 high-confidence severity; phone location rules retain the base phone severity. Account and email location findings request review of identifier storage or exposure in these contexts. BVN/NIN field handling without literal values also uses `error`, as IQ-12 specifies. These are source heuristics and do not establish assignment, execution, disclosure or a legal breach. Unlabelled eleven-digit detection remains a separate note-level rule outside this pack.

On 7 November 2026 the owner authorised keeping base and location findings together. For example, a labelled BVN in configuration produces `pii.bvn` and `pii.bvn.configuration` at the same value. The first records identifier detection; the second records location context. The existing matcher does not replace one with the other. Reports measure them separately and count findings, not unique identifiers. Their totals must not be described as counts of people, records or distinct risks. Where path scopes overlap, each applicable location rule can emit its own observation. No base rule is disabled, narrowed or silently deduplicated. NUBAN and email base rules are separate cards; their location detection runs independently here.

## Scopes and limits

Fixture scopes include `fixtures/`, `__tests__/`, `*.spec.*`, `seeds/` and `seed.*`. Configuration scopes include `config/`, `.env`, `.env.*`, `appsettings*.json`, `application*.yml`, `application*.yaml` and `*.properties`. Migration scopes include `migrations/` and `Migrations/`. Model scopes include `api/models/`, `api/responses/`, `Models/` and `Responses/`. All are pack-defined, case-sensitive repository-relative globs. A path supplies context, not proof of use.

Literal log rules require a recognised call within 128 scalar values. BVN, NIN and account log rules additionally require a label before the value, separated by at most 32 printable non-digit characters. The call can belong to another statement within that window. This trade-off supports existing multiline logging examples without inventing syntax analysis. Recognised call names and supported field forms are enumerated in the generated reference. Field rules match direct variable and property handling in the three launch stacks; masked expressions, unrelated field names and separate statements have negative cases. Formatting, aliases, indirect calls and other receiver spellings are not exhaustive.

Account location rules use the existing validator's bank-code-011 baseline and exclude documented account examples. Other bank codes remain outside validated coverage; this pack does not claim general bank-code validation. Phone rules reuse the base pack's national and international prefix data and exclusions. Email uses bounded ASCII mailbox parts and the existing documentation-domain validator, not full mailbox syntax. Neither a migration nor a plain string column establishes deployment encryption. Response models alone cannot prove that a response was sent or lacked authorisation.

## Reproduction and acceptance

The generator reuses the documented synthetic values and shared case writer. It supplies 35 literal positives including both international phone spellings, 24 field positives across .NET, Node.js and JVM, and 67 near-misses. Existing golden cases also carry explicit location expectations. Base and location observations have separate expected entries; no measured finding is used to invent its own expected result.

```sh
node tests/corpus/New-LocationCases.mjs --write
node ci/rule-packs/build-detection.mjs --write
node --test ci/rule-packs/*.test.mjs
dotnet test tests/Corpus.Tests -c Release
```

`DetectionPackTests` passes the unsigned source bytes through the real signed loader using only the labelled in-memory development key. It checks the five-family/five-location matrix, exact locations, severities and inventory kinds, path aliases, log-call spellings and unsafe field forms. Every authored pack also scans every synthetic repository with exact expected findings and the unchanged error-level false-positive gate. The combined [synthetic measurement report](../docs-data/synthetic-measurements.md) records per-rule precision and recall; it does not establish precision on real repositories. Regenerate that report with `GATEKEEPER_WRITE_SYNTHETIC_REPORT=1 dotnet test tests/Corpus.Tests -c Release --filter FullyQualifiedName~DetectionReport_Should_MatchTheCommittedReport`.

Source packs remain unsigned release inputs. Production signing, embedding and publication are separate release operations.
