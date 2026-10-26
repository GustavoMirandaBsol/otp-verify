import assert from 'node:assert/strict';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { loadConfigFromFile } from 'vite';

const frontendRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const configFile = path.join(frontendRoot, 'vite.config.ts');
const variableName = 'VITE_API_PROXY_TARGET';
const previousValue = process.env[variableName];
const expectedTarget = 'http://127.0.0.1:5999';

try {
  process.env[variableName] = expectedTarget;
  const loaded = await loadConfigFromFile(
    { command: 'serve', mode: 'development' },
    configFile,
    frontendRoot,
  );

  assert.ok(loaded, 'Vite debe cargar vite.config.ts');
  assert.equal(
    loaded.config.server?.proxy?.['/api']?.target,
    expectedTarget,
    'El proxy /api debe respetar VITE_API_PROXY_TARGET',
  );
  console.log('PASS: proxy configurable de Vite verificado.');
} finally {
  if (previousValue === undefined) delete process.env[variableName];
  else process.env[variableName] = previousValue;
}
