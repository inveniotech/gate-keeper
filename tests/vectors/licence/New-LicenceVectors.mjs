// Regenerates the public licence-token vectors from the development seed.
// The seed is a test key. It must never sign a customer licence.
// Usage, from the repository root: node tests/vectors/licence/New-LicenceVectors.mjs

import { createPrivateKey, createPublicKey, sign } from "node:crypto";
import { writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const directory = dirname(fileURLToPath(import.meta.url));
const keyId = "gklic-dev-1";
const seedHex = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";
const seed = Buffer.from(seedHex, "hex");
const pkcs8 = Buffer.concat([
  Buffer.from("302e020100300506032b657004220420", "hex"),
  seed,
]);
const privateKey = createPrivateKey({ key: pkcs8, format: "der", type: "pkcs8" });
const spki = createPublicKey(privateKey).export({ type: "spki", format: "der" });
const publicKeyHex = spki.subarray(spki.length - 32).toString("hex");

function payload(claims) {
  return JSON.stringify(claims);
}

function baseClaims(extra) {
  return {
    schemaVersion: 1,
    product: "gatekeeper",
    keyId,
    orgId: "ORG-EXAMPLE-001",
    cacRegistrationNumber: "RC0000000",
    purchasedOn: "2026-11-01",
    ...extra,
  };
}

function signatureHex(payloadLine) {
  return sign(null, Buffer.from(payloadLine, "utf8"), privateKey).toString("hex");
}

function armour(tokenKeyId, payloadLine, signature) {
  return [
    "-----BEGIN GATEKEEPER LICENCE-----",
    `Key-Id: ${tokenKeyId}`,
    `Signature: ${signature}`,
    payloadLine,
    "-----END GATEKEEPER LICENCE-----",
    "",
  ].join("\n");
}

const withUpdates = payload(baseClaims({ updatesUntil: "2027-11-01" }));
const oneTime = payload(baseClaims({}));
const withUpdatesSignature = signatureHex(withUpdates);
const oneTimeSignature = signatureHex(oneTime);

const tamperedPayload = withUpdates.replace("ORG-EXAMPLE-001", "ORG-EXAMPLE-002");
const tamperedSignature = (withUpdatesSignature[0] === "0" ? "1" : "0") + withUpdatesSignature.slice(1);

const outOfOrder = payload(baseClaims({ updatesUntil: "2026-10-01" }));
const mismatchPayload = payload({
  schemaVersion: 1,
  product: "gatekeeper",
  keyId: "gklic-other",
  orgId: "ORG-EXAMPLE-001",
  cacRegistrationNumber: "RC0000000",
  purchasedOn: "2026-11-01",
});

const unknownPayload = payload({
  schemaVersion: 1,
  product: "gatekeeper",
  keyId: "gklic-unknown",
  orgId: "ORG-EXAMPLE-001",
  cacRegistrationNumber: "RC0000000",
  purchasedOn: "2026-11-01",
});

writeFileSync(
  join(directory, "development-public-key.json"),
  JSON.stringify(
    {
      keyId,
      label: "development-only",
      purpose: "licence-signing",
      seedHex,
      publicKeyHex,
    },
    null,
    2,
  ) + "\n",
);

writeFileSync(join(directory, "claims-with-updates.gklic"), armour(keyId, withUpdates, withUpdatesSignature));
writeFileSync(join(directory, "claims-one-time.gklic"), armour(keyId, oneTime, oneTimeSignature));
writeFileSync(join(directory, "tampered-payload.gklic"), armour(keyId, tamperedPayload, withUpdatesSignature));
writeFileSync(join(directory, "tampered-signature.gklic"), armour(keyId, withUpdates, tamperedSignature));
writeFileSync(join(directory, "unknown-key.gklic"), armour("gklic-unknown", unknownPayload, signatureHex(unknownPayload)));
writeFileSync(join(directory, "key-id-mismatch.gklic"), armour(keyId, mismatchPayload, signatureHex(mismatchPayload)));
writeFileSync(join(directory, "claims-dates-out-of-order.gklic"), armour(keyId, outOfOrder, signatureHex(outOfOrder)));
writeFileSync(
  join(directory, "malformed-armour.gklic"),
  [
    "-----BEGIN GATEKEEPER LICENCE-----",
    `Key-Id: ${keyId}`,
    "Signature: 00",
    "{\"schemaVersion\":1}",
    "{\"schemaVersion\":1}",
    "",
  ].join("\n"),
);
writeFileSync(
  join(directory, "discovery.json"),
  JSON.stringify(
    {
      cases: [
        {
          name: "missing-file",
          flagSet: true,
          flagExists: false,
          tokenTextSet: true,
          tokenFileSet: true,
          tokenFileExists: true,
          configFileExists: true,
          mountedFileExists: true,
          expectSource: "flag",
          expect: "missing-file",
        },
        {
          name: "later-source-when-nothing-earlier-is-set",
          flagSet: false,
          flagExists: false,
          tokenTextSet: false,
          tokenFileSet: false,
          tokenFileExists: false,
          configFileExists: false,
          mountedFileExists: true,
          expectSource: "mounted-file",
          expect: "present",
        },
      ],
    },
    null,
    2,
  ) + "\n",
);

process.stdout.write(`publicKeyHex=${publicKeyHex}\n`);
process.stdout.write(`withUpdatesBytes=${Buffer.byteLength(withUpdates)}\n`);
