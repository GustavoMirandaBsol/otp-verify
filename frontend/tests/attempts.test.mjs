import assert from 'node:assert/strict';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createServer } from 'vite';

const frontendRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const server = await createServer({
  root: frontendRoot,
  appType: 'custom',
  logLevel: 'silent',
  server: { middlewareMode: true },
});

try {
  const { ATTEMPT_LIMITS, buildAttemptSearchPath, createLatestRequestGate, isE164 } = await server.ssrLoadModule('/src/attempts.ts');

  assert.deepEqual([...ATTEMPT_LIMITS], [3, 5, 10], 'El combo debe ofrecer exactamente 3, 5 y 10.');
  assert.equal(isE164('+59170000000'), true, 'Debe aceptar un número E.164 boliviano.');
  assert.equal(isE164('+15551234567'), true, 'Debe aceptar otro número E.164 válido.');

  for (const invalid of ['59170000000', '+591 70000000', '+591ABC00000', '+1234567', '+1234567890123456']) {
    assert.equal(isE164(invalid), false, `Debe rechazar '${invalid}'.`);
  }

  assert.equal(
    buildAttemptSearchPath('+59170000000', 5),
    '/verifications/attempts?phone=%2B59170000000&limit=5',
    'La búsqueda debe codificar el teléfono y conservar el límite.',
  );

  const gate = createLatestRequestGate();
  const requestA = gate.start();
  assert.equal(gate.isCurrent(requestA), true, 'La solicitud recién iniciada debe ser vigente.');
  gate.invalidate();
  assert.equal(gate.isCurrent(requestA), false, 'Cambiar la búsqueda debe invalidar la respuesta pendiente.');
  const requestB = gate.start();
  const requestC = gate.start();
  assert.equal(gate.isCurrent(requestB), false, 'Una solicitud posterior debe invalidar la anterior.');
  assert.equal(gate.isCurrent(requestC), true, 'Solo la solicitud más reciente debe actualizar la pantalla.');

  console.log('PASS: límites y validación E.164 del frontend verificados.');
} finally {
  await server.close();
}
