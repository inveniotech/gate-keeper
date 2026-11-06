# Identifier rule acceptance

P2-03 (issue #30), card 01, records the corpus-first inputs for the five identifier cards. The owner approved the P2-02 citation register in principle on 5 November 2026, with legal review pending as recorded in [CITATIONS.md](CITATIONS.md). Production identifier rules remain to be implemented, and these fixtures do not claim detection precision or recall before a rule pack is measured.

The generated inputs cover labelled bank verification numbers, national identity numbers, NUBAN account numbers, Nigerian mobile numbers and email addresses in .NET, Node.js and JVM source. Each family also has a shape near-miss and an excluded or validator-rejected value. Every personal identifier is taken from `New-CorpusValues.mjs`, including the pinned sequential example and the documented wrong-check-digit account number. No additional real or invented identifier is copied into the corpus. The existing corpus supplies comments, fixtures, configuration, documented examples and further edge cases.

```sh
node tests/corpus/New-IdentifierCases.mjs --write
node --test ci/rule-packs/identifier-cases.test.mjs
dotnet test tests/Corpus.Tests -c Release
```

The generator owns only its named `p2-` case files and its marked README section. Do not hand-edit those outputs. The Node tests check the language/family matrix and exact regeneration; the existing corpus tests check expected-file structure and enumeration. They do not substitute for running production rules.

Implement the identifier cards separately using the approved-in-principle register: BVN, NIN and their shared low-confidence rule; NUBAN; phone; email. Keep labelled BVN/NIN and unsafe field handling at `error`, unlabelled eleven-digit values at `note`, and record both precision and recall. Confirm the CBN algorithm source and mobile-prefix data before the corresponding cards. Do not guess that a bare eleven-digit value distinguishes a BVN from a NIN. Record any missing matcher capability as an explicit decision instead of introducing detection logic in code. P2-04 supplies location-focused cases and rule data; P2-07 supplies the combined false-positive measurement. No case here changes either gate.
