// SPDX-License-Identifier: Apache-2.0

// The documented corpus script (NFR-004, quality-budgets specification
// section 4.2). It generates the synthetic identifiers every golden-corpus
// case embeds, and it checks the small set of sequential examples whose
// validity must stay intact (IQ-49). Run it and compare its table with the
// values a review needs:
//
//     node tests/corpus/New-CorpusValues.mjs
//
// The generator is deterministic: a pinned seed drives a mulberry32
// generator, so every run on any machine prints the same table (rule 6).
// The values are synthetic; IQ-49 decided a generated value that happens to
// match a real identifier is not a problem in the corpus. This script is the
// one source of values for tests/corpus (rule 10); cases never invent a value.
//
// The NUBAN values follow the weighting the Central Bank of Nigeria
// published in its 2010 NUBAN implementation proposals: the check digit
// covers the 3-digit bank code and the 9-digit serial with the weights
// 3,7,3,3,7,3,3,7,3,3,7,3, modulo 10; ten minus the remainder, and a
// remainder of zero gives the check digit zero. The customer-facing account
// number is the 9-digit serial followed by the check digit. The generator
// verifies the two examples that proposal documents, so their check digits
// always stay valid (section 4.2 of the quality-budgets specification).

const seed = 20261104;

function mulberry32(pinnedSeed) {
  let state = pinnedSeed >>> 0;
  return function nextRandom() {
    state = (state + 0x6d2b79f5) >>> 0;
    let mixed = state;
    mixed = Math.imul(mixed ^ (mixed >>> 15), mixed | 1);
    mixed ^= mixed + (Math.imul(mixed ^ (mixed >>> 7), mixed | 61) ^ mixed);
    return ((mixed ^ (mixed >>> 14)) >>> 0) / 4294967296;
  };
}

const random = mulberry32(seed);

function randomDigitRun(length) {
  let digits = "";
  for (let index = 0; index < length; index++) {
    digits += String(Math.floor(random() * 10));
  }
  return digits;
}

function isSequentialOrRepeated(digits) {
  if (/^(\d)\1+$/.test(digits)) {
    return true;
  }
  let ascending = true;
  let descending = true;
  for (let index = 1; index < digits.length; index++) {
    const step = digits.charCodeAt(index) - digits.charCodeAt(index - 1);
    if (step !== 1) {
      ascending = false;
    }
    if (step !== -1) {
      descending = false;
    }
  }
  return ascending || descending;
}

function randomIdentifierValue(length) {
  for (;;) {
    const digits = randomDigitRun(length);
    if (!isSequentialOrRepeated(digits)) {
      return digits;
    }
  }
}

const nubanWeights = [3, 7, 3, 3, 7, 3, 3, 7, 3, 3, 7, 3];

function nubanCheckDigit(bankCode, serial9) {
  const twelve = (bankCode + serial9).split("").map(Number);
  const weighted = twelve.reduce(
    (sum, digit, index) => sum + digit * nubanWeights[index],
    0
  );
  const remainder = weighted % 10;
  return remainder === 0 ? 0 : 10 - remainder;
}

function nubanWithCheckDigit(bankCode, serial9) {
  return `${serial9}${nubanCheckDigit(bankCode, serial9)}`;
}

// The documented examples the corpus pins. The first two come from the
// worked examples in the Central Bank of Nigeria's 2010 NUBAN proposals;
// the bank verification number and the telephone number are the obviously
// fake sequential values documentation uses (IQ-49). A failure here is a
// corpus defect: their validity must stay intact.
const documentedExamples = [
  {
    label: "nuban-documented-account-one",
    value: nubanWithCheckDigit("011", "000001457"),
    expected: "0000014579",
  },
  {
    label: "nuban-documented-account-two",
    value: nubanWithCheckDigit("011", "000000022"),
    expected: "0000000220",
  },
  { label: "bvn-documented-example", value: "12345678901", expected: "12345678901" },
];

for (const example of documentedExamples) {
  if (example.value !== example.expected) {
    throw new Error(
      `The documented example ${example.label} must keep its value: expected ${example.expected}, computed ${example.value}.`
    );
  }
}

// The Nigerian mobile prefixes the corpus uses. The rule pack's own prefix
// list is pack data (P2-03); this list only documents what the cases embed.
const phonePrefixes = [
  "0803", "0806", "0810", "0813", "0814", "0816", "0703", "0706", "0903",
  "0906", "0805", "0807", "0811", "0812", "0815", "0705", "0802", "0808",
  "0701", "0901", "0809", "0817", "0909",
];

function randomPhone() {
  const prefix = phonePrefixes[Math.floor(random() * phonePrefixes.length)];
  return prefix + randomIdentifierValue(11 - prefix.length);
}

// Every value a corpus case embeds is a row of this table, so a reviewer can
// audit each identifier back to the script (NFR-004).
const generated = {};

