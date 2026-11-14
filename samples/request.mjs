// SPDX-License-Identifier: Apache-2.0
import { IncomingMessage, ServerResponse } from 'node:http';
import { Duplex } from 'node:stream';

/** Executes the actual HTTP framework pipeline on an in-memory socket; no server listens and no connection is made. */
export function request(handler, path) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    const socket = new Duplex({ read() {}, write(chunk, _encoding, done) { chunks.push(Buffer.from(chunk)); done(); } });
    const input = new IncomingMessage(socket);
    input.method = 'GET'; input.url = path; input.headers = { host: 'sample.invalid' };
    const output = new ServerResponse(input);
    output.assignSocket(socket);
    output.on('error', reject);
    output.on('finish', () => {
      try {
        const bytes = Buffer.concat(chunks).toString('utf8');
        resolve({ status: output.statusCode, body: JSON.parse(bytes.slice(bytes.indexOf('\r\n\r\n') + 4)) });
      } catch (error) { reject(error); }
      finally { socket.destroy(); }
    });
    handler(input, output, error => reject(error ?? new Error('The sample route did not handle the request.')));
  });
}
