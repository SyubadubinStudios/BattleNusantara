const { test, expect } = require('@playwright/test');

// The opening cinematic moves the camera for five seconds; scenarios that click the arena switch it off, as a player can in Settings.
const calm = page => page.addInitScript(() => { if (!localStorage.getItem('nusantara.settings')) localStorage.setItem('nusantara.settings', JSON.stringify({ language: 'id', quality: 'high', reducedMotion: false, volume: 20, cinematics: false })); });
async function ready(page, cinematics = false) {
  if (!cinematics) await calm(page);
  await page.goto('/');
  await expect(page.locator('.world')).toHaveAttribute('data-ready', 'true', { timeout: 30000 });
}
async function play(page, mode = 'RTS Skirmish') {
  await page.getByRole('button', { name: 'MULAI PETUALANGAN' }).click();
  await page.getByRole('button', { name: mode, exact: true }).click();
  await page.getByRole('button', { name: 'MAIN SOLO', exact: false }).click();
  await expect(page.locator('.battle-bottom')).toBeVisible();
}

test('RTS foundations can be cancelled and workers complete queued sites', async ({ page }) => {
  await ready(page); await play(page);
  const map = page.locator('.tactical-map');
  const place = async (x, z) => {
    const build = page.getByRole('button', { name: /Bangun pos/ });
    await expect(build).toBeEnabled({ timeout: 20000 });
    await build.click();
    await expect(build).toHaveAttribute('aria-pressed', 'true');
    const bounds = await map.boundingBox();
    await page.mouse.click(bounds.x + bounds.width * x / 100, bounds.y + bounds.height * z / 100);
  };
  await place(30, 18);
  await expect(page.getByRole('button', { name: /Batalkan pos.*75/ })).toBeVisible();
  await page.getByRole('button', { name: /Batalkan pos/ }).click();
  await expect(page.locator('.construction-site')).toHaveCount(0);
  await place(30, 18);
  await place(10, 32); // Explored ground: outposts cannot be founded in unexplored fog.
  await expect(page.locator('.construction-site')).toHaveCount(2);
  const hire = page.getByRole('button', { name: /Latih pekerja/ });
  await expect(hire).toBeEnabled({ timeout: 20000 });
  await hire.click();
  await page.getByRole('button', { name: /Pilih pekerja/ }).click();
  await page.getByRole('button', { name: /Kerjakan pos/ }).first().click();
  await expect(page.locator('.construction-site progress').first()).toHaveAttribute('value', '100', { timeout: 20000 });
  await expect(page.locator('.construction-site progress').last()).toHaveAttribute('value', '100', { timeout: 20000 });
  await expect(page.getByRole('button', { name: /Batalkan pos/ })).toHaveCount(0);
  await page.screenshot({ path: 'docs/screenshots/rts-construction-queue.png' });
});

test('RTS construction requires workers, pauses and resumes to a usable outpost', async ({ page }) => {
  await ready(page); await play(page);
  await page.keyboard.press('b');
  const map = page.locator('.tactical-map');
  const bounds = await map.boundingBox();
  await page.mouse.click(bounds.x + bounds.width * .30, bounds.y + bounds.height * .18);
  const site = page.locator('.construction-site');
  const progress = site.locator('progress');
  await expect(progress).toHaveAttribute('value', '0');
  await page.getByRole('button', { name: /Latih pekerja/ }).click();
  await page.getByRole('button', { name: /Pilih pekerja/ }).click();
  await site.getByRole('button', { name: /Kerjakan pos/ }).click();
  await expect.poll(async () => Number(await progress.getAttribute('value')), { timeout: 15000 }).toBeGreaterThan(10);
  await page.getByRole('button', { name: /Tahan posisi/ }).click();
  await expect(page.locator('.selection-summary')).toContainText('Tahan posisi');
  const paused = await progress.getAttribute('value');
  await page.waitForTimeout(1200);
  await expect(progress).toHaveAttribute('value', paused);
  await site.getByRole('button', { name: /Kerjakan pos/ }).click();
  await expect(progress).toHaveAttribute('value', '100', { timeout: 15000 });
  await expect(site).toContainText('Siap');
  await page.screenshot({ path: 'docs/screenshots/rts-construction.png' });
  await page.setViewportSize({ width: 390, height: 844 });
  const tactical = await page.locator('.tactical-panel').boundingBox();
  const economy = await page.locator('.economy-panel').boundingBox();
  const hud = await page.locator('.battle-bottom').boundingBox();
  expect(tactical.y + tactical.height).toBeLessThanOrEqual(economy.y);
  expect(economy.y + economy.height).toBeLessThanOrEqual(hud.y);
  await page.screenshot({ path: 'docs/screenshots/construction-mobile.png', fullPage: true });
});

