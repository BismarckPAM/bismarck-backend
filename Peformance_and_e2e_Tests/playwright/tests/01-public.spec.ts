import { test, expect } from '@playwright/test';
import { urls } from '../support/config.js';

test.beforeEach(() => { urls(); });

test('E2E-01 public landing loads successfully and login link works', async ({ page }) => {
  const response = await page.goto('/');
  expect(response?.status()).toBe(200);
  await expect(page.locator('body')).toContainText(/Bismarck/i);
  // Click the actual link rather than jumping directly to the destination.
  await page.locator('a[href="/login"]').first().click();
  await expect(page).toHaveURL(/\/login$/);
  await expect(page.getByRole('button', { name: 'Authenticate & Enter' })).toBeVisible();
});

test('E2E-02 login exposes labelled inputs and password visibility works both ways', async ({ page }) => {
  await page.goto('/login');
  await expect(page.getByLabel('Corporate Email Address')).toBeVisible();
  const password = page.getByLabel('Master Security Password');
  await expect(password).toHaveAttribute('type', 'password');
  await page.getByRole('button', { name: 'Show password' }).click();
  await expect(password).toHaveAttribute('type', 'text');
  await page.getByRole('button', { name: 'Hide password' }).click();
  await expect(password).toHaveAttribute('type', 'password');
});

for (const [index, [suffix, email, password]] of [
  ['both empty', '', ''], ['email missing', '', 'test-password'], ['password missing', 'nobody@example.invalid', ''],
].entries()) {
  test(`E2E-03.${index + 1} ${suffix} is rejected locally without a login POST`, async ({ page }) => {
    let posts = 0;
    page.on('request', r => { if (new URL(r.url()).pathname === '/api/identity/auth/login' && r.method() === 'POST') posts++; });
    await page.goto('/login');
    await page.getByLabel('Corporate Email Address').fill(email);
    await page.getByLabel('Master Security Password').fill(password);
    await page.getByRole('button', { name: 'Authenticate & Enter' }).click();
    await expect(page.getByRole('alert')).toHaveText('Invalid email or password');
    expect(posts).toBe(0);
    await expect(page).toHaveURL(/\/login$/);
  });
}

for (const [index, route] of ['/dashboard', '/resources', '/my-requests', '/approval-queue', '/admin/users', '/jit-access'].entries()) {
  test(`E2E-04.${index + 1} anonymous direct navigation to ${route} returns to login`, async ({ page }) => {
    await page.goto(route);
    await expect(page).toHaveURL(/\/login(?:[?#]|$)/);
    await expect(page.getByTestId('logout-btn')).toHaveCount(0);
    expect(await page.evaluate(() => localStorage.getItem('bismarck_pam_token'))).toBeNull();
  });
}

test('E2E-05 unknown route presents an actual not-found screen', async ({ page }) => {
  await page.goto('/e2e-route-that-does-not-exist');
  await expect(page.getByRole('heading', { name: /404|not found/i })).toBeVisible();
});
