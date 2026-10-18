import { test, expect } from '../support/fixtures.js';
import { endpoint } from '../support/api.js';

// These are browser resilience checks with controlled responses. They are NOT
// evidence that a real failing backend recovered; see README classification.
test('E2E-30 [controlled] Resource Service 503 shows error; retry loads real data', async ({ dev }) => {
  await dev.page.route('**/api/resources', route => route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'Injected test outage' }) }));
  await dev.page.goto('/resources');
  await expect(dev.page.getByTestId('error-state')).toBeVisible();
  await expect(dev.page.getByRole('heading', { name: 'Failed to Load Resources' })).toBeVisible();
  await expect(dev.page.getByRole('table', { name: 'Privileged Resources' })).toHaveCount(0);
  await dev.page.unroute('**/api/resources');
  const response = dev.page.waitForResponse(r => new URL(r.url()).pathname === endpoint.resources);
  await dev.page.getByRole('button', { name: 'Try Again' }).click();
  expect((await response).status()).toBe(200);
  await expect(dev.page.getByTestId('error-state')).toHaveCount(0);
  await expect(dev.page.getByRole('table', { name: 'Privileged Resources' })).toBeVisible();
});

test('E2E-31 [controlled] successful empty inventory is distinct from service failure', async ({ dev }) => {
  await dev.page.route('**/api/resources', route => route.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));
  await dev.page.goto('/resources');
  await expect(dev.page.getByTestId('empty-state')).toBeVisible();
  await expect(dev.page.getByRole('heading', { name: 'No resources found', exact: true })).toBeVisible();
  await expect(dev.page.getByTestId('error-state')).toHaveCount(0);
  await expect(dev.page.getByRole('textbox', { name: 'Search resources' })).toHaveCount(0);
});

test('E2E-32 [controlled] malformed 200 response is rejected instead of masquerading as empty data', async ({ dev }) => {
  await dev.page.route('**/api/resources', route => route.fulfill({ status: 200, contentType: 'application/json', body: '{"items":[]}' }));
  await dev.page.goto('/resources');
  await expect(dev.page.getByTestId('error-state')).toBeVisible();
  await expect(dev.page.getByTestId('empty-state')).toHaveCount(0);
});

test('E2E-33 [controlled] network failure renders a recoverable error, not a success screen', async ({ dev }) => {
  await dev.page.route('**/api/resources', route => route.abort('failed'));
  await dev.page.goto('/resources');
  await expect(dev.page.getByTestId('error-state')).toBeVisible();
  await expect(dev.page.getByRole('button', { name: 'Try Again' })).toBeEnabled();
  await expect(dev.page.getByRole('table', { name: 'Privileged Resources' })).toHaveCount(0);
});

test('E2E-34 [controlled] API 401 clears all session fields and returns to login', async ({ dev }) => {
  await dev.page.route('**/api/resources', route => route.fulfill({ status: 401, contentType: 'application/json', body: '{"message":"Unauthorized"}' }));
  await dev.page.goto('/resources');
  await expect(dev.page).toHaveURL(/\/login$/);
  expect(await dev.page.evaluate(() => ['bismarck_pam_token', 'bismarck_pam_user', 'bismarck_pam_expires_at'].map(k => localStorage.getItem(k)))).toEqual([null, null, null]);
  await expect(dev.page.getByTestId('logout-btn')).toHaveCount(0);
});