test('RTS outposts block movement and units navigate around the new footprint', async ({ page }) => {
  await ready(page); await play(page);
  const map = page.locator('.tactical-map');
  const bounds = await map.boundingBox();
  const point = (x, z) => ({ position: { x: bounds.width * x / 100, y: bounds.height * z / 100 } });
  await page.keyboard.press('b');
  await map.click(point(30, 18));
  await expect(page.locator('.economy-panel')).toContainText('1 / 4');
  await page.keyboard.press('1');
  await map.click(point(30, 18));
  await expect(page.getByRole('status')).toContainText('Perintah ditolak');
  await map.click(point(36, 12));
  const hero = map.locator('circle[data-unit-id="1"]');
  await expect.poll(async () => Math.abs(Number(await hero.getAttribute('cx')) - 36)).toBeLessThan(1);
  await expect.poll(async () => Math.abs(Number(await hero.getAttribute('cy')) - 12)).toBeLessThan(1);
  await page.screenshot({ path: 'docs/screenshots/rts-navigation.png' });
});
test('menu renders 3D, hero selection, language persists, credits and mobile layout', async ({ page }) => {
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await ready(page);
  await page.screenshot({ path: 'docs/screenshots/main-menu.png' });
  await page.getByRole('button', { name: 'PAHLAWAN', exact: true }).click();
  await page.getByRole('button', { name: /Nyi Roro Kidul.*Mystic|Mitologi.*Nyi Roro Kidul/ }).click();
  await expect(page.locator('.hero-card.selected')).toContainText('Nyi Roro Kidul');
  await page.screenshot({ path: 'docs/screenshots/heroes.png' });
  await page.getByRole('button', { name: /Pengaturan/ }).click();
  await page.getByLabel('Bahasa', { exact: true }).selectOption('en');
  await page.getByLabel('Reduce motion').check();
  await page.getByRole('button', { name: 'Close', exact: true }).click();
  await page.reload(); await expect(page.getByRole('button', { name: 'HEROES', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Credits' }).click();
  await expect(page.locator('.credits-roll')).toContainText('Ariana Mischa Fadhila');
  await page.getByRole('button', { name: 'Close', exact: true }).click();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: 'docs/screenshots/mobile.png', fullPage: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  expect(errors).toEqual([]);
});
test('RTS economy, movement, ultimate, pause and return to menu', async ({ page }) => {
  await ready(page); await play(page);
  await page.getByRole('button', { name: /Rekrut/ }).click();
  await page.getByRole('button', { name: /Bangun pos/ }).click();
  const placementMap = page.locator('.tactical-map'); const placementBounds = await placementMap.boundingBox();
  await placementMap.click({ position: { x: placementBounds.width * .28, y: placementBounds.height * .12 } });
  await expect(page.locator('.economy-panel')).toContainText('1 / 4');
  await expect(page.getByRole('button', { name: /Rekrut/ })).toBeDisabled();
  const before = await page.locator('.hero-hud small').innerText();
  await page.locator('canvas').click({ position: { x: 720, y: 450 } });
  await page.keyboard.press('q');
  await expect(page.locator('.ability').first()).toContainText(/\ds/);
  await page.screenshot({ path: 'docs/screenshots/rts-battle.png' });
  await page.keyboard.press('Escape');
  await expect(page.getByText('Permainan dijeda.', { exact: true })).toBeVisible();
  const clock = await page.locator('.match-label').innerText();
  await page.waitForTimeout(500);
  expect(await page.locator('.match-label').innerText()).toBe(clock);
  await page.getByRole('button', { name: 'Akhiri permainan', exact: true }).click();
  await expect(page.getByRole('button', { name: 'MULAI PETUALANGAN' })).toBeVisible();
  expect(before).toContain('HP');
});
test('Adventure and MOBA expose their own objectives', async ({ page }) => {
  await ready(page); await play(page, 'Adventure');
  await expect(page.getByRole('heading', { name: 'Pulihkan tiga prasasti' })).toBeVisible();
  await expect(page.locator('.resources')).toContainText('0 / 3');
  await expect(page.getByRole('button', { name: /Rekrut/ })).toHaveCount(0);
  await page.screenshot({ path: 'docs/screenshots/adventure.png' });
  await page.getByRole('button', { name: 'Menu utama', exact: true }).click();
  await play(page, 'MOBA Arena');
  await expect(page.locator('.match-label')).toContainText('MOBA');
});

test('MOBA shows towers, sells levelled items and respawns the fallen hero', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('nusantara.settings', JSON.stringify({ language: 'id', quality: 'low', reducedMotion: true, volume: 0 })));
  await ready(page); await play(page, 'MOBA Arena');
  await expect(page.getByRole('heading', { name: 'Pertahankan Jagat Nusantara' })).toBeVisible();
  await expect(page.locator('.objective')).toContainText('menara');
  await expect(page.locator('.tower-count')).toContainText('3 – 3');
  await expect(page.getByRole('button', { name: /Rekrut/ })).toHaveCount(0);
  const gold = async () => Number(await page.locator('.resources b').first().innerText());
  const sandals = page.locator('[data-item="2"]');
  await expect(sandals).toContainText('100 ◈ · Lv 0');
  const before = await gold();
  await sandals.click();
  await expect(sandals).toContainText('200 ◈ · Lv 1');
  expect(await gold()).toBeLessThan(before);
  await expect(page.locator('[data-item="0"]')).toBeDisabled();
  await expect(page.locator('[data-item="1"]')).toBeDisabled();
  await page.screenshot({ path: 'docs/screenshots/moba.png' });

  // Walk into the enemy base: the hero falls, the match continues, and the hero returns.
  const point = await page.evaluate(() => {
    for (const [x, y] of [[760, 640], [740, 610], [780, 660], [720, 600]]) if (document.elementFromPoint(x, y)?.tagName === 'CANVAS') return [x, y];
    return null;
  });
  expect(point).not.toBeNull();
  const health = page.locator('.hero-health');
  await expect.poll(async () => { await page.mouse.click(point[0], point[1]); return health.innerText(); }, { timeout: 60000, intervals: [1500] }).toContain('Bangkit dalam');
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await page.screenshot({ path: 'docs/screenshots/moba-respawn.png' });
  await expect(health).toContainText('HP', { timeout: 20000 });
  await expect(page.locator('.match-label')).toContainText('MOBA');
});
test('two clients join a real SignalR room and receive the same simulation', async ({ page, browser }) => {
  await page.addInitScript(() => localStorage.setItem('nusantara.settings', JSON.stringify({ language: 'id', quality: 'low', reducedMotion: true, volume: 0 })));
  await ready(page);
  await page.getByRole('button', { name: 'MULAI PETUALANGAN' }).click();
  await page.locator('summary', { hasText: 'Bermain bersama' }).click();
  await page.getByRole('button', { name: 'Buat / gabung ruang' }).click();
  await expect(page.locator('.match-label b')).toHaveText(/ROOM [0-9A-F]{6}/);
  const code = (await page.locator('.match-label b').innerText()).split(' ')[1];
  const context = await browser.newContext(); const guest = await context.newPage();
  await guest.addInitScript(() => localStorage.setItem('nusantara.settings', JSON.stringify({ language: 'id', quality: 'low', reducedMotion: true, volume: 0 })));
  await ready(guest); await guest.getByRole('button', { name: 'MULAI PETUALANGAN' }).click();
  await guest.locator('summary', { hasText: 'Bermain bersama' }).click(); await guest.getByLabel('Kode ruang (kosongkan untuk membuat)').fill(code);
  await guest.getByRole('button', { name: 'Buat / gabung ruang' }).click();
  await expect(guest.locator('.match-label b')).toHaveText(`ROOM ${code}`);
  await page.getByRole('button', { name: /Upgrade/ }).click();
  await expect(guest.locator('.resources')).toContainText('Level 2');
  await guest.keyboard.press('q');
  await expect(guest.locator('.ability').first()).toContainText(/\ds/);
  await expect(page.locator('.ability').first()).toContainText('Sumpah Palapa');
  await page.getByRole('button', { name: /Rekrut/ }).click();
  await expect(page.locator('.unit-roster').getByRole('button', { name: /Prajurit/ })).toBeVisible();
  await page.keyboard.press('2');
  await expect(page.locator('.unit-roster').getByRole('button', { name: /Prajurit/ })).toHaveAttribute('aria-pressed', 'true');
  await page.keyboard.press('h');
  await expect(page.locator('.selection-summary')).toContainText('Tahan posisi');
  await expect(guest.locator('.unit-roster')).not.toContainText('Prajurit');
  await expect(guest.getByRole('button', { name: /Pilih pasukan/ })).toBeDisabled();
  await page.getByRole('button', { name: /Latih pekerja/ }).click();
  await page.getByRole('button', { name: /Pilih pekerja/ }).click();
  await page.getByRole('button', { name: /Tambang 1/ }).click();
  await expect.poll(async () => Number(await guest.locator('.collected-gold b').innerText()), { timeout: 20000 }).toBeGreaterThan(0);
  await expect(guest.getByRole('button', { name: /Pilih pekerja/ })).toBeDisabled();
  await context.close();
});

test('RTS squad selection, map orders, blocked terrain and hold position', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('nusantara.settings', JSON.stringify({ language: 'id', quality: 'low', reducedMotion: true, volume: 0 })));
  await ready(page); await play(page);
  await page.getByRole('button', { name: /Rekrut/ }).click();
  await page.getByRole('button', { name: /Rekrut/ }).click();
  await page.keyboard.press('2');
  await expect(page.locator('.selection-summary strong')).toHaveText('2');
  await page.keyboard.press('h');
  await expect(page.locator('.selection-summary')).toContainText('Tahan posisi');
  const map = page.locator('.tactical-map');
  const rect = await map.boundingBox();
  await map.click({ position: { x: rect.width * .3, y: rect.height * .2 } });
  await expect(page.locator('.selection-summary')).toContainText('Bergerak');
  const selected = map.locator('circle[stroke="#ffffff"]');
  await expect(selected).toHaveCount(2);
  await expect.poll(async () => Number(await selected.first().getAttribute('cx'))).toBeGreaterThan(26);
  await expect(map.locator('circle[data-unit-id="1"]')).toHaveAttribute('cx', '24');
  await map.click({ position: { x: rect.width * .44, y: rect.height * .4 } });
  await expect(page.getByRole('status')).toContainText('Perintah ditolak');
  await page.keyboard.press('a');
  await expect(page.getByRole('button', { name: /Serang-bergerak/ })).toHaveAttribute('aria-pressed', 'true');
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.getByRole('button', { name: /Serang-bergerak/ })).toHaveAttribute('aria-pressed', 'false');
  await page.keyboard.press('a');
  await map.click({ position: { x: rect.width * .7, y: rect.height * .35 } });
  await expect(page.locator('.selection-summary')).toContainText('Serang-bergerak');
  await page.keyboard.press('h');
  await expect(page.locator('.selection-summary')).toContainText('Tahan posisi');
  await page.screenshot({ path: 'docs/screenshots/rts-tactics.png' });
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.getByRole('button', { name: /Pilih pasukan/ })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  const panelBounds = await page.locator('.tactical-panel').boundingBox();
  const hudBounds = await page.locator('.battle-bottom').boundingBox();
  expect(panelBounds.y + panelBounds.height).toBeLessThanOrEqual(hudBounds.y);
  const economyBounds = await page.locator('.economy-panel').boundingBox();
  expect(panelBounds.y + panelBounds.height).toBeLessThanOrEqual(economyBounds.y);
  expect(economyBounds.y + economyBounds.height).toBeLessThanOrEqual(hudBounds.y);
  await page.screenshot({ path: 'docs/screenshots/rts-mobile.png', fullPage: true });
});

