import { test as setup } from '@playwright/test';
import { STORAGE_STATE } from '../../playwright.config';
import { ConstantHelper, UiHelpers } from '@umbraco/playwright-testhelpers';

setup('authenticate', async ({ page }) => {
  const umbracoUi = new UiHelpers(page);

  await umbracoUi.goToBackOffice();
  await umbracoUi.login.enterEmail(process.env.UMBRACO_USER_LOGIN || 'admin@test.com');
  await umbracoUi.login.enterPassword(process.env.UMBRACO_USER_PASSWORD || 'Test12345678!');
  await umbracoUi.login.clickLoginButton();

  // Wait for backoffice to load by navigating to Settings
  await umbracoUi.login.goToSection(ConstantHelper.sections.settings);

  await page.context().storageState({ path: STORAGE_STATE });
});
