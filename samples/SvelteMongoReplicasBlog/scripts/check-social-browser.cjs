// Run against a seeded local app: node scripts/check-social-browser.cjs http://localhost:8080
// PLAYWRIGHT_MODULE may point to an existing Playwright installation.
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright')
const assert = require('node:assert/strict')
const fs = require('node:fs')
const path = require('node:path')

;(async () => {
  const url = process.argv[2]
  assert.ok(url, 'Supply the URL of a seeded local app')
  const browser = await chromium.launch({ channel: 'chrome', headless: true })
  try {
    const page = await browser.newPage({ viewport: { width: 1280, height: 900 } })
    const errors = []
    page.on('pageerror', error => errors.push(error.message))
    await page.goto(url + '/login')
    await page.locator('#email').fill('inertia.demo42.00000@yopmail.com')
    await page.locator('#password').fill('Demo-42-correct-horse!')
    await page.getByRole('button', { name: 'Log in', exact: true }).click()
    await page.waitForURL(url + '/posts')
    await page.goto(url + '/users')
    await page.getByRole('heading', { name: 'Find your people.' }).waitFor()
    const person = page.locator('article').filter({ has: page.getByRole('button') }).first()
    const profile = await person.locator('a').first().getAttribute('href')
    const follow = person.getByRole('button')
    const followed = await follow.getAttribute('aria-pressed') === 'true'
    const followers = () => person.locator('p').innerText().then(text => Number(text.match(/(\d+) followers/)[1]))
    const before = await followers()
    await follow.click()
    await page.waitForFunction(({ profile, expected }) => {
      const article = [...document.querySelectorAll('article')].find(el => el.querySelector(`a[href="${profile}"]`))
      return article?.querySelector('button')?.getAttribute('aria-pressed') === String(expected)
    }, { profile, expected: !followed })
    assert.equal(await followers(), before + (followed ? -1 : 1))
    await page.reload()
    assert.equal(await follow.getAttribute('aria-pressed'), String(!followed), 'Follow persists after reload')
    await follow.click()
    await page.waitForFunction(({ profile, expected }) => {
      const article = [...document.querySelectorAll('article')].find(el => el.querySelector(`a[href="${profile}"]`))
      return article?.querySelector('button')?.getAttribute('aria-pressed') === String(expected)
    }, { profile, expected: followed })
    assert.equal(await followers(), before)
    const output = process.env.SOCIAL_SCREENSHOT_DIR
    if (output) { fs.mkdirSync(output, { recursive: true }); await page.screenshot({ path: path.join(output, 'people-desktop.png') }) }
    await page.setViewportSize({ width: 390, height: 844 })
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'People fits on mobile')
    if (output) await page.screenshot({ path: path.join(output, 'people-mobile.png') })
    await page.getByRole('textbox', { name: 'Search people' }).fill('no-person-' + Date.now())
    await page.getByText(/No people match/).waitFor()
    await page.getByRole('button', { name: 'Clear search', exact: true }).first().click()
    await page.locator('article').first().waitFor()
    await page.goto(url + '/posts')
    const post = page.locator('article').first()
    for (const [inactive, active] of [['Like', 'Unlike'], ['Repost', 'Undo repost']]) {
      const button = post.getByRole('button', { name: new RegExp(`^(${inactive}|${active})$`) })
      const initial = await button.getAttribute('aria-pressed') === 'true'
      const count = () => button.locator('span').filter({ hasNot: page.locator('svg') }).allTextContents()
        .then(parts => Number(parts.find(text => /^\d+$/.test(text)) || 0))
      const initialCount = await count()
      await button.click()
      await post.getByRole('button', { name: initial ? inactive : active, exact: true }).waitFor()
      assert.equal(await count(), initialCount + (initial ? -1 : 1), inactive + ' count changes once')
      await page.reload()
      assert.equal(await button.getAttribute('aria-pressed'), String(!initial), inactive + ' persists after reload')
      await button.click()
      await post.getByRole('button', { name: initial ? active : inactive, exact: true }).waitFor()
      assert.equal(await count(), initialCount, inactive + ' undo restores the counter')
    }
    assert.deepEqual(errors, [])
    console.log('Social interactions, persistence, search, and mobile checks passed')
  } finally { await browser.close() }
})().catch(error => { console.error(error); process.exitCode = 1 })
