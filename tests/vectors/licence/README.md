# Licence-token vectors

Public vectors for the licence-token specification, version 1. They are signed with the development key described in that specification. The key is for these vectors only. It is not a production key, and the customer numbers in the tokens are synthetic.

Regenerate the files from the repository root:

```powershell
node tests/vectors/licence/New-LicenceVectors.mjs
```

The test project checks the committed files. A trailing CR on an armour line is covered by a test that rewrites line endings in memory, so this folder does not store a second copy of the same token.
