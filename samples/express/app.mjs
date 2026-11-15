// SPDX-License-Identifier: Apache-2.0
import express from 'express';
import { pathToFileURL } from 'node:url';
import { createExpressMiddleware } from '@inveniotech/gate-keeper/express';
import { installation, services } from '../installation.mjs';
import { request } from '../request.mjs';

const syntheticValue = 'bankVerificationNumber 32826314843';

/** Builds an Express application with real logging and atomic JSON response filtering. */
export async function createApp(runtime, sink = process.stdout) {
  const { logger, consent } = services(runtime, sink);
  const app = express();
  app.use(createExpressMiddleware(runtime, ['/customer/bvn'], 1024 * 1024));
  app.get('/customer', async (req, res) => {
    const granted = await consent.isGranted(typeof req.query.subject === 'string' ? req.query.subject : 'synthetic-subject', 'acme.marketing-sms');
    logger.info({ record: syntheticValue }, 'Customer record');
    res.type('application/json').json({ customer: { bvn: syntheticValue }, granted });
  });
  return { handler: app, close: async () => { logger.flush(); } };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    const app = await createApp(installation(process.argv[2], process.argv[3]));
    try { console.log(JSON.stringify((await request(app.handler, '/customer')).body)); }
    finally { await app.close(); }
  } catch { console.error('The Express sample could not run. Build the middleware and supply a signed pack directory and public-only key catalogue.'); process.exitCode = 1; }
}
