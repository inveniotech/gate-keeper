# Evidence-sink transport, version 1

**Status: draft — not accepted.** This specification is an AI draft for the owner to review and approve (rule 15 of `AGENTS.md`). It records how a pipeline step copies scan evidence into a store the customer already controls. Nothing in this document is final, accepted or approved until the owner says so.

This document is the written contract for work package P0-07 ([issue #6](https://github.com/inveniotech/gate-keeper/issues/6)). It does not implement the pipeline step; that is P4-04. The evidence file itself is specified by the results format (P0-04). The repository identity string is specified by the configuration specification (P0-05).

This draft does not add a JSON Schema file. The contract is a path, a sidecar and a pipeline step, not a new JSON document. A later change that adds a schema updates that schema, this specification and the version field in the same pull request (rule 24 of `AGENTS.md`).

---

## 0. Document status

### 0.1 Decision status

Normative text in this specification comes from the recorded decisions listed here. The specification itself stays a draft, because rule 15 reserves final approval of specifications to the owner.

| Identifier | Status | What it settles |
|---|---|---|
| IQ-27 | Decided | The binary writes a local file and contains no network code. A pipeline step uploads with tools the customer already runs. |
| IQ-28 | Decided | That step prints the SHA-256 of the evidence file into the continuous-integration log. Customer-side signing of the evidence file is not part of version 1. |
| IQ-29 | Decided | The sink path is `<root>/<repository-id>/<branch>/<evaluatedOn>-<commit>.gk.json`. Pull-request scans use `<root>/<repository-id>/prs/<number>/`. Posture uses default-branch scans only. The identity inside the evidence file is the identity that counts, not the path. |
| IQ-46 | Decided | A results object is complete only when its `.sha256` sidecar is present and matches the object. |
| IQ-85 | Decided | The same step writes a sidecar named `<results>.sha256` beside the evidence file. The log line stays. |

The register entry for each identifier is an owner record. This specification cites the identifier and does not describe where that register is kept.

### 0.2 Normative keywords

The key words **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT** and **MAY** are to be interpreted as described in RFC 2119. When this document writes "the binary", it means `gatekeeper`. When it writes "the evidence file", it means the Gate-Keeper evidence JSON for one scan. SHA-256 means the 256-bit hash from FIPS 180-4, written as 64 lowercase hexadecimal characters.

### 0.3 Change log

| Version | Date | Change |
|---|---|---|
| 1 (draft) | 2 November 2026 | First draft. |
| 1 (draft) | 13 November 2026 | Local file storage is part of the free scanner. Every remote evidence sink is a paid Feature Pack feature. |

### 0.4 Details this draft fixes

The decisions name the path shape, the sidecar name, and the fact that the binary does not upload. They do not name the sidecar bytes, the retry count, or what a failed copy does to the scan's exit code. This draft fixes the following so that two pipelines store the same witness. Each item is part of the draft for the owner to accept or amend.

1. **The upload step is given the evidence path.** This specification does not name a command-line flag for the binary. The examples call that local file `results.gk.json`.
2. **Only the evidence JSON is stored in the sink.** A SARIF log stays with the continuous-integration platform's own code-scanning upload. Version 1 does not give SARIF a sink path.
3. **The witness line** is 64 lowercase hex characters, two spaces, the final path segment of the stored evidence file, and one LF. The sidecar file is that line. The log line is the same text.
4. **An empty revision** uses the file name `<evaluatedOn>-norev.gk.json`. A scan outside git still has a civil date.
5. **Branch names** may contain `/`. A repository identity or a branch MUST NOT contain whitespace, `\`, a leading `/`, or a `..` segment. The pull-request number is a decimal integer with no sign and no padding.
6. **A failed copy is not exit code 2.** The scan has already finished. The upload step fails the job and leaves the local evidence file as the scan wrote it. Exit code 2 remains a failure of the scan itself.
7. **The copy is attempted three times.** The step does not run the scan again. This specification does not add a delay between attempts.
8. **No new secret.** Folder, S3-compatible and Azure Blob copies use the rights the job already has. There is no Gate-Keeper credential and no default account.

---

## 1. Scope and non-goals

### 1.1 In scope

The default of not uploading. The path of an evidence file and its sidecar in a customer sink. The witness line. Where credentials come from. What a failed copy does, and what it does not do to the scan. One worked example for a folder, one for an S3-compatible bucket, and one for Azure Blob.

### 1.2 Not in scope

- The bytes inside the evidence file (the results format).
- How the dashboard polls, stores or displays what it reads. A consumer MUST treat a missing or mismatched sidecar as incomplete, and that is the whole of the rule stated here.
- SARIF upload.
- Marking this specification accepted.

---

## 2. Zero egress

The binary writes the evidence file on the machine that ran the scan. It does not open a connection in order to scan, and it does not contain upload code. A repository that runs only the scan therefore sends nothing.

The default pipeline publishes the evidence file as an artefact of that pipeline and stops there. No sink is contacted.

Local file storage is part of the free scanner. That is the evidence file written on the machine that ran the scan, including a copy to a folder the job can already write. Every remote evidence sink is a paid Feature Pack feature. That includes an S3-compatible bucket, an Azure Blob container, and any other destination that is not a local file. This specification describes the path those destinations share. It does not describe how the Feature Pack is licensed.

A sink exists only when the customer adds a later step and names the destination. The destination is one of:

- a folder the job can already write to, including a mounted share
- an S3-compatible bucket the customer owns
- an Azure Blob container the customer owns

The step uses the customer's own tools: a file copy, `aws s3 cp`, or `az storage blob upload`. Those tools are not part of the binary. The step MUST NOT send the file to an address operated by Invenio. There is no default host, bucket, account or URL. If the customer does not name a destination, the step is not added, and the scan's lack of network code is unchanged.

The network connection, when there is one, is made by the customer's tool to the customer's destination. It is not made by the scan.

---

## 3. What is copied

The scan step runs first.

- Exit code 0 or 1 means the scan finished and wrote an evidence file. Exit code 1 means the findings met the failure threshold. The evidence file is still copied. Findings are part of the evidence.
- Exit code 2 means the scan failed and did not write the evidence file. The upload step does not run.

The upload step copies two objects:

1. The evidence file, stored under the name in section 4.
2. The sidecar from section 5, stored beside it.

The step computes the digest from the local evidence bytes, writes the sidecar beside that local file, prints the witness line to the job log, and then copies both objects. The local evidence file is not modified.

---

## 4. Path layout

`<root>` is the destination the customer named: a directory, a bucket prefix, or a container prefix. It has no default.

`<repository-id>` is the repository identity from the configuration specification: `repository.id` when it is set, otherwise the normalised git remote. The evidence file carries the same identity. A reader MUST trust the identity inside the file when the path and the file disagree.

`<branch>` is the branch that was scanned. `<evaluatedOn>` is the civil date of the scan, `YYYY-MM-DD`. `<commit>` is the 40-character lowercase hexadecimal revision. When the revision is empty, the file name uses `norev` instead of a commit.

Default-branch and other branch scans:

```text
<root>/<repository-id>/<branch>/<evaluatedOn>-<commit>.gk.json
```

Pull-request scans:

```text
<root>/<repository-id>/prs/<number>/<evaluatedOn>-<commit>.gk.json
```

`<number>` is the pull-request number. Pull-request scans are stored so they can be read. They do not count as posture. Posture uses scans stored on the default branch only.

Example, with synthetic names. The root is the customer's prefix `gatekeeper`. The identity is `github.com/Acme/payments`. The branch is `main`. The date is 2 November 2026. The revision is forty hex characters:

```text
gatekeeper/github.com/Acme/payments/main/2026-11-02-0123456789abcdef0123456789abcdef01234567.gk.json
```

The same scan for pull request 42:

```text
gatekeeper/github.com/Acme/payments/prs/42/2026-11-02-0123456789abcdef0123456789abcdef01234567.gk.json
```

A branch such as `feature/payments` is two path segments. That is allowed. A segment of `..` is not.

---

## 5. Integrity

The digest is SHA-256 over the evidence file's bytes, including its trailing newline. The witness line is:

```text
<64 lowercase hex characters><two spaces><evidence file name><LF>
```

`<evidence file name>` is the final path segment, not the directory. For the example in section 4 the segment is `2026-11-02-0123456789abcdef0123456789abcdef01234567.gk.json`.

The sidecar object uses that evidence name plus `.sha256`. It is stored in the same directory as the evidence object. Its entire contents are the witness line. The job log prints the same line, so a reader who still has the log can compare it with the sidecar.

Worked digest, so the line can be checked by hand. The bytes are the six characters `example` followed by one LF. This is not an evidence file. It only shows the line shape. SHA-256 of those bytes is `13550350a8681c84c861aac2e5b440161c2b33a3e4f302ac680ca5b686de48de`, so the line is:

```text
13550350a8681c84c861aac2e5b440161c2b33a3e4f302ac680ca5b686de48de  example
```

A consumer MUST NOT treat an evidence object as complete unless the sidecar exists in the same directory and its digest equals the SHA-256 of the object. A copy that uploaded the evidence and failed to upload the sidecar leaves an incomplete pair. The next attempt copies both.

---

## 6. Credentials

The binary never reads sink credentials.

| Transport | What the step uses |
|---|---|
| Folder | The job's existing right to write to the mounted path. No cloud credential. |
| S3-compatible | The credential chain the `aws` command already uses in that job. |
| Azure Blob | The login the `az` command already uses in that job. |

The examples do not set an access key, a connection string or a shared-access signature. A pipeline that needs those values takes them from the customer's own secret store and passes them the way that cloud's tool already documents. This specification does not name a secret.

---

## 7. Retries and failure

The upload step copies both objects, up to three times. An attempt fails if either object is not stored, or if the stored sidecar does not match the stored evidence. The next attempt copies both again. The scan is not run again. After the third failed attempt the step fails the job.

That failure does not change the scan step's exit code. A scan that exited 0 stays 0. A scan that exited 1 stays 1. The local evidence file is left as the scan wrote it. The upload failure is a failed job step. It is not exit code 2, because exit code 2 means the scan itself could not finish.

A job that wants the evidence stored therefore fails when the copy fails, and the scan result remains visible on the scan step.

---

## 8. Worked examples

The three examples use the same local file, `results.gk.json`, the same identity, branch, date and revision as section 4, and the same witness line. They are GitHub Actions steps because that is the first wrapper. The commands are what any other pipeline runs. Names such as `acme-evidence` are synthetic. Section 7 is what repeats a failed copy. The examples show the copy itself, once.

Section 8.1 is local file storage and is part of the free scanner. Sections 8.2 and 8.3 are remote evidence sinks. Every remote evidence sink is a paid Feature Pack feature.

Each example runs only when the scan step succeeded. `scan` below is that step's id. The digest commands print the witness line for `results.gk.json` and write it to `results.gk.json.sha256`. The stored file name is the layout name, not `results.gk.json`.

### 8.1 Folder

The share is already mounted at `/mnt/evidence`. The copy uses the job's filesystem rights.

```yaml
- name: Copy evidence to the mounted folder
  if: steps.scan.outcome == 'success'
  shell: bash
  run: |
    set -euo pipefail
    name="2026-11-02-0123456789abcdef0123456789abcdef01234567.gk.json"
    dest="/mnt/evidence/github.com/Acme/payments/main"
    line="$(sha256sum results.gk.json | cut -d ' ' -f 1)  ${name}"
    printf '%s\n' "$line" | tee results.gk.json.sha256
    mkdir -p "$dest"
    cp results.gk.json "$dest/$name"
    cp results.gk.json.sha256 "$dest/$name.sha256"
```

On Windows the same two files are copied with `Copy-Item` after the same witness line has been written. The digest bytes do not change with the operating system.

### 8.2 S3-compatible storage

The job's existing AWS configuration selects the account and the region. The example adds no key.

```yaml
- name: Copy evidence to the S3-compatible bucket
  if: steps.scan.outcome == 'success'
  shell: bash
  run: |
    set -euo pipefail
    name="2026-11-02-0123456789abcdef0123456789abcdef01234567.gk.json"
    dest="s3://acme-evidence/gatekeeper/github.com/Acme/payments/main"
    line="$(sha256sum results.gk.json | cut -d ' ' -f 1)  ${name}"
    printf '%s\n' "$line" | tee results.gk.json.sha256
    aws s3 cp results.gk.json "$dest/$name"
    aws s3 cp results.gk.json.sha256 "$dest/$name.sha256"
```

An S3-compatible service that is not Amazon's uses the endpoint the customer's AWS configuration already names. The step does not invent one.

### 8.3 Azure Blob

The job is already logged in to the customer's Azure account. The example does not set a key.

```yaml
- name: Copy evidence to Azure Blob
  if: steps.scan.outcome == 'success'
  shell: bash
  run: |
    set -euo pipefail
    name="2026-11-02-0123456789abcdef0123456789abcdef01234567.gk.json"
    blob="gatekeeper/github.com/Acme/payments/main/$name"
    line="$(sha256sum results.gk.json | cut -d ' ' -f 1)  ${name}"
    printf '%s\n' "$line" | tee results.gk.json.sha256
    az storage blob upload --auth-mode login --account-name acmeevidence \
      --container-name evidence --file results.gk.json --name "$blob" --overwrite true
    az storage blob upload --auth-mode login --account-name acmeevidence \
      --container-name evidence --file results.gk.json.sha256 --name "$blob.sha256" --overwrite true
```

`--overwrite true` replaces an object stored for the same date and commit. The digest of the same evidence bytes does not change, so a repeated copy is the same witness.
