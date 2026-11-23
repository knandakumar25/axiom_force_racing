const assert = require('node:assert/strict');
const { chromium } = require('playwright');

async function main() {
  const browser = await chromium.launch();
  try {
    const page = await browser.newPage();
    const site = process.env.BASE_URL || 'http://website';
    const response = await page.goto(site);
    assert.equal(response.status(), 200);
    assert.match(await page.locator('body').innerText(), /Axiom Force Racing/);
    const health = await page.request.get(site + '/api/health');
    assert.equal(health.status(), 200);
    assert.equal((await health.json()).status, 'ok');
  } finally {
    await browser.close();
  }
}
main().catch((error) => { console.error(error); process.exitCode = 1; });
