import { test, expect, loginUI } from '../support/fixtures.js';
import { endpoint, gotoData, type Mine } from '../support/api.js';

interface Resource { id: string; name: string; type: string; environment: string; criticality: string; }

test('E2E-10 valid developer login succeeds through the real Identity Service', async ({ page }) => {
  await loginUI(page, 'dev');
  await expect(page.getByRole('heading', { name: 'Dashboard', exact: true })).toBeVisible();
});

test('E2E-11 non-default deep link survives login', async ({ page }) => {
  const { credentials } = await import('../support/config.js');
  const account = credentials('dev');
  await page.goto('/resources');
  await expect(page).toHaveURL(/\/login$/);
  await page.getByLabel('Corporate Email Address').fill(account.email);
  await page.getByLabel('Master Security Password').fill(account.password);
  await page.getByRole('button', { name: 'Authenticate & Enter' }).click();
  await expect(page).toHaveURL(/\/resources$/);
  await expect(page.getByRole('heading', { name: 'Privileged Resources', exact: true })).toBeVisible();
});

test('E2E-12 refresh retains the real session and loads backend data', async ({ dev }) => {
  await gotoData(dev.page, '/resources', endpoint.resources);
  const response = dev.page.waitForResponse(r => new URL(r.url()).pathname === endpoint.resources);
  await dev.page.reload();
  expect((await response).status()).toBe(200);
  await expect(dev.page).toHaveURL(/\/resources$/);
  await expect(dev.page.getByTestId('logout-btn')).toBeVisible();
});

test('E2E-13 logout removes all stored session fields and blocks back navigation', async ({ dev }) => {
  await dev.page.goto('/dashboard');
  await dev.page.getByTestId('logout-btn').click();
  await expect(dev.page).toHaveURL(/\/login$/);
  expect(await dev.page.evaluate(() => ['bismarck_pam_token', 'bismarck_pam_user', 'bismarck_pam_expires_at'].map(k => localStorage.getItem(k)))).toEqual([null, null, null]);
  await dev.page.goBack();
  await expect(dev.page).toHaveURL(/\/login$/);
  await dev.page.goto('/my-requests');
  await expect(dev.page).toHaveURL(/\/login$/);
});

test('E2E-14 expired stored session is cleared on reload', async ({ dev }) => {
  await dev.page.goto('/dashboard');
  // Deliberately expire the existing test session, without inventing a JWT.
  await dev.page.evaluate(() => localStorage.setItem('bismarck_pam_expires_at', '2000-01-01T00:00:00Z'));
  await dev.page.reload();
  await expect(dev.page).toHaveURL(/\/login$/);
  expect(await dev.page.evaluate(() => localStorage.getItem('bismarck_pam_token'))).toBeNull();
});

test('E2E-15 sidebar links navigate to request and history pages', async ({ dev }) => {
  await dev.page.goto('/dashboard');
  const navigation = dev.page.getByRole('navigation', { name: 'Main Navigation' });
  await navigation.getByRole('link', { name: 'Request privileged access', exact: true }).click();
  await expect(dev.page).toHaveURL(/\/request-access$/);
  await expect(dev.page.getByRole('heading', { name: 'Request Access', exact: true })).toBeVisible();
  await navigation.getByRole('link', { name: 'My access requests', exact: true }).click();
  await expect(dev.page).toHaveURL(/\/my-requests$/);
  await expect(dev.page.getByRole('heading', { name: 'My Requests', exact: true })).toBeVisible();
});

for (const [index, route] of ['/users', '/approval-queue', '/audit', '/policies', '/onboarding', '/admin/users', '/admin/policies', '/admin/permissions'].entries()) {
  test(`E2E-16.${index + 1} ordinary developer is specifically denied ${route}`, async ({ dev }) => {
    await dev.page.goto(route);
    await expect(dev.page.getByTestId('forbidden')).toBeVisible();
    await expect(dev.page.getByRole('heading', { name: 'Access restricted' })).toBeVisible();
    await expect(dev.page.getByRole('button', { name: 'Confirm Approve', exact: true })).toHaveCount(0);
  });
}

test('E2E-17 Admin can load the actual directory and pending approval queue', async ({ admin }) => {
  const users = await gotoData(admin.page, '/users', endpoint.users);
  expect(Array.isArray(await users.json())).toBe(true);
  await expect(admin.page.getByTestId('forbidden')).toHaveCount(0);
  await expect(admin.page.getByRole('heading', { name: 'Identities & Users' })).toBeVisible();
  await gotoData(admin.page, '/approval-queue', endpoint.requests);
  await expect(admin.page.getByRole('heading', { name: 'Approval Queue' })).toBeVisible();
});

test('E2E-18 resources match API data; search excludes nonmatches and clear restores rows', async ({ dev }) => {
  const response = await gotoData(dev.page, '/resources', endpoint.resources);
  const resources = await response.json() as Resource[];
  expect(resources.length, 'Seed at least two resources for meaningful filtering').toBeGreaterThanOrEqual(2);
  const table = dev.page.getByRole('table', { name: 'Privileged Resources', exact: true });
  await expect(table.locator('tbody tr')).toHaveCount(resources.length);
  const search = dev.page.getByRole('textbox', { name: 'Search resources' });
  await search.fill(resources[0].name);
  await expect(table.locator('tbody')).toContainText(resources[0].name);
  const query = resources[0].name.toLowerCase();
  // Compare against the actual supported fields rather than assuming names are unique.
  const expected = resources.filter(r => [r.name, r.type, r.environment, r.criticality, (r as Resource & { description?: string }).description, (r as Resource & { ipAddress?: string }).ipAddress].some(v => v?.toLowerCase().includes(query)));
  await expect(table.locator('tbody tr')).toHaveCount(expected.length);
  await search.fill('se3112-nonexistent-resource-7f25ae');
  await expect(dev.page.getByTestId('no-search-results')).toBeVisible();
  await expect(table).toHaveCount(0);
  await dev.page.getByRole('button', { name: 'Clear Filters' }).click();
  await expect(table.locator('tbody tr')).toHaveCount(resources.length);
});