test('RTS workers gather finite resources and player places a drop-off outpost', async ({ page }) => {
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  await page.addInitScript(() => localStorage.setItem('nusantara.settings', JSON.stringify({ language: 'id', quality: 'low', reducedMotion: true, volume: 0 })));
  await ready(page); await play(page);
  await page.keyboard.press('b');
  await expect(page.getByRole('button', { name: /Bangun pos/ })).toHaveAttribute('aria-pressed', 'true');
  const map = page.locator('.tactical-map'); const rect = await map.boundingBox();
  await map.click({ position: { x: rect.width * .44, y: rect.height * .4 } });
  await expect(page.getByRole('status')).toContainText('Perintah ditolak');
  await expect(page.locator('.economy-panel')).toContainText('0 / 4');
  await map.click({ position: { x: rect.width * .3, y: rect.height * .32 } });
  await expect(page.locator('.economy-panel')).toContainText('1 / 4');
  await expect(page.getByRole('button', { name: /Bangun pos/ })).toHaveAttribute('aria-pressed', 'false');
  await page.getByRole('button', { name: /Latih pekerja/ }).click();
  await expect(page.locator('.unit-roster')).toContainText('Pekerja');
  await page.keyboard.press('3');
  await page.getByRole('button', { name: /Tambang 1/ }).click();
  await expect(page.locator('.selection-summary')).toContainText('Mengumpulkan');
  await expect.poll(async () => Number(await page.locator('.collected-gold b').innerText()), { timeout: 20000 }).toBeGreaterThan(0);
  await expect(page.getByRole('button', { name: /Tambang 1/ })).not.toContainText('120');
  await page.screenshot({ path: 'docs/screenshots/rts-economy.png' });
  await page.setViewportSize({ width: 390, height: 844 });
  const tactical = await page.locator('.tactical-panel').boundingBox();
  const economy = await page.locator('.economy-panel').boundingBox();
  const hud = await page.locator('.battle-bottom').boundingBox();
  expect(tactical.y + tactical.height).toBeLessThanOrEqual(economy.y);
  expect(economy.y + economy.height).toBeLessThanOrEqual(hud.y);
  await page.screenshot({ path: 'docs/screenshots/economy-mobile.png', fullPage: true });
  expect(errors).toEqual([]);
});

