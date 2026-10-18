export type Role = 'dev' | 'admin' | 'other';

export function required(name: string): string {
  const value = process.env[name]?.trim();
  if (!value) throw new Error(`Missing ${name}. Configure .env; required tests must not silently skip.`);
  return value;
}

export function urls() {
  const frontend = new URL(required('FRONTEND_URL'));
  const api = new URL(required('API_URL'));
  if (frontend.username || frontend.password || api.username || api.password)
    throw new Error('Use credential-free URLs; credentials belong in the dedicated environment variables.');
  return { frontend: frontend.origin, api: api.origin };
}

export function credentials(role: Role) {
  const prefix = `E2E_${role.toUpperCase()}`;
  return { email: required(`${prefix}_EMAIL`), password: required(`${prefix}_PASSWORD`) };
}

export function asyncTimeout() {
  const value = Number(process.env.E2E_ASYNC_TIMEOUT_MS || 60_000);
  if (!Number.isInteger(value) || value < 5000 || value > 120_000)
    throw new Error('E2E_ASYNC_TIMEOUT_MS must be between 5000 and 120000.');
  return value;
}

export function writeConfig() {
  if (process.env.E2E_ALLOW_WRITES !== 'true')
    throw new Error('Lifecycle/security tests need E2E_ALLOW_WRITES=true and disposable test data. Use npm run test:read otherwise.');
  const resourceId = required('E2E_RESOURCE_ID');
  if (!/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(resourceId))
    throw new Error('E2E_RESOURCE_ID must be the dedicated resource UUID.');
  const level = Number(process.env.E2E_LEVEL || 1);
  const duration = Number(process.env.E2E_DURATION_MINUTES || 5);
  if (!Number.isInteger(level) || level < 1 || level > 5) throw new Error('E2E_LEVEL must be 1..5.');
  if (!Number.isInteger(duration) || duration < 1 || duration > 10)
    throw new Error('Use a short test duration between 1 and 10 minutes.');
  return { resourceId, level, duration };
}
