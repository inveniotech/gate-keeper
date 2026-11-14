// SPDX-License-Identifier: Apache-2.0
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { Writable } from 'node:stream';
import { loadInstallation } from '../middleware/node/dist/esm/index.js';
import { fixture, keys } from '../middleware/node/test-fixtures.ts';
import { createApp as express } from './express/app.mjs';
import { createApp as nest } from './nestjs/app.mjs';
import { request } from './request.mjs';

for (const [framework, create] of [['Express', express], ['NestJS', nest]]) {
  test(`${framework} should redact logs and responses and check consent through its real request pipeline`, async () => {
    const logs = [];
    const sink = new Writable({ write(chunk, _encoding, done) { logs.push(chunk.toString()); done(); } });
    const runtime = loadInstallation({ embedded: [fixture()], keys });
    const application = await create(runtime, sink);
    try {
      for (const [subject, granted] of [['synthetic-subject', true], ['other-subject', false]]) {
        const response = await request(application.handler, `/customer?subject=${subject}`);
        assert.equal(response.status, 200);
        assert.deepEqual(response.body, { customer: { bvn: 'bankVerificationNumber [REDACTED:pii.bvn]' }, granted });
      }
      assert.equal(logs.length, 2);
      assert.ok(logs.every(line => line.includes('[REDACTED:pii.bvn]')));
      assert.ok(logs.every(line => !line.includes('32826314843') && !line.includes('synthetic-subject')));
    } finally { await application.close(); }
  });
}
