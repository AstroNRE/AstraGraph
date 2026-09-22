import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "e2e",
  testMatch: "live.spec.ts",
  timeout: 90_000,
  webServer: {
    command: "dotnet run --project ../AstraGraph.Studio.DevHost --configuration Release -p:RobustToolboxRoot=/workspaces/RobustToolbox -p:RobustToolsBuild=true -- --mode standalone --open-browser false --permissions publisher",
    port: 5173,
    timeout: 180_000,
    reuseExistingServer: true
  },
  use: {
    baseURL: "http://127.0.0.1:5173"
  }
});
