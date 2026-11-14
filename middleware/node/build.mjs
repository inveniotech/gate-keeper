// SPDX-License-Identifier: Apache-2.0
import { spawnSync } from 'node:child_process';
import { copyFileSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
process.chdir(fileURLToPath(new URL('.', import.meta.url)));
// This literal directory belongs to this package and contains generated output only.
rmSync(new URL('./dist/', import.meta.url), { recursive: true, force: true });
for (const [format, module] of [['esm', 'ES2022'], ['cjs', 'CommonJS']]) {
  const result = spawnSync(process.execPath, ['node_modules/typescript/bin/tsc', '-p', 'tsconfig.json', '--module', module, '--outDir', `dist/${format}`], { stdio: 'inherit' });
  if (result.status !== 0) process.exit(result.status ?? 1);
  // TypeScript rewrites runtime imports but leaves declaration specifiers untouched.
  for (const file of readdirSync(`dist/${format}`).filter((name) => name.endsWith('.d.ts'))) {
    const path = `dist/${format}/${file}`;
    writeFileSync(path, readFileSync(path, 'utf8').replace(/(['"])(\.[^'"]+)\.ts\1/g, '$1$2.js$1'));
  }
}
mkdirSync('dist/cjs', { recursive: true });
writeFileSync('dist/cjs/package.json', '{ "type": "commonjs" }\n');
copyFileSync('../../LICENSE', 'LICENSE');
