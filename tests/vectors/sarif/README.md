# Official SARIF schema

This is the unmodified official SARIF 2.1.0 JSON Schema from oasis-tcs/sarif-spec, commit 1742a253eb654bf8a4ed7c60eaef7412b2c23553. It is test data only; shipped code never downloads a schema. The schema retains its upstream notices and licence links. Its .schema extension distinguishes this external validation fixture from our own format contracts.

Regenerate it from the repository root with PowerShell:

    $content = gh api 'repos/oasis-tcs/sarif-spec/contents/sarif-2.1/schema/sarif-schema-2.1.0.json?ref=1742a253eb654bf8a4ed7c60eaef7412b2c23553' --jq .content
    [IO.File]::WriteAllBytes('tests/vectors/sarif/sarif-2.1.0.schema', [Convert]::FromBase64String(($content -join '')))

ResultsWriterTests validates emitted bytes against this schema and the narrower Gate-Keeper profile without network access.
