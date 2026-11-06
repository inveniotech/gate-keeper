# Citation register: owner approval in principle

The owner approved the current [machine-readable register](citations.json), including its mappings, wording and detection limits, **in principle on 5 November 2026**. This authorises use of the current citations in rule implementation. Legal counsel's review remains pending under IQ-57; this is not a record of completed legal review or final legal approval. The owner will introduce any changes counsel recommends. The mappings explain why a detection signal merits review; they do not classify every identifier as sensitive personal data or establish a legal breach.

## Primary sources and verification

The register pins the SHA-256 of the official [Nigeria Data Protection Act 2023](https://ndpc.gov.ng/wp-content/uploads/2024/03/Nigeria_Data_Protection_Act_2023.pdf) and the [20 March 2025 General Application and Implementation Directive](https://ndpc.gov.ng/wp-content/uploads/2025/03/NDP-ACT-GAID-2025-MARCH-20TH.pdf). Both PDFs were downloaded from the Nigeria Data Protection Commission and their relevant provisions checked against extracted text on 5 November 2026. Hashes identify the reviewed source bytes; they do not establish that no later amendment exists. The superseded 2024 draft directive is not a source for these mappings. In particular, security-system monitoring is Article 29 in the 2025 text, not Article 30 from the earlier draft.

The proposed mappings are interpretations for review. Sections 24 and 39 supply the minimisation and security context; section 65 supplies the personal-data definition. They do not name our rule identifiers or impose a blanket prohibition on identifiers in code. Article 29 supports security review. The migration wording deliberately avoids claiming that a column declaration proves missing encryption. The processor entry separately maps engagement safeguards to section 29 and Article 34, and transfers to sections 41–43, Article 45 and Schedule 5. Consent remains context-dependent; the reserved rule cannot infer consent records from a missing source pattern.

## Category mapping

| Category | Candidate NDPA references | Candidate GAID references |
|---|---|---|
| `pii.bvn`, `pii.nin`, `pii.account`, `pii.phone`, `pii.email`, `pii.ng-11-digit-id` | 24(1)(c), 24(1)(f), 39, 65 | 29(1)–(2) |
| `pii.in-logs` | 24(1)(b)–(d), 24(1)(f), 39 | 29(1)–(2) |
| `pii.in-fixtures`, `pii.in-api-models` | 24(1)(b)–(c), 24(1)(f), 39 | 29(2)(h) |
| `pii.in-configuration` | 24(1)(c), 24(1)(f), 39 | 29(1)–(2) |
| `pii.in-migrations` | 24(1)(f), 39(1)–(2) | 29(2)(g)–(h) |
| `transfer.processor` | 29, 41–43 | 34, 45, Schedule 5 |
| `consent.missing` (reserved, note only) | 25, 26 | 17, 18 |

Each JSON entry holds its proposed result title, one-sentence explanation and a limit on what the detection can establish. The `pii.nuban` rule family maps to the `pii.account` register category. Secret scanning is outside the MVP; there is no placeholder citation for it. This file is a review aid, not a new rule-pack format. The registry itself is metadata and must not be assembled as a content pack.

## Review and use

The recorded status is `approved-in-principle`, with `reviewedBy: owner`, the approval date and `legalReview.status: pending`. It records the owner's explicit instruction to proceed with the current citations while counsel reviews them. Automated tests do not supply approval. A later completed review and any corrections must be recorded accurately; a new or materially changed mapping needs fresh owner review. The distinct `approved` status is reserved for a later recorded final approval.

`approvedCitation` in `ci/rule-packs/citations.mjs` recognises the owner's approval in principle only when both register and category carry that status, reviewer/date metadata exists and legal review is explicitly pending. It also supports recorded final approval. Draft or unknown categories and missing review metadata still fail. Rule generation must use that lookup; draft entries cannot be copied into shipping rules. Run `node --test ci/rule-packs/citations.test.mjs` to check completeness, source pins and both approval paths. The module uses only Node.js 26 built-ins and changes no product dependency.
