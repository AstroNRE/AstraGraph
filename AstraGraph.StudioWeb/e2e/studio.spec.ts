import { expect, test } from "@playwright/test";

test("studio shell loads without an authoring backend", async ({ page }) => {
  await page.goto("/");
  await expect(page).toHaveTitle("Astra Studio");
  await expect(page.getByRole("button", { name: "Publish" })).toBeVisible();
  await expect(page.getByText("Authoring backend unavailable")).toBeVisible();
});
