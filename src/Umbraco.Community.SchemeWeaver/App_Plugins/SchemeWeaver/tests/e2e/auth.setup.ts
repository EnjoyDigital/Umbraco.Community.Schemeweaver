import { test as setup, expect } from '@playwright/test';

const STORAGE_STATE = process.env.STORAGE_STATE_PATH || 'tests/e2e/.auth/user.json';

setup('authenticate', async ({ page }) => {
  const email = process.env.UMBRACO_USER || 'admin@test.com';
  const password = process.env.UMBRACO_PASSWORD || 'Test12345678!';

  await page.goto('/umbraco/login');
  await page.waitForLoadState('networkidle');

  // Fill in login form
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: /login/i }).click();

  // Wait for successful login - backoffice should load
  await expect(page.locator('umb-backoffice')).toBeVisible({ timeout: 30000 });

  // Save authentication state
  await page.context().storageState({ path: STORAGE_STATE });
});
