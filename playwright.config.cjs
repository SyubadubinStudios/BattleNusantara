const { defineConfig } = require('@playwright/test');
module.exports = defineConfig({
  testDir: './tests/browser', timeout: 90000, expect: { timeout: 15000 }, fullyParallel: false, workers: 1,
  use: { baseURL: 'http://localhost:5080', viewport: { width: 1440, height: 1000 }, channel: 'msedge', trace: 'retain-on-failure', launchOptions: { args: ['--enable-webgl', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] } },
  webServer: { command: 'dotnet run --project src/BattleNusantara.Server -c Release --no-launch-profile --urls http://localhost:5080', url: 'http://localhost:5080/health', reuseExistingServer: !process.env.CI, timeout: 120000 },
  reporter: 'list'
});
