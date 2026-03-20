import { test as setup } from '@playwright/test';
import { STORAGE_STATE } from '../../playwright.config';
import { UiHelpers } from '@umbraco/playwright-testhelpers';

setup('authenticate', async ({ page }) => {
  const umbracoUi = new UiHelpers(page);

  await umbracoUi.goToBackOffice();
  await umbracoUi.login.enterEmail(process.env.UMBRACO_USER_LOGIN || 'admin@test.com');
  await umbracoUi.login.enterPassword(process.env.UMBRACO_USER_PASSWORD || 'SecurePass1234');
  await umbracoUi.login.clickLoginButton();

  // Wait for backoffice to fully load after login
  await page.waitForURL(/\/umbraco\/section\//, { timeout: 30_000 });

  await page.context().storageState({ path: STORAGE_STATE });
});
