// SPDX-License-Identifier: Apache-2.0
import 'reflect-metadata';
import { Controller, Get, Header, Module, Req } from '@nestjs/common';
import { NestFactory } from '@nestjs/core';
import { pathToFileURL } from 'node:url';
import { createNestInterceptor } from '@inveniotech/gate-keeper/nest';
import { installation, services } from '../installation.mjs';
import { request } from '../request.mjs';

const syntheticValue = 'bankVerificationNumber 32826314843';

/** Builds a NestJS application; decorator functions keep this JavaScript example free of a transpiler. */
export async function createApp(runtime, sink = process.stdout) {
  const { logger, consent } = services(runtime, sink);
  class CustomerController {
    async customer(req) {
      const granted = await consent.isGranted(typeof req.query.subject === 'string' ? req.query.subject : 'synthetic-subject', 'acme.marketing-sms');
      logger.info({ record: syntheticValue }, 'Customer record');
      return { customer: { bvn: syntheticValue }, granted };
    }
  }
  Controller('customer')(CustomerController);
  const descriptor = Object.getOwnPropertyDescriptor(CustomerController.prototype, 'customer');
  Get()(CustomerController.prototype, 'customer', descriptor);
  Header('Content-Type', 'application/json')(CustomerController.prototype, 'customer', descriptor);
  Req()(CustomerController.prototype, 'customer', 0);
  class SampleModule {}
  Module({ controllers: [CustomerController] })(SampleModule);
  const app = await NestFactory.create(SampleModule, { logger: false });
  app.useGlobalInterceptors(createNestInterceptor(runtime, ['/customer/bvn'], 1024 * 1024));
  await app.init();
  return { handler: app.getHttpAdapter().getInstance(), close: async () => { await app.close(); logger.flush(); } };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    const app = await createApp(installation(process.argv[2], process.argv[3]));
    try { console.log(JSON.stringify((await request(app.handler, '/customer')).body)); }
    finally { await app.close(); }
  } catch { console.error('The NestJS sample could not run. Build the middleware and supply a signed pack directory and public-only key catalogue.'); process.exitCode = 1; }
}
