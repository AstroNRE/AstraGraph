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
  await expect(page.locator(".studio-frame-label")).toHaveText("Window");
  await expect(page.locator(".studio-zoom")).toContainText("100%");
  await expect(page.locator(".html-page").getByText("Window")).toBeVisible();
  await page.getByRole("button", { name: "Button", exact: true }).dragTo(page.locator(".ui-stage"));
  await expect(page.locator(".html-page").getByRole("button", { name: "Button" })).toBeVisible();
  await page.locator(".studio-layer").nth(1).click();
  await expect(page.locator(".studio-inspect-title")).toHaveText("Label");
  await page.locator(".studio-inspect").getByLabel("Text").fill("Inspect");
  await expect(page.locator(".html-page").getByText("Inspect")).toBeVisible();
  await expect(page.locator(".studio-inspect-title")).toHaveText("Label");
  await expect(page.locator(".html-page .h-se")).toBeVisible();
  await page.locator(".studio-inspect").getByRole("button", { name: "Code" }).click();
  await expect(page.getByLabel("Element CSS")).toBeVisible();
  await expect(page.getByRole("button", { name: "Open logic graph" })).toBeVisible();
  const handle = page.locator(".html-page .h-se");
  const box = await handle.boundingBox();
  if (!box) throw new Error("resize handle missing");
  await page.mouse.move(box.x + 2, box.y + 2);
  await page.mouse.down();
  await page.mouse.move(box.x + 80, box.y + 30);
  await page.mouse.up();
  await expect(page.locator(".html-page .astra-selected")).toHaveCSS("width", /\d+px/);
  await expect(page.locator(".studio-inspect-title")).toHaveText("Label");
  const label = page.locator(".html-page .astra-label");
  const before = await label.boundingBox();
  if (!before) throw new Error("label missing");
  await page.mouse.move(before.x + 12, before.y + 8);
  await page.mouse.down();
  await page.mouse.move(before.x + 36, before.y + 24);
  await page.mouse.up();
  const after = await label.boundingBox();
  if (!after) throw new Error("label lost");
  expect(Math.abs(after.y - before.y)).toBeLessThan(80);
  await expect(page.locator(".studio-frame-label")).toBeVisible();
  const width = page.locator(".studio-zoom input").first();
  await width.fill("700");
  await width.blur();
  await expect(page.locator(".ui-stage")).toHaveCSS("width", "700px");
});

test("sprites from build textures drop onto the canvas", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: "UI canvas" }).click();
  await page.getByRole("button", { name: "New UI" }).click();
  await page.getByRole("button", { name: "Sprites" }).click();
  await page.getByPlaceholder("Search sprites").fill("derelict1");
  const card = page.locator(".sprite-card").first();
  await expect(card).toBeVisible({ timeout: 20000 });
  await card.click();
  await expect(page.locator(".html-page .astra-sprite img")).toBeVisible();
  await expect(page.locator(".studio-inspect-title")).toHaveText("TextureRect");
});