test('RTS drag selection, additive boxes and saved control groups', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('nusantara.settings', JSON.stringify({ language: 'id', quality: 'low', reducedMotion: true, volume: 0 })));
  await ready(page); await play(page);
  await page.getByRole('button', { name: /Rekrut/ }).click();
  await page.getByRole('button', { name: /Rekrut/ }).click();
  const summary = page.locator('.selection-summary strong');
  await expect(page.locator('.unit-roster button')).toHaveCount(3);
  await expect(summary).toHaveText('1');
  const onCanvas = point => page.evaluate(([x, y]) => document.elementFromPoint(x, y)?.tagName, point);
  const drag = async (from, to, modifier) => {
    expect(await onCanvas(from)).toBe('CANVAS');
    if (modifier) await page.keyboard.down(modifier);
    await page.mouse.move(from[0], from[1]); await page.mouse.down();
    await page.mouse.move((from[0] + to[0]) / 2, (from[1] + to[1]) / 2); await page.mouse.move(to[0], to[1]);
    await expect(page.locator('.selection-box')).toBeVisible();
  };
  const release = async modifier => { await page.mouse.up(); if (modifier) await page.keyboard.up(modifier); await expect(page.locator('.selection-box')).toBeHidden(); };

  // A box around the whole arena selects every owned unit, and nothing else.
  await drag([320, 110], [1130, 800]);
  await release();
  await expect(summary).toHaveText('3');
  await page.keyboard.press('Control+4');
  await expect(page.getByRole('status')).toContainText('Grup 4 disimpan');
  await expect(page.locator('[data-group="4"]')).toContainText('3');

  await page.keyboard.press('2');
  await expect(summary).toHaveText('2');
  await page.keyboard.press('Alt+5');
  await expect(page.locator('[data-group="5"]')).toContainText('2');
  await page.keyboard.press('1');
  await expect(summary).toHaveText('1');
  await page.keyboard.press('4');
  await expect(summary).toHaveText('3');
  await page.keyboard.press('5');
  await expect(summary).toHaveText('2');
  await expect(page.locator('[data-group="6"]')).toBeDisabled();
  await page.keyboard.press('6');
  await expect(page.getByRole('status')).toContainText('Grup 6 kosong');
  await expect(summary).toHaveText('2');

  // An empty box keeps the current selection; Shift adds to it.
  await drag([1000, 700], [1100, 790]);
  await release();
  await expect(page.getByRole('status')).toContainText('Tidak ada unit milikmu');
  await expect(summary).toHaveText('2');
  await drag([320, 110], [1130, 800], 'Shift');
  await page.screenshot({ path: 'docs/screenshots/rts-control-groups.png' });
  await release('Shift');
  await expect(summary).toHaveText('3');

  // A press without movement is still an ordinary move order.
  // Ruins reject orders, so try a few screen points until one lands on open ground.
  for (const [x, y] of [[700, 500], [620, 330], [820, 420], [560, 600]]) {
    expect(await onCanvas([x, y])).toBe('CANVAS');
    await page.mouse.click(x, y);
    await expect(page.getByRole('status')).toContainText(/Perintah (diterima|ditolak)/);
    if ((await page.getByRole('status').textContent()).includes('diterima')) break;
    await page.keyboard.press('4');
    await expect(page.getByRole('status')).not.toContainText('Perintah');
  }
  await expect(page.locator('.selection-summary')).toContainText('Bergerak');
  await page.getByRole('button', { name: 'Simpan grup 6' }).click();
  await expect(page.locator('[data-group="6"]')).toContainText('3');
});

test('RTS fog of war hides distant enemies and foundations can be prioritised', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('nusantara.settings', JSON.stringify({ language: 'id', quality: 'low', reducedMotion: true, volume: 0 })));
  await ready(page); await play(page);
  const map = page.locator('.tactical-map');
  const enemies = map.locator('circle[data-unit-id][fill="#d17791"]');
  await expect(page.locator('.world')).toHaveAttribute('data-fog', 'on');
  await expect(map.locator('.map-fog')).toHaveCount(1);
  await expect(map.locator('circle[data-unit-id]')).toHaveCount(1);
  await expect(enemies).toHaveCount(0);
  // The first wave walks from the enemy gate and appears only once it enters allied vision.
  await expect.poll(() => enemies.count(), { timeout: 45000 }).toBeGreaterThan(0);
  const near = await enemies.first().evaluate(node => [Number(node.getAttribute('cx')), Number(node.getAttribute('cy'))]);
  const sources = await map.locator('mask circle').evaluateAll(nodes => nodes.map(n => [Number(n.getAttribute('cx')), Number(n.getAttribute('cy')), Number(n.getAttribute('r'))]));
  expect(sources.some(([x, z, r]) => Math.hypot(near[0] - x, near[1] - z) <= r + .01)).toBe(true);
  await page.screenshot({ path: 'docs/screenshots/rts-fog.png' });

  // Two unstaffed foundations: the later one can be moved to the front of the queue.
  await expect.poll(async () => Number(await page.locator('.resources b').first().innerText()), { timeout: 30000 }).toBeGreaterThanOrEqual(200);
  const place = async (x, z) => {
    await page.keyboard.press('b');
    const bounds = await map.boundingBox();
    await page.mouse.click(bounds.x + bounds.width * x / 100, bounds.y + bounds.height * z / 100);
  };
  await place(28, 12);
  await expect(page.locator('.construction-site')).toHaveCount(1);
  await place(10, 32); // Explored ground: outposts cannot be founded in unexplored fog.
  const sites = page.locator('.construction-site');
  await expect(sites).toHaveCount(2);
  await expect(sites.first()).toContainText('#1');
  await expect(sites.last()).toContainText('#2');
  const later = await sites.last().getAttribute('data-site-id');
  await expect(page.getByRole('button', { name: /Dahulukan pos/ })).toHaveCount(1);
  await page.getByRole('button', { name: /Dahulukan pos/ }).click();
  await expect(sites.first()).toHaveAttribute('data-site-id', later);
  await expect(sites.first()).toContainText('#1');
  await expect(sites.first().getByRole('button', { name: /Dahulukan pos/ })).toHaveCount(0);
  await expect(sites.last().getByRole('button', { name: /Dahulukan pos/ })).toHaveCount(1);
  await page.locator('.construction-list').scrollIntoViewIfNeeded();
  await page.screenshot({ path: 'docs/screenshots/rts-queue-priority.png' });
});


