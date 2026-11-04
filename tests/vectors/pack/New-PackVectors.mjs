// Regenerates the public rule-pack loader vectors from the development seeds.
// The seeds are test keys. They must never sign a released rule pack.
// Usage, from the repository root: node tests/vectors/pack/New-PackVectors.mjs
//
// The pack loader (P1-03) receives the specification's byte sequences directly:
// the content bytes, the manifest armour with its signature over the payload
// line, and an optional detached watermark. These vectors feed one set of
// sequences per scenario, so every content-level check runs on bytes whose
// signature verified, exactly as a real pack arrives at the loader.
//
// Every object is written with its keys sorted; content files end with a
// newline and use LF endings (the producer profile, rule-pack-v1 section 6).

import { createHash, createPrivateKey, createPublicKey, sign } from "node:crypto";
import { writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const directory = dirname(fileURLToPath(import.meta.url));

// Development pack-signing key. Clearly labelled as development-only (rule 14).
const packKeyId = "gkpk-dev-1";
const packSeedHex = "a3e21c4d09f8b7e6560d1a2f3c4b5d6e7f8091a2b3c4d5e6f708192a3b4c5d6e";

// Development watermark key: the spec checks the detached block with a
// different embedded public key to the pack-signing key (section 2.3).
const watermarkKeyId = "gkwm-dev-1";
const watermarkSeedHex = "4c8f2d31a7b09e1665c4d3f2e1a098b7c6d5e4f3a2b1c09d8e7f6a5b4c3d2e1f";

function keyFromSeed(seedHex) {
  const pkcs8 = Buffer.concat([
    Buffer.from("302e020100300506032b657004220420", "hex"),
    Buffer.from(seedHex, "hex"),
  ]);
  return createPrivateKey({ key: pkcs8, format: "der", type: "pkcs8" });
}

function publicKeyHex(privateKey) {
  const spki = createPublicKey(privateKey).export({ type: "spki", format: "der" });
  return spki.subarray(spki.length - 32).toString("hex");
}

const packPrivateKey = keyFromSeed(packSeedHex);
const watermarkPrivateKey = keyFromSeed(watermarkSeedHex);

// Digests over the exact content bytes; two algorithms over one byte sequence
// keep the two hashes tied together (section 2.2).
function contentDigests(content) {
  return {
    sha256: createHash("sha256").update(content).digest("hex"),
    sha512: createHash("sha512").update(content).digest("hex"),
  };
}

// The manifest payload line in exactly the field order the spec fixes.
function manifestLine(keyId, packId, version, digests) {
  return `{"keyId":"${keyId}","packId":"${packId}","version":"${version}",` +
    `"contentHashes":{"sha256":"${digests.sha256}","sha512":"${digests.sha512}"}}`;
}

function manifestArmour(headerKeyId, payloadLine) {
  return [
    "-----BEGIN GATE-KEEPER RULE PACK MANIFEST-----",
    `Key-Id: ${headerKeyId}`,
    payloadLine,
    "-----END GATE-KEEPER RULE PACK MANIFEST-----",
    "",
  ].join("\n");
}

// The watermark payload line in exactly the field order the spec fixes
// (section 2.3). When `mismatch` is true the copy would not match the digest.
function watermarkLine(keyId, digest, mismatch) {
  const recordedDigest = mismatch ? "f".repeat(64) : digest;
  return `{"keyId":"${keyId}","contentDigest":"${recordedDigest}",` +
    `"organisationName":"ORGANISATION-EXAMPLE-ONE","licenceId":"LIC-0000001","generatedAt":"2027-01-01T00:00:00Z"}`;
}

function watermarkArmour(headerKeyId, payloadLine) {
  return [
    "-----BEGIN GATE-KEEPER RULE PACK WATERMARK-----",
    `Key-Id: ${headerKeyId}`,
    payloadLine,
    "-----END GATE-KEEPER RULE PACK WATERMARK-----",
    "",
  ].join("\n");
}

function signatureHex(payloadLine, privateKey) {
  return sign(null, Buffer.from(payloadLine, "utf8"), privateKey).toString("hex");
}

// One content document: keys of every object are sorted; `rules` keeps the
// document order the content lists them in (section 3).
function packContent(properties, rules, schemaVersion = 1) {
  return JSON.stringify(
    {
      schemaVersion,
      id: properties.id,
      version: properties.version,
      provenance: properties.provenance,
      minEngineVersion: properties.minEngineVersion,
      ...(properties.provenance !== "local"
        ? {
            coverageDate: properties.coverageDate,
            contentIssuedAt: properties.contentIssuedAt,
            ...(properties.baseVersion ? { baseVersion: properties.baseVersion } : {}),
          }
        : {}),
      rules,
    },
    null,
    2,
  ) + "\n";
}

// JSON.stringify writes each object's keys in insertion order, so every
// literal below is written sorted: category, citation, id, matcher,
// sinceVersion, severity — with matcher pattern keys boundary, shape,
// validator and shape part keys class, emit, max, min sorted inside.
function digitRule(id, severity, sinceVersion, category) {
  return {
    category,
    citation: { title: "Placeholder citation — approved NDPA wording arrives in P2-02" },
    id,
    matcher: {
      pattern: {
        boundary: "digit",
        shape: [{ class: "digit", emit: true, max: 11, min: 11 }],
        validator: { length: 11, name: "digits-length" },
      },
    },
    sinceVersion,
    severity,
  };
}

const baseVersion = "2027.1.0";
const nextVersion = "2027.2.0";
const bundledProperties = {
  id: "invenio.ng.example.p1",
  provenance: "bundled",
  minEngineVersion: "0.1.0",
  coverageDate: "2027-01-01",
  contentIssuedAt: "2027-01-01",
};

// The valid bundled pack that must load.
const baseContent = packContent(
  { ...bundledProperties, version: baseVersion },
  [digitRule("pii.bvn", "error", baseVersion, "bvn")],
);

// A newer version of the same pack: the 2027.2.0 line adds pii.nin, so it wins
// as a whole when both copies load (IQ-24).
const nextLineContent = packContent(
  {
    ...bundledProperties,
    version: nextVersion,
    coverageDate: "2027-02-01",
    contentIssuedAt: "2027-02-01",
  },
  [digitRule("pii.bvn", "error", nextVersion, "bvn"), digitRule("pii.nin", "error", nextVersion, "nin")],
);

// A copy with the same pack id and version but different content: two copies
// of one release share one content digest, so this is a conflict.
const conflictContent = packContent(
  { ...bundledProperties, version: baseVersion },
  [digitRule("pii.bvn", "error", baseVersion, "bvn"), digitRule("pii.nin", "error", baseVersion, "nin")],
);

// A different pack whose rules carry an identifier the first pack owns: a load
// error under IQ-24.
const rivalContent = packContent(
  { ...bundledProperties, id: "invenio.ng.example.rival", version: baseVersion },
  [digitRule("pii.bvn", "error", baseVersion, "bvn")],
);

// A content document of a schema version this build does not read.
const schemaMajorContent = packContent(
  { ...bundledProperties, version: baseVersion },
  [digitRule("pii.bvn", "error", baseVersion, "bvn")],
  2,
);

// A pack whose minimum engine version is newer than the engine.
const newerEngineContent = packContent(
  { ...bundledProperties, version: baseVersion, minEngineVersion: "100.0.0" },
  [digitRule("pii.bvn", "error", baseVersion, "bvn")],
);

// A plan-gated pack: admissible only by a Phase 5 entitlement policy, and the
// MVP 'bundled packs only' policy refuses it (IQ-10).
const planGatedContent = packContent(
  { ...bundledProperties, id: "invenio.ng.example.gated", version: baseVersion, provenance: "plan-gated" },
  [digitRule("pii.bvn", "error", baseVersion, "bvn")],
);

// A pack whose coverage date is after the issue date: an inconsistent line.
const coverageAfterIssuedContent = packContent(
  {
    ...bundledProperties,
    version: "2027.3.1",
    coverageDate: "2027-03-01",
    contentIssuedAt: "2027-02-01",
    baseVersion: "2027.3.0",
  },
  [digitRule("pii.bvn", "error", "2027.3.1", "bvn")],
);

// A correction whose baseVersion does not share its line's year and month.
const baseVersionWrongContent = packContent(
  {
    ...bundledProperties,
    version: "2027.3.1",
    coverageDate: "2027-02-01",
    contentIssuedAt: "2027-02-15",
    baseVersion: "2027.1.0",
  },
  [digitRule("pii.bvn", "error", "2027.3.1", "bvn")],
);

// A bundled pack that carries a rule identifier starting with 'local.': only
// local packs may carry those identifiers (IQ-25).
const localRuleInBundledContent = packContent(
  { ...bundledProperties, version: baseVersion },
  [digitRule("pii.bvn", "error", baseVersion, "bvn"), digitRule("local.synthetic-code", "note", baseVersion, "synthetic-code")],
);

// The content does not agree with the manifest: one byte of the signed content
// was changed after signing, so the digests no longer match any source.
const tamperedContent = baseContent.replace("2027.1.0", "2027.1.9");

const contents = {
  pack: baseContent,
  "next-line": nextLineContent,
  conflict: conflictContent,
  rival: rivalContent,
  "schema-major": schemaMajorContent,
  "newer-engine": newerEngineContent,
  "plan-gated": planGatedContent,
  "coverage-after-issued": coverageAfterIssuedContent,
  "base-version-wrong": baseVersionWrongContent,
  "local-rule-in-bundled": localRuleInBundledContent,
};

// The signed scenarios: each stem names a content above; the manifest
// repeats the content's id and version over its digests.
const scenarios = [
  { stem: "pack", version: baseVersion, packId: "invenio.ng.example.p1" },
  { stem: "next-line", version: nextVersion, packId: "invenio.ng.example.p1" },
  { stem: "conflict", version: baseVersion, packId: "invenio.ng.example.p1" },
  { stem: "rival", version: baseVersion, packId: "invenio.ng.example.rival" },
  { stem: "schema-major", version: baseVersion, packId: "invenio.ng.example.p1" },
  { stem: "newer-engine", version: baseVersion, packId: "invenio.ng.example.p1" },
  { stem: "plan-gated", version: baseVersion, packId: "invenio.ng.example.gated" },
  { stem: "coverage-after-issued", version: "2027.3.1", packId: "invenio.ng.example.p1" },
  { stem: "base-version-wrong", version: "2027.3.1", packId: "invenio.ng.example.p1" },
  { stem: "local-rule-in-bundled", version: baseVersion, packId: "invenio.ng.example.p1" },
];

for (const scenario of scenarios) {
  const content = contents[scenario.stem];
  const digests = contentDigests(Buffer.from(content, "utf8"));
  const payloadLine = manifestLine(packKeyId, scenario.packId, scenario.version, digests);
  writeFileSync(join(directory, `${scenario.stem}.pack.json`), content);
  writeFileSync(join(directory, `${scenario.stem}.manifest.armour`), manifestArmour(packKeyId, payloadLine));
  writeFileSync(join(directory, `${scenario.stem}.signature.hex`), signatureHex(payloadLine, packPrivateKey) + "\n");
}

// The tampered copies derive from the base scenario without a re-sign, so the
// bytes the loader receives cannot be traced back to the digests it verifies.
const baseDigests = contentDigests(Buffer.from(baseContent, "utf8"));
const basePayloadLine = manifestLine(packKeyId, "invenio.ng.example.p1", baseVersion, baseDigests);
const baseSignature = signatureHex(basePayloadLine, packPrivateKey);
writeFileSync(join(directory, "tampered-content.pack.json"), tamperedContent);
writeFileSync(join(directory, "tampered-content.manifest.armour"), manifestArmour(packKeyId, basePayloadLine));
writeFileSync(join(directory, "tampered-content.signature.hex"), baseSignature + "\n");
writeFileSync(join(directory, "tampered-signature.pack.json"), baseContent);
writeFileSync(join(directory, "tampered-signature.manifest.armour"), manifestArmour(packKeyId, basePayloadLine));
writeFileSync(
  join(directory, "tampered-signature.signature.hex"),
  (baseSignature[0] === "0" ? "1" : "0") + baseSignature.slice(1) + "\n",
);

// An unknown keyId: the catalogue has no key 'gkpk-unknown', so the loader
// refuses before it verifies, whatever the signature bytes are.
const unknownPayloadLine = manifestLine(
  "gkpk-unknown",
  "invenio.ng.example.p1",
  baseVersion,
  baseDigests,
);
writeFileSync(join(directory, "unknown-key.manifest.armour"), manifestArmour("gkpk-unknown", unknownPayloadLine));
writeFileSync(join(directory, "unknown-key.signature.hex"), signatureHex(unknownPayloadLine, packPrivateKey) + "\n");

// A key-id mismatch: the armour header names one key and the payload another.
const mismatchPayloadLine = manifestLine("gkpk-other", "invenio.ng.example.p1", baseVersion, baseDigests);
writeFileSync(join(directory, "key-id-mismatch.manifest.armour"), manifestArmour(packKeyId, mismatchPayloadLine));
writeFileSync(join(directory, "key-id-mismatch.signature.hex"), signatureHex(mismatchPayloadLine, packPrivateKey) + "\n");

// Watermark vectors hang off the valid pack's content digest.
const validDigest = baseDigests.sha256;
const validWatermarkPayload = watermarkLine(watermarkKeyId, validDigest, false);
const validWatermarkSignature = signatureHex(validWatermarkPayload, watermarkPrivateKey);

writeFileSync(join(directory, "watermark.manifest.armour"), watermarkArmour(watermarkKeyId, validWatermarkPayload));
writeFileSync(join(directory, "watermark.signature.hex"), validWatermarkSignature + "\n");

writeFileSync(
  join(directory, "watermark-digest-mismatch.manifest.armour"),
  watermarkArmour(watermarkKeyId, watermarkLine(watermarkKeyId, validDigest, true)),
);
writeFileSync(
  join(directory, "watermark-digest-mismatch.signature.hex"),
  signatureHex(
    watermarkLine(watermarkKeyId, validDigest, true),
    watermarkPrivateKey,
  ) + "\n",
);

writeFileSync(
  join(directory, "watermark-tampered-signature.signature.hex"),
  (validWatermarkSignature[0] === "0" ? "1" : "0") + validWatermarkSignature.slice(1) + "\n",
);

writeFileSync(
  join(directory, "development-public-key.json"),
  JSON.stringify(
    {
      keys: [
        { keyId: packKeyId, label: "development-only", purpose: "pack-signing", seedHex: packSeedHex, publicKeyHex: publicKeyHex(packPrivateKey) },
        { keyId: watermarkKeyId, label: "development-only", purpose: "pack-watermark", seedHex: watermarkSeedHex, publicKeyHex: publicKeyHex(watermarkPrivateKey) },
      ],
    },
    null,
    2,
  ) + "\n",
);

process.stdout.write(`pack-key public key: ${publicKeyHex(packPrivateKey)}\n`);
process.stdout.write(`watermark public key: ${publicKeyHex(watermarkPrivateKey)}\n`);