test('E2E-19 environment filter renders only the API resources in that environment', async ({ dev }) => {
  const response = await gotoData(dev.page, '/resources', endpoint.resources);
  const resources = await response.json() as Resource[];
  const environment = resources.find(x => x.environment)?.environment;
  expect(environment, 'Seed an environment-labelled resource').toBeTruthy();
  await dev.page.getByRole('combobox', { name: 'Filter by environment' }).selectOption(environment!);
  const rows = dev.page.getByRole('table', { name: 'Privileged Resources' }).locator('tbody tr');
  const expected = resources.filter(x => x.environment === environment);
  await expect(rows).toHaveCount(expected.length);
  await expect(rows.locator('.user-fullname')).toHaveText(expected.map(resource => resource.name));
});

test('E2E-20 Admin user search produces a real no-match state and restores the directory', async ({ admin }) => {
  await gotoData(admin.page, '/users', endpoint.users);
  const table = admin.page.getByRole('table');
  await expect(table).toBeVisible();
  const originalCount = await table.locator('tbody tr').count();
  expect(originalCount).toBeGreaterThan(0);
  const search = admin.page.getByRole('textbox', { name: 'Search users' });
  await search.fill('nonexistent-se3112-user-7f25ae');
  await expect(admin.page.getByTestId('no-search-results')).toBeVisible();
  await expect(table).toHaveCount(0);
  await search.fill('');
  await expect(admin.page.getByRole('table').locator('tbody tr')).toHaveCount(originalCount);
});

for (const [index, [name, reason, duration, error]] of [
  ['19-character justification', 'x'.repeat(19), '5', 'Justification must be at least 20 characters.'],
  ['whitespace justification', '   ', '5', 'Justification is required.'],
  ['zero duration', 'Valid test justification for validation', '0', 'Duration must be a positive number of minutes.'],
  ['negative duration', 'Valid test justification for validation', '-1', 'Duration must be a positive number of minutes.'],
  ['duration above maximum', 'Valid test justification for validation', '1441', 'Duration cannot exceed 1440 minutes (24h).'],
].entries()) {
  test(`E2E-21.${index + 1} ${name} blocks submission and sends zero POSTs`, async ({ dev }) => {
    await gotoData(dev.page, '/request-access', endpoint.resources);
    const resource = dev.page.locator('#resourceId');
    const options = await resource.locator('option').evaluateAll(options => options.map(o => (o as HTMLOptionElement).value).filter(Boolean));
    expect(options.length).toBeGreaterThan(0);
    await resource.selectOption(options[0]);
    await dev.page.locator('#requestedLevel').selectOption('1');
    await dev.page.locator('#reason').fill(reason);
    await dev.page.locator('#durationMinutes').fill(duration);
    let posts = 0;
    dev.page.on('request', r => { if (new URL(r.url()).pathname === endpoint.requests && r.method() === 'POST') posts++; });
    await dev.page.getByRole('button', { name: 'Submit Request' }).click();
    await expect(dev.page.getByText(error, { exact: true })).toBeVisible();
    expect(posts).toBe(0);
  });
}

test('E2E-22 missing resource and level are both rejected without a POST', async ({ dev }) => {
  await gotoData(dev.page, '/request-access', endpoint.resources);
  let posts = 0;
  dev.page.on('request', r => { if (new URL(r.url()).pathname === endpoint.requests && r.method() === 'POST') posts++; });
  await dev.page.getByRole('button', { name: 'Submit Request' }).click();
  await expect(dev.page.getByText('Resource is required.', { exact: true })).toBeVisible();
  await expect(dev.page.getByText('Access level is required.', { exact: true })).toBeVisible();
  expect(posts).toBe(0);
});

test('E2E-23 own history counters agree with persisted request states', async ({ dev }) => {
  const response = await gotoData(dev.page, '/my-requests', endpoint.mine);
  const history = await response.json() as Mine;
  expect(Array.isArray(history.items)).toBe(true);
  expect(history.items.every(x => x.requesterUserId === dev.user.id)).toBe(true);
  expect(history.counters.total).toBe(history.items.length);
  expect(history.counters.total).toBe(history.counters.pending + history.counters.approved + history.counters.rejected);
  // UI dashboard counters must reflect backend history, even after a fresh context.
  await dev.page.goto('/dashboard');
  for (const key of ['total', 'pending', 'approved', 'rejected'] as const)
    await expect(dev.page.getByTestId(`metric-${key}`)).toHaveText(String(history.counters[key]));
});

test('E2E-24 access simulator displays the exact real authorization decision and reason', async ({ admin }) => {
  await gotoData(admin.page, '/access-check', endpoint.resources);
  await expect(admin.page.getByRole('button', { name: 'Evaluate access' })).toBeEnabled();
  const response = admin.page.waitForResponse(r => new URL(r.url()).pathname === '/api/authorization/check' && r.request().method() === 'POST');
  await admin.page.getByRole('button', { name: 'Evaluate access' }).click();
  const result = await response;
  expect(result.status()).toBe(200);
  const data = await result.json();
  expect(['ALLOW', 'DENY', 'APPROVAL_REQUIRED']).toContain(data.decision);
  expect(data.reason).toBeTruthy();
  const panel = admin.page.getByTestId('decision-result');
  await expect(panel.locator('.decision-label')).toHaveText(data.decision.replace('_', ' '));
  await expect(panel.getByRole('heading')).toHaveText(data.reason);
});