const quiet = page => page.addInitScript(() => { if (!localStorage.getItem('nusantara.settings')) localStorage.setItem('nusantara.settings', JSON.stringify({ language: 'id', quality: 'low', reducedMotion: true, volume: 0 })); });
const rendererStats = page => page.evaluate(async () => (await import(new URL('_content/BattleNusantara.UI/world.js', document.baseURI).href)).stats());

test('Adventure shows quests and dialogue, guards, saves and resumes the run', async ({ page }) => {
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await quiet(page);
  await ready(page); await play(page, 'Adventure');
  const log = page.locator('.quest-log');
  await expect(log.locator('li')).toHaveCount(6); // Five main quests and the optional side quest.
  await expect(log.locator('li.side-quest')).toHaveAttribute('data-quest', 'nakhoda');
  await expect(log.locator('li[data-quest="juru-kunci"]')).toHaveAttribute('data-state', 'Active');
  await expect(log.locator('li[data-quest="prasasti"]')).toContainText('0/3');
  await expect(log.locator('li[data-quest="kalatidha"]')).toHaveAttribute('data-state', 'Locked');
  const dialogue = page.locator('.dialogue');
  await expect(dialogue).toContainText('Narator');
  await expect(dialogue).toContainText('Tahun 2125');
  await page.screenshot({ path: 'docs/screenshots/adventure-quests.png' });
  await dialogue.getByRole('button', { name: /Lanjut/ }).click();
  await expect(dialogue).toContainText('juru kunci');
  await dialogue.getByRole('button', { name: /Lanjut/ }).click();
  await expect(dialogue).toHaveCount(0);
  // The quest log stays clear of the HUD.
  const logBounds = await log.boundingBox(), hud = await page.locator('.battle-bottom').boundingBox();
  expect(logBounds.y + logBounds.height).toBeLessThanOrEqual(hud.y);

  // Walk to the keeper: the first quest completes and the keeper speaks.
  const keeper = log.locator('li[data-quest="juru-kunci"]');
  const spot = await page.evaluate(async () => (await import(new URL('_content/BattleNusantara.UI/world.js', document.baseURI).href)).screenPoint(30, 22));
  expect(await page.evaluate(([x, y]) => document.elementFromPoint(x, y)?.tagName, spot)).toBe('CANVAS');
  await page.mouse.click(spot[0], spot[1]);
  await expect(keeper).toHaveAttribute('data-state', 'Done', { timeout: 30000 });
  await expect(log.locator('li[data-quest="penjaga"]')).toHaveAttribute('data-state', 'Active');
  await expect(page.locator('.dialogue')).toContainText('Juru Kunci');

  const guard = page.locator('[data-ability="guard"]');
  await expect(guard).toContainText('Tameng');
  await page.keyboard.press('w');
  await expect(guard).toContainText(/\ds/);
  await expect(guard).toBeDisabled();

  await page.keyboard.press('Escape');
  await page.getByRole('button', { name: 'Simpan permainan', exact: true }).click();
  await expect(page.locator('.save-notice')).toContainText('tersimpan');
  await page.screenshot({ path: 'docs/screenshots/adventure-save.png' });
  await page.getByRole('button', { name: 'Akhiri permainan', exact: true }).click();
  await page.reload();
  await expect(page.locator('.world')).toHaveAttribute('data-ready', 'true', { timeout: 30000 });
  await page.getByRole('button', { name: 'MULAI PETUALANGAN' }).click();
  await page.getByRole('button', { name: 'Lanjutkan simpanan Adventure' }).click();
  await expect(page.locator('.quest-log li[data-quest="juru-kunci"]')).toHaveAttribute('data-state', 'Done');
  await expect(page.locator('.match-label')).toContainText('ADVENTURE');
  await expect(page.locator('.match-label')).not.toContainText('00:00');

  // A damaged save is refused and removed instead of starting a broken match.
  await page.getByRole('button', { name: 'Menu utama', exact: true }).click();
  await page.evaluate(() => localStorage.setItem('nusantara.save', '{"Version":1,"Hero":"nobody"}'));
  await page.reload();
  await expect(page.locator('.world')).toHaveAttribute('data-ready', 'true', { timeout: 30000 });
  await page.getByRole('button', { name: 'MULAI PETUALANGAN' }).click();
  await page.getByRole('button', { name: 'Lanjutkan simpanan Adventure' }).click();
  await expect(page.getByRole('alert')).toContainText('Simpanan rusak');
  await expect(page.getByRole('button', { name: 'Lanjutkan simpanan Adventure' })).toHaveCount(0);
  expect(await page.evaluate(() => localStorage.getItem('nusantara.save'))).toBeNull();
  expect(errors).toEqual([]);
});

