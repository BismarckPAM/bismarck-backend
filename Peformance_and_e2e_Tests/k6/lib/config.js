export const baseUrl = (__ENV.BASE_URL || '').replace(/\/$/, '');
export const token = __ENV.ACCESS_TOKEN || '';
export const authorization = token ? { Authorization: `Bearer ${token}` } : {};
export const mode = __ENV.PROFILE || 'demo';
const count = (v, fallback) => Number.isFinite(Number(v)) && Number(v) > 0 ? Number(v) : fallback;
export const safeVus = Math.min(count(__ENV.MAX_VUS, 5), 10);
export const latencyLimit = count(__ENV.P95_LIMIT_MS, 2000);
export function requireConfig() {
  if (!/^https?:\/\//.test(baseUrl)) throw new Error('BASE_URL required: use the deployed API gateway URL (not necessarily the frontend URL).');
}
export function scenarioStages(kind) {
  if (kind === 'stress') return [{duration:'15s',target:1},{duration:'30s',target:safeVus},{duration:'30s',target:Math.min(safeVus * 2, 10)},{duration:'15s',target:0}];
  if (kind === 'spike') return [{duration:'10s',target:1},{duration:'5s',target:Math.min(safeVus * 2,10)},{duration:'20s',target:Math.min(safeVus * 2,10)},{duration:'10s',target:0}];
  if (kind === 'endurance') return [{duration:'20s',target:safeVus},{duration:'2m',target:safeVus},{duration:'20s',target:0}];
  return [{duration:'15s',target:1},{duration:'40s',target:safeVus},{duration:'15s',target:0}];
}
