import { expect, test } from "@playwright/test";

test("create, compile, publish, and roll back a graph", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByRole("button", { name: "Publish" })).toBeVisible();
  await expect(page.getByRole("status")).toContainText("Connected");
  await page.getByRole("button", { name: "New graph" }).click();
  await page.locator(".dialog").getByLabel("Name").fill("Door flow");
  await page.getByRole("button", { name: "Create" }).click();
  await expect(page.getByRole("banner").getByText("Door flow")).toBeVisible({ timeout: 20_000 });
  await page.getByRole("button", { name: "Compile" }).click();
  await expect(page.getByText("SemanticHash").or(page.getByText("Completed"))).toBeVisible({ timeout: 20_000 });
  await page.getByRole("button", { name: "Publish" }).click();
  await page.locator(".dialog").getByRole("button", { name: "Publish" }).click();
  await expect(page.getByRole("button", { name: "Rollback" }).first()).toBeVisible({ timeout: 20_000 });
  await page.getByRole("button", { name: "Rollback" }).first().click();
});

test("debugger, designer, and permissions stay on the shell", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByRole("status")).toContainText("Connected");
  await page.getByRole("button", { name: "debug" }).click();
  await expect(page.getByRole("button", { name: "Step Out" })).toBeVisible();
  await page.getByRole("button", { name: "design" }).click();
  await page.getByRole("button", { name: "New UI" }).click();
  await expect(page.getByRole("button", { name: "Button" })).toBeVisible();
  await page.getByRole("button", { name: "access" }).click();
  await expect(page.getByText("PublishServer")).toBeVisible();
  await expect(page.getByText("Rollback")).toBeVisible();
});