test('a spectator watches a LAN room without a seat or orders', async ({ page, browser }) => {
  await quiet(page);
  await ready(page);
  await page.getByRole('button', { name: 'MULAI PETUALANGAN' }).click();
  await page.locator('summary', { hasText: 'Bermain bersama' }).click();
  await expect(page.getByRole('button', { name: /Tonton ruang/ })).toBeDisabled();
  await page.getByRole('button', { name: 'Buat / gabung ruang' }).click();
  await expect(page.locator('.match-label b').first()).toHaveText(/ROOM [0-9A-F]{6}/);
  const code = (await page.locator('.match-label b').first().innerText()).split(' ')[1];
  const context = await browser.newContext(); const viewer = await context.newPage();
  await quiet(viewer);
  await ready(viewer); await viewer.getByRole('button', { name: 'MULAI PETUALANGAN' }).click();
  await viewer.locator('summary', { hasText: 'Bermain bersama' }).click();
  await viewer.getByLabel('Kode ruang (kosongkan untuk membuat)').fill('ZZZZZZ');
  await viewer.getByRole('button', { name: /Tonton ruang/ }).click();
  await expect(viewer.getByRole('alert')).toContainText('Tidak dapat menonton');
  await viewer.getByLabel('Kode ruang (kosongkan untuk membuat)').fill(code);
  await viewer.getByRole('button', { name: /Tonton ruang/ }).click();
  await expect(viewer.locator('.spectator-tag')).toHaveText('PENONTON');
  await expect(viewer.locator('.hero-hud.spectator')).toContainText(code);
  await expect(viewer.locator('.abilities')).toHaveCount(0);
  await expect(viewer.locator('.tactical-panel')).toHaveCount(0);
  // The spectator sees what the host does but cannot act: its Q leaves the host's ultimate ready.
  await viewer.keyboard.press('q');
  await page.getByRole('button', { name: /Upgrade/ }).click();
  await expect(viewer.locator('.resources')).toContainText('Level 2');
  await expect(page.locator('.ability').first()).toContainText('Sumpah Palapa');
  const clock = await viewer.locator('.match-label').innerText();
  await expect.poll(() => viewer.locator('.match-label').innerText()).not.toBe(clock);
  await viewer.screenshot({ path: 'docs/screenshots/lan-spectator.png' });
  // Both player seats remain available while someone watches.
  const second = await browser.newContext(); const guest = await second.newPage();
  await quiet(guest);
  await ready(guest); await guest.getByRole('button', { name: 'MULAI PETUALANGAN' }).click();
  await guest.locator('summary', { hasText: 'Bermain bersama' }).click(); await guest.getByLabel('Kode ruang (kosongkan untuk membuat)').fill(code);
  await guest.getByRole('button', { name: 'Buat / gabung ruang' }).click();
  await expect(guest.locator('.match-label b').first()).toHaveText(`ROOM ${code}`);
  await expect(guest.locator('.abilities')).toBeVisible();
  await second.close(); await context.close();
});

test('MOBA shows hero experience and the renderer stays inside its draw budget', async ({ page }) => {
  await quiet(page);
  await ready(page); await play(page, 'MOBA Arena');
  await expect(page.locator('progress.xp')).toHaveAttribute('max', '100');
  await expect(page.locator('[data-ability="guard"]')).toBeEnabled();
  // Let three waves, ten champions, six towers and the boss all be on screen.
  await expect(page.locator('.match-label')).toContainText(/00:[45]\d/, { timeout: 70000 });
  const stats = await rendererStats(page);
  console.log('RENDER moba', JSON.stringify(stats));
  expect(stats.units).toBeGreaterThan(20);
  expect(stats.calls).toBeLessThan(400);
  expect(stats.triangles).toBeLessThan(150000);
});

const openAccount = async page => {
  await page.getByRole('button', { name: 'MULAI PETUALANGAN' }).click();
  const account = page.locator('details.account');
  if (!(await account.evaluate(node => node.open))) await account.locator('summary').click();
  return account;
};
const uniqueName = prefix => (prefix + Date.now().toString(36) + Math.floor(Math.random() * 1296).toString(36)).slice(0, 20);

test('an account registers, survives a reload, signs out and signs back in', async ({ page }) => {
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await quiet(page);
  await ready(page);
  const name = uniqueName('Ana');
  const account = await openAccount(page);
  await account.getByLabel(/Nama akun/).fill(name);
  await account.getByLabel(/Kata sandi/).fill('short');
  await account.getByRole('button', { name: 'Daftar akun baru' }).click();
  await expect(page.getByRole('alert')).toContainText('8–128');
  await expect(account.getByLabel(/Kata sandi/)).toHaveValue('');
  await account.getByLabel(/Kata sandi/).fill('kata-sandi-1');
  await account.getByRole('button', { name: 'Daftar akun baru' }).click();
  await expect(account.locator('.account-summary')).toContainText(name);
  await expect(account.locator('.account-summary')).toContainText('0 pertandingan');
  await expect(account.getByRole('button', { name: /Cari pertandingan/ })).toBeEnabled();
  await page.screenshot({ path: 'docs/screenshots/account.png' });
  // The password never reaches browser storage; only the session token does.
  const stored = await page.evaluate(() => JSON.stringify(localStorage));
  expect(stored).not.toContain('kata-sandi-1');
  expect(stored).toContain('nusantara.session');

  await page.reload();
  await expect(page.locator('.world')).toHaveAttribute('data-ready', 'true', { timeout: 30000 });
  const restored = await openAccount(page);
  await expect(restored.locator('.account-summary')).toContainText(name);
  await restored.getByRole('button', { name: 'Keluar dari akun' }).click();
  await expect(restored.getByLabel(/Nama akun/)).toBeVisible();
  expect(await page.evaluate(() => localStorage.getItem('nusantara.session'))).toBeNull();

  await restored.getByLabel(/Nama akun/).fill(name);
  await restored.getByLabel(/Kata sandi/).fill('kata-sandi-salah');
  await restored.getByRole('button', { name: 'Masuk', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('salah');
  await restored.getByLabel(/Kata sandi/).fill('kata-sandi-1');
  await restored.getByRole('button', { name: 'Masuk', exact: true }).click();
  await expect(restored.locator('.account-summary')).toContainText(name);
  // Registering the same name again is refused.
  await restored.getByRole('button', { name: 'Keluar dari akun' }).click();
  await restored.getByLabel(/Nama akun/).fill(name.toUpperCase());
  await restored.getByLabel(/Kata sandi/).fill('kata-sandi-2');
  await restored.getByRole('button', { name: 'Daftar akun baru' }).click();
  await expect(page.getByRole('alert')).toContainText('sudah dipakai');
  expect(errors).toEqual([]);
});

test('matchmaking seats two signed-in players in one room', async ({ page, browser }) => {
  await quiet(page);
  await ready(page);
  const register = async (target, name) => {
    const account = await openAccount(target);
    await account.getByLabel(/Nama akun/).fill(name);
    await account.getByLabel(/Kata sandi/).fill('kata-sandi-1');
    await account.getByRole('button', { name: 'Daftar akun baru' }).click();
    await expect(account.locator('.account-summary')).toContainText(name);
    return account;
  };
  const first = await register(page, uniqueName('Budi'));
  await page.getByRole('button', { name: 'MOBA Arena', exact: true }).click();
  await first.getByRole('button', { name: /Cari pertandingan · MOBA Arena/ }).click();
  await expect(first.locator('.queue-status')).toContainText('1 / 2');
  await page.screenshot({ path: 'docs/screenshots/matchmaking.png' });
  // Cancelling leaves the queue; searching again re-enters it.
  await first.getByRole('button', { name: 'Batalkan pencarian' }).click();
  await expect(first.getByRole('button', { name: /Cari pertandingan/ })).toBeVisible();
  await first.getByRole('button', { name: /Cari pertandingan · MOBA Arena/ }).click();
  await expect(first.locator('.queue-status')).toContainText('1 / 2');

  const context = await browser.newContext(); const guest = await context.newPage();
  await quiet(guest);
  await ready(guest);
  const second = await register(guest, uniqueName('Citra'));
  await guest.getByRole('button', { name: 'MOBA Arena', exact: true }).click();
  await second.getByRole('button', { name: /Cari pertandingan · MOBA Arena/ }).click();
  await expect(page.locator('.match-label b').first()).toHaveText(/ROOM [0-9A-F]{6}/);
  await expect(guest.locator('.match-label b').first()).toHaveText(await page.locator('.match-label b').first().innerText());
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.locator('.match-label')).toContainText('MOBA');
  await expect(guest.locator('.abilities')).toBeVisible();
  await context.close();
});

test('opening cinematic plays and can be skipped, characters animate and combat is heard', async ({ page }) => {
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await ready(page, true); await play(page, 'MOBA Arena');
  const world = page.locator('.world');
  await expect(world).toHaveAttribute('data-cinematic', 'intro');
  await expect(page.locator('.cinematic.on .cine-title b')).toHaveText('MOBA Arena');
  await expect(page.locator('.cinematic.on .cine-title span')).toContainText('Dorong jalur');
  await page.waitForTimeout(1500);
  await page.screenshot({ path: 'docs/screenshots/cinematic.png' });
  expect((await rendererStats(page)).animated).toBeGreaterThan(5); // Rigged characters are playing their clips.
  await page.keyboard.press('h'); // Any key or press ends the shot.
  await expect(world).toHaveAttribute('data-cinematic', 'off');
  await expect(page.locator('.cinematic.on')).toHaveCount(0);
  // The waves meet: the adaptive layer hears the fighting.
  await expect(world).toHaveAttribute('data-combat', 'battle', { timeout: 60000 });
  const fighting = await rendererStats(page);
  expect(fighting.combat).toBeGreaterThan(.1);
  expect(fighting.calls).toBeLessThan(400);
  await page.screenshot({ path: 'docs/screenshots/characters.png' });

  // Switching the cinematic off in Settings is remembered and respected.
  await page.getByRole('button', { name: /Pengaturan/ }).click();
  await page.locator('#cinematics').uncheck();
  await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('nusantara.settings')).cinematics)).toBe(false);
  expect(errors).toEqual([]);
});

