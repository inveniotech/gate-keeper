# Correction-release check

P2-08-02 (#101) implements the release comparison required by rule-pack-v1 section 9. `checkCorrection(base, correction)` requires the same pack identifier, schema and provenance, a pinned patch-zero base, the same numeric year/month line, the declared matching `baseVersion`, and a positive correction patch. Correction rule identifiers must be valid, unique and a subset of the base identifiers. Removing a rule is allowed; introducing a rule requires a new line. Version components are compared numerically, so month or patch text ordering cannot change the result.

```sh
node --test ci/pack-release/corrections/check.test.mjs
node ci/pack-release/corrections/check.mjs /path/to/pinned-base.json /path/to/correction.json
```

Use Node.js 26; only built-in modules are required. The command reads at most 16 MiB per document, prints metadata-only fixed diagnostics and returns 0 for acceptance or 1 for rejection. Supply immutable base bytes from the accepted line, not a moving branch. This comparison supplements schema validation and signature verification; it does not replace either or approve citations. Existing synthetic content provides complete acceptance tests without production keys or production rules. CI runs those tests in the existing pack-check job. The release producer can call the exported comparison before producing signing bytes.
