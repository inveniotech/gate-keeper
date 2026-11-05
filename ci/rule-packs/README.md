# Rule-pack authoring checks

P2-02 provides an approval-aware lookup for the draft citation register. `approvedCitation(register, category)` returns a copy of the approved citation only when the register, category and review metadata permit use. A draft, unknown or duplicate category, missing wording or absent review metadata throws an error. This is an authoring helper, not a substitute for human or legal review and not a product runtime dependency.

Run `node --test ci/rule-packs/citations.test.mjs` with Node.js 26. Tests use synthetic approval metadata to exercise the lookup; they never approve the committed register. The [review page](../../rule-packs/CITATIONS.md) explains the official source pins and approval process. The register is not itself a rule pack.

P2-03 adds `node --test ci/rule-packs/identifier-cases.test.mjs` for the corpus-first identifier matrix and byte-exact regeneration. Its generated inputs and pending production acceptance are documented in [IDENTIFIER-ACCEPTANCE.md](../../rule-packs/IDENTIFIER-ACCEPTANCE.md).