test('weather and time of day follow the match clock', async ({ page }) => {
  test.setTimeout(330000);
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await ready(page); await play(page, 'Adventure');
  const world = page.locator('.world'), chip = page.locator('.sky-chip');
  await expect(chip).toContainText('Cerah');
  await expect(world).toHaveAttribute('data-weather', 'clear');
  expect((await rendererStats(page)).rain).toBeLessThan(.05);
  await expect(chip).toContainText('Kabut', { timeout: 120000 });
  await expect(world).toHaveAttribute('data-weather', 'mist');
  await expect(chip).toContainText('Hujan', { timeout: 120000 });
  await expect(world).toHaveAttribute('data-weather', 'rain');
  await expect.poll(async () => (await rendererStats(page)).rain, { timeout: 20000 }).toBeGreaterThan(.4);
  await page.screenshot({ path: 'docs/screenshots/weather-rain.png' });
  await expect(chip).toContainText('Badai', { timeout: 60000 });
  await expect(chip).toHaveAttribute('data-weather', 'storm');
  const stats = await rendererStats(page);
  expect(stats.daylight).toBeLessThan(.7); // Four minutes in, the day has turned towards evening.
  expect(stats.calls).toBeLessThan(400);
  expect(errors).toEqual([]);
});

test('the Keeper answers the authored questions once he has been met', async ({ page }) => {
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await quiet(page);
  await ready(page); await play(page, 'Adventure');
  const dialogue = page.locator('.dialogue'), ask = page.locator('.npc-ask');
  await dialogue.getByRole('button', { name: /Lanjut/ }).click();
  await dialogue.getByRole('button', { name: /Lanjut/ }).click();
  await expect(ask).toHaveCount(0); // Not before the Keeper has been met.
  const spot = await page.evaluate(async () => (await import(new URL('_content/BattleNusantara.UI/world.js', document.baseURI).href)).screenPoint(30, 22));
  await page.mouse.click(spot[0], spot[1]);
  await expect(page.locator('.quest-log li[data-quest="juru-kunci"]')).toHaveAttribute('data-state', 'Done', { timeout: 30000 });
  for (let i = 0; i < 3; i++) await dialogue.getByRole('button', { name: /Lanjut/ }).click();
  await expect(ask).toBeVisible();
  await expect(ask.locator('.npc-topics button')).toHaveCount(3);
  await expect(ask.locator('p')).toContainText('Apa yang ingin kau ketahui');
  await ask.getByRole('button', { name: 'Apa yang menunggu di Gua Mistis?' }).click();
  // The server answers with a generated reply when a model is configured, and with the authored one otherwise.
  await expect(ask.locator('.npc-source')).toHaveText(/naskah|jawaban AI/, { timeout: 20000 });
  await expect(ask.locator('p')).toContainText(/keris/i);
  const source = await ask.locator('.npc-source').getAttribute('data-source');
  if (source === 'authored') await expect(ask.locator('p')).toHaveText('Keris Pusaka Jagat tersimpan di sana, di timur laut. Pintunya tersegel sampai lima penjaga jalan dikalahkan, dan para penunggu gua menjaga kerisnya.');
  console.log('NPC', source, await ask.locator('p').textContent());
  await page.screenshot({ path: 'docs/screenshots/keeper-ask.png' });
  // Walking away closes the panel.
  const away = await page.evaluate(async () => (await import(new URL('_content/BattleNusantara.UI/world.js', document.baseURI).href)).screenPoint(50, 45));
  await page.mouse.click(away[0], away[1]);
  await expect(ask).toHaveCount(0, { timeout: 30000 });
  expect(errors).toEqual([]);
});

