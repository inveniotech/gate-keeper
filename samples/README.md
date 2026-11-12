# Sample applications

This folder holds one sample application per stack, each demonstrating the middleware in a real framework.

**Status: the .NET acceptance host lives here (card P3-02).** The remaining
stacks — and the fuller samples that add response filtering, consent and the
other integrations — still arrive in P3-07 (#44).

## `aspnetcore` — the .NET acceptance host

`samples/aspnetcore` is a minimal ASP.NET Core application built with
`CreateSlimBuilder`. It refuses to start without a signed pack: it reads the
signed pack's release media from one directory (`<pack>.pack.json`,
`<pack>.manifest.armour`, `<pack>.signature.hex`) and the installation's
public-only key catalogue from a separate file, then loads them with
`RuntimePacks`. A catalogue that carries private key material is refused.

The host is built but never listens. It writes two deterministic lines:

- `Compliance: …` — the compliance redactor answering
  `IRedactorProvider.GetRedactor` for the Gate-Keeper classification.
- the Serilog line — the enricher replacing the scalar string property before
  the sink renders it.

Both lines carry `[REDACTED:pii.bvn]` for the synthetic value the detection
corpus commits (`32826314843` next to a BVN label, because `pii.bvn` only
matches inside that window) and neither carries the digits. The signed pack is
never written into `rule-packs/`: the acceptance test signs the committed
`rule-packs/bvn.pack.json` in memory with the labelled `gkpk-dev-1` development
key.

```powershell
dotnet run --project samples/aspnetcore -- <pack-directory> <key-file>
```

Real installations embed their own signed packs and public keys; the development
keys are test-only material and must never sign a released pack.