generated["bvn-log"] = randomIdentifierValue(11);
generated["bvn-fixture"] = randomIdentifierValue(11);
generated["bvn-configuration"] = randomIdentifierValue(11);
generated["bvn-comment"] = randomIdentifierValue(11);
generated["bvn-api-model"] = randomIdentifierValue(11);
generated["bvn-utf16-configuration"] = randomIdentifierValue(11);
generated["bvn-very-long-line"] = randomIdentifierValue(11);
// A bank verification number shaped value with one digit missing: the
// context anchor stays, the shape check fails.
generated["bvn-wrong-length"] = generated["bvn-very-long-line"].slice(0, 10);

generated["nin-log"] = randomIdentifierValue(11);
generated["nin-fixture"] = randomIdentifierValue(11);
generated["nin-configuration"] = randomIdentifierValue(11);
generated["nin-comment"] = randomIdentifierValue(11);
// A twelve-digit value next to the personal-identity-number context: the
// shape check fails, and no eleven-digit run hides inside it.
generated["nin-wrong-length"] = generated["nin-comment"] + String((10 - Number(generated["nin-comment"][10])) % 10);

const bankCode = "011";
generated["nuban-source"] = nubanWithCheckDigit(bankCode, "463150822");
generated["nuban-migration"] = nubanWithCheckDigit(bankCode, "556134044");
generated["nuban-configuration"] = nubanWithCheckDigit(bankCode, "715280839");
// A NUBAN-shaped value whose check digit is five away from the valid one,
// so the bank-code residue the proposal documents fails for it.
generated["nuban-wrong-check-digit"] = (() => {
  const valid = generated["nuban-source"];
  const changed = (Number(valid[9]) + 5) % 10;
  return valid.slice(0, 9) + String(changed);
})();
generated["nuban-wrong-length"] = "146283051";

generated["phone-log"] = randomPhone();
generated["phone-api-model"] = randomPhone();
generated["phone-fixture"] = randomPhone();
generated["phone-comment"] = randomPhone();
// A telephone-shaped value with no eleven-digit run and a prefix outside
// every Nigerian mobile block.
generated["phone-not-nigerian"] = "+44-7700-900186";

generated["email-source"] = "amaka.ogbonna-42@ledger-ops.example";
generated["email-log"] = "processor-alerts@ledger-ops.example";
generated["email-configuration"] = "chidi.nwosu-18@ledger-ops.example";
generated["email-comment"] = "compliance-reviews@ledger-ops.example";
generated["email-fixture"] = "tunde.balogun-27@ledger-ops.example";
// The example.com placeholders the near-miss case carries (IQ-49).
generated["email-placeholder-primary"] = "complaints-desk@example.com";
generated["email-placeholder-secondary"] = "data-officer@example.com";

// Values individual edge cases need, appended last so the entries above
// keep the values earlier runs printed.
generated["bvn-gitignored"] = randomIdentifierValue(11);
generated["phone-symlink-target"] = randomPhone();
generated["reference-eleven-digit-unlabelled"] = randomIdentifierValue(11);

const table = {};
for (const example of documentedExamples) {
  table[example.label] = example.value;
}
for (const [label, value] of Object.entries(generated)) {
  table[label] = value;
}

const lines = [];
for (const [label, value] of Object.entries(table)) {
  lines.push(`${label} = ${value}`);
}
lines.push("");
lines.push("documented examples verified: 3");
if (process.argv[2] === "--values-json") {
  console.log(JSON.stringify(Object.values(table), null, 2));
} else if (process.argv[2] === "--benchmark") {
  const { mkdir, writeFile } = await import("node:fs/promises");
  const { join, resolve } = await import("node:path");
  const { createHash } = await import("node:crypto");
  const fileCount = Number(process.argv[5]);
  if (!process.argv[3] || process.argv[4] !== "--files" || ![500, 10000].includes(fileCount) || process.argv.length !== 6) {
    throw new Error("Use --benchmark <new-directory> --files 500 or 10000.");
  }
  const destination = resolve(process.argv[3]);
  // Exclusive directory creation prevents overwriting an existing repository.
  await mkdir(destination);
  const parameters = { generatorVersion: 1, seed, fileCount, historyScanned: false, utf16Every: 10, largeEvery: 20 };
  const parameterHash = createHash("sha256").update(JSON.stringify(parameters)).digest("hex");
  const next = mulberry32(seed);
  let totalBytes = 0;
  for (let index = 0; index < fileCount; index++) {
    let body = "";
    const size = index % 20 === 0 ? 4096 : 256;
    for (let letter = 0; letter < size; letter++) body += String.fromCharCode(97 + Math.floor(next() * 26));
    body += "\n";
    const bytes = index % 10 === 0 ? Buffer.concat([Buffer.from([255, 254]), Buffer.from(body, "utf16le")]) : Buffer.from(body, "utf8");
    await writeFile(join(destination, `file-${String(index).padStart(5, "0")}.txt`), bytes, { flag: "wx" });
    totalBytes += bytes.length;
  }
  await writeFile(destination + ".manifest.json", JSON.stringify({ ...parameters, parameterHash, totalBytes }, null, 2) + "\n", { flag: "wx" });
  console.log(`Generated ${fileCount} deterministic benchmark files.`);
} else {
  console.log(lines.join("\n"));
}