const worldModule = page => page.evaluate(async () => (await import(new URL('_content/BattleNusantara.UI/world.js', document.baseURI).href)).screenPoint !== undefined);
const groundPoint = (page, x, z) => page.evaluate(async ([x, z]) => (await import(new URL('_content/BattleNusantara.UI/world.js', document.baseURI).href)).screenPoint(x, z), [x, z]);

test('six heroes with portraits; the original superheroes use their own ultimates', async ({ page }) => {
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await quiet(page);
  await ready(page);
  await page.getByRole('button', { name: 'PAHLAWAN', exact: true }).click();
  const cards = page.locator('.hero-card');
  await expect(cards).toHaveCount(6);
  for (const name of ['Satria Bledhek', 'Rara Angkasa']) await expect(page.locator('.hero-card', { hasText: name })).toContainText('Pahlawan Super');
  await expect.poll(() => page.locator('.hero-art img').evaluateAll(images => images.filter(i => i.complete && i.naturalWidth > 0).length)).toBe(6);
  await page.locator('.hero-card', { hasText: 'Rara Angkasa' }).click();
  await page.screenshot({ path: 'docs/screenshots/heroes.png' });
  await page.getByRole('button', { name: /Bertempur bersama Rara Angkasa/ }).click();
  await page.getByRole('button', { name: 'Adventure', exact: true }).click();
  await page.getByRole('button', { name: 'MAIN SOLO', exact: false }).click();
  await expect(page.locator('.hero-seal img')).toHaveAttribute('src', /rara-angkasa\.webp/);
  // Characters are Blender models, not primitives.
  await expect.poll(async () => (await rendererStats(page)).models).toBeGreaterThan(10);
  // Leap: the hero flies onto the nearest guardian; the effect is drawn and the guardian falls.
  await expect(page.locator('.modal-backdrop')).toHaveCount(0);
  await page.keyboard.press('q');
  await expect(page.locator('.match-label')).toBeVisible();
  await expect.poll(async () => (await rendererStats(page)).effects, { timeout: 3000 }).toBeGreaterThan(0);
  await expect(page.locator('.quest-log li[data-quest="penjaga"]')).toContainText(/[1-5]\/5/, { timeout: 10000 });
  expect(errors).toEqual([]);
});

test('Adventure opens with an authored cutscene and voiced narration', async ({ page }) => {
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  // Record every shot title as it appears: the shots run on the wall clock, which a slow test browser cannot keep pace with.
  await page.addInitScript(() => {
    window.__shots = [];
    new MutationObserver(() => { const title = document.querySelector('.cine-title b')?.textContent; if (title && window.__shots.at(-1) !== title) window.__shots.push(title); })
      .observe(document, { subtree: true, childList: true, characterData: true });
  });
  await ready(page, true); await play(page, 'Adventure');
  const world = page.locator('.world');
  await expect(world).toHaveAttribute('data-cinematic', 'intro');
  await expect.poll(async () => (await rendererStats(page)).voice).toBe('intro:0');
  await expect(page.locator('.cinematic.on .cine-title b')).toHaveText('Perkemahan Juru Kunci', { timeout: 8000 }).catch(() => {});
  await page.screenshot({ path: 'docs/screenshots/cutscene.png' });
  await expect(world).toHaveAttribute('data-cinematic', 'off', { timeout: 30000 });
  expect(await page.evaluate(() => window.__shots)).toEqual(['Karang barat', 'Perkemahan Juru Kunci', 'Gua Mistis', 'Adventure']);
  // The voice clip for the current line exists and is served from the game itself.
  const response = await page.request.get('/_content/BattleNusantara.UI/voice/intro-0-id.mp3');
  expect(response.status()).toBe(200);
  expect((await response.body()).length).toBeGreaterThan(10000);
  // Switching voices off in Settings is remembered.
  await page.getByRole('button', { name: /Pengaturan/ }).click();
  await page.locator('#voices').uncheck();
  await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('nusantara.settings')).voices)).toBe(false);
  expect(errors).toEqual([]);
});

test('the Old Captain gives a generated side quest and answers typed questions', async ({ page }) => {
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await quiet(page);
  await ready(page); await play(page, 'Adventure');
  const dialogue = page.locator('.dialogue'), log = page.locator('.quest-log');
  const clear = async () => { for (let i = 0; i < 8 && await dialogue.count(); i++) { await dialogue.getByRole('button', { name: /Lanjut/ }).click().catch(() => {}); await page.waitForTimeout(200); } };
  await expect(dialogue).toContainText('Narator');
  await expect(page.locator('.modal-backdrop')).toHaveCount(0);
  await clear();
  const shore = await groundPoint(page, 10, 63);
  await page.mouse.click(shore[0], shore[1]);
  await expect(log.locator('li.side-quest')).toHaveAttribute('data-quest', /serpihan|arwah/, { timeout: 30000 });
  await expect(log.locator('li.side-quest')).toContainText(/0\/[34]/);
  await expect(dialogue).toContainText('Nakhoda Tua');
  await clear();
  const ask = page.locator('.npc-ask[data-npc="captain"]');
  await expect(ask).toBeVisible();
  await expect(ask.locator('.npc-topics button')).toHaveCount(3);
  await ask.locator('#npc-question').fill('Kenapa badai terus datang?');
  await ask.getByRole('button', { name: 'Tanya', exact: true }).click();
  await expect(ask.locator('p')).toContainText(/badai|kabut|langit|pandang/i, { timeout: 20000 });
  console.log('TALK', await ask.locator('.npc-source').getAttribute('data-source'), await ask.locator('p').textContent());
  await page.screenshot({ path: 'docs/screenshots/captain-talk.png' });
  await ask.locator('#npc-question').fill('Siapa presiden pertama?');
  await ask.getByRole('button', { name: 'Tanya', exact: true }).click();
  await expect(ask.locator('p')).toContainText(/Laut tak pernah bercerita|tidak|tak/i, { timeout: 20000 });
  expect(errors).toEqual([]);
});
