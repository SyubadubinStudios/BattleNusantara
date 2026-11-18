// Runs a hidden WPF process with a temporary WebView2 profile, then inspects its real DOM.
const { spawn } = require('node:child_process');
const { mkdir } = require('node:fs/promises');
const path = require('node:path');
const { chromium, expect } = require('@playwright/test');

// The CDP port defaults to 9223; set DESKTOP_SMOKE_PORT when another program already listens there.
const port = Number(process.env.DESKTOP_SMOKE_PORT) || 9223;
(async () => {
  await mkdir('artifacts', { recursive: true });
  const executable = path.resolve(process.argv[2] || 'src/BattleNusantara.Desktop/bin/Release/net10.0-windows10.0.19041.0/BattleNusantara.Desktop.exe');
  const child = spawn(executable, [], { windowsHide: true, env: { ...process.env, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: `--remote-debugging-port=${port}`, WEBVIEW2_USER_DATA_FOLDER: path.resolve(`artifacts/webview-test-${Date.now()}`) } });
  child.stdout.on('data', data => process.stdout.write(data));
  child.stderr.on('data', data => process.stderr.write(data));
  let browser;
  try {
    for (let i = 0; i < 40; i++) {
      try { browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`, { timeout: 2000 }); break; }
      catch { if (child.exitCode !== null) throw new Error(`Desktop exited: ${child.exitCode}`); await new Promise(r => setTimeout(r, 500)); }
    }
    if (!browser) throw new Error('WebView2 debugging endpoint did not start.');
    console.log('WebView pages:', browser.contexts().flatMap(c => c.pages()).map(p => p.url()));
    let page;
    for (let i = 0; i < 40; i++) {
      page = browser.contexts().flatMap(c => c.pages()).find(p => /0\.0\.0\.|\/\/localhost\/|^app:/.test(p.url())); // WPF BlazorWebView or Photino
      if (page) break;
      await new Promise(r => setTimeout(r, 500));
    }
    if (!page) throw new Error('Blazor WebView page not found: ' + browser.contexts().flatMap(c => c.pages()).map(p => p.url()).join(', '));
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    await expect(page.locator('.world')).toHaveAttribute('data-ready', 'true', { timeout: 30000 });
    await page.getByRole('button', { name: 'MULAI PETUALANGAN' }).click();
    await page.getByRole('button', { name: 'MAIN SOLO', exact: false }).click();
    await expect(page.locator('.battle-bottom')).toBeVisible();
    await expect(page.locator('.match-label')).not.toContainText('00:00', { timeout: 15000 });
    await page.getByRole('button', { name: /Rekrut/ }).click();
    await page.getByRole('button', { name: /Pilih pasukan/ }).click();
    await page.getByRole('button', { name: /Tahan posisi/ }).click();
    await expect(page.locator('.selection-summary')).toContainText('Tahan posisi');
    await page.getByRole('button', { name: /Latih pekerja/ }).click();
    await page.getByRole('button', { name: /Pilih pekerja/ }).click();
    await page.getByRole('button', { name: /Tambang 1/ }).click();
    await expect.poll(async () => Number(await page.locator('.collected-gold b').innerText()), { timeout: 20000 }).toBeGreaterThan(0);
    await page.getByRole('button', { name: /Bangun pos/ }).click();
    const map = page.locator('.tactical-map');
    const clickMap = async (x, z) => {
      const bounds = await map.boundingBox();
      await page.mouse.click(bounds.x + bounds.width * x / 100, bounds.y + bounds.height * z / 100);
    };
    await clickMap(28, 12);
    await expect(page.locator('.economy-panel')).toContainText('1 / 4');
    await page.getByRole('button', { name: /Pilih pekerja/ }).click();
    await page.getByRole('button', { name: /Kerjakan pos/ }).click();
    await expect(page.locator('.construction-site progress')).toHaveAttribute('value', '100', { timeout: 20000 });
    await page.getByRole('button', { name: /Pilih hero/ }).click();
    await expect(page.locator('.unit-roster').getByRole('button', { name: 'Hero 1', exact: true })).toHaveAttribute('aria-pressed', 'true');
    await clickMap(28, 12);
    await expect(page.getByRole('status')).toContainText('Perintah ditolak');
    await clickMap(36, 12);
    await expect.poll(async () => Math.abs(Number(await map.locator('circle[data-unit-id="1"]').getAttribute('cx')) - 36), { timeout: 15000 }).toBeLessThan(1);
    await expect(page.getByRole('button', { name: /Bangun pos/ })).toBeEnabled({ timeout: 15000 });
    await page.getByRole('button', { name: /Bangun pos/ }).click();
    await expect(page.getByRole('button', { name: /Bangun pos/ })).toHaveAttribute('aria-pressed', 'true');
    await clickMap(46, 12);
    await expect(page.locator('.construction-site')).toHaveCount(2);
    await page.getByRole('button', { name: /Batalkan pos/ }).click();
    await expect(page.locator('.construction-site')).toHaveCount(1);
    const commandBounds = await page.locator('.tactical-panel').boundingBox();
    const hudBounds = await page.locator('.battle-bottom').boundingBox();
    expect(commandBounds.y + commandBounds.height).toBeLessThanOrEqual(hudBounds.y);
    const economyBounds = await page.locator('.economy-panel').boundingBox();
    expect(economyBounds.y + economyBounds.height).toBeLessThanOrEqual(hudBounds.y);
    const resourceBounds = await page.locator('.resources').boundingBox();
    expect(economyBounds.y).toBeGreaterThanOrEqual(resourceBounds.y + resourceBounds.height + 8);
    await page.screenshot({ path: process.env.SMOKE_SCREENSHOT || 'docs/screenshots/desktop.png' });
    if (errors.length) throw new Error(errors.join('\n'));
    console.log('PASS: WPF + offline rendering + squad orders + worker deposits + construction + cancellation + dynamic outpost navigation.');
  } finally {
    await browser?.close();
    child.kill();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
