import { expect, test } from "@playwright/test";

test("studio shell loads without an authoring backend", async ({ page }) => {
  await page.goto("/");
  await expect(page).toHaveTitle("Astra Studio");
  await expect(page.getByRole("button", { name: "Publish" })).toBeVisible();
  await expect(page.getByText("Authoring backend unavailable")).toBeVisible();
});

test("design mode switches the graph canvas to the UI canvas", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: "UI canvas" }).click();
  await expect(page.getByRole("button", { name: "New UI" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Align left" })).toHaveCount(0);
  await page.getByRole("button", { name: "Graph canvas" }).click();
  await expect(page.getByRole("button", { name: "Align left" })).toBeVisible();
  await expect(page.getByRole("button", { name: "New UI" })).toHaveCount(0);
});

test("design canvas uses a frame, layers, and inspector", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: "UI canvas" }).click();
  await page.getByRole("button", { name: "New UI" }).click();
  await expect(page.getByText("Layers", { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Button" })).toBeVisible();
  await expect(page.locator(".figma-frame-label")).toHaveText("Window");
  await expect(page.locator(".figma-zoom")).toContainText("100%");
  await expect(page.frameLocator(".html-page").getByText("Window")).toBeVisible();
  await page.getByRole("button", { name: "Button", exact: true }).dragTo(page.locator(".ui-stage"));
  await expect(page.frameLocator(".html-page").getByRole("button", { name: "Button" })).toBeVisible();
  const width = page.locator(".figma-zoom input").first();
  await width.fill("700");
  await width.blur();
  await expect(page.locator(".ui-stage")).toHaveCSS("width", "700px");
});
