# Support-bundle privacy checks

P1-13 uses the documented corpus generator as the only source for the generated identifier inventory. `node --test ci/privacy/inventory.test.mjs` checks that the committed inventory is exactly reproducible. Regenerate it with:

```sh
node tests/corpus/New-CorpusValues.mjs --values-json > tests/corpus/synthetic-values.json
```

The .NET acceptance test `CorpusSupportBundleTests` scans every corpus case with the real command, opens each generated ZIP entry and asserts that none of those synthetic identifiers appears. The test also injects identifiers into configuration fields, checks that keys are absent and requires a live synthetic finding. Run it with `dotnet test tests/Corpus.Tests --filter FullyQualifiedName~CorpusSupportBundleTests`.
