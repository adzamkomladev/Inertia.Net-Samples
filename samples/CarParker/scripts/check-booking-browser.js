// Run with playwright-cli run-code --filename=scripts/check-booking-browser.js against a disposable, demo-seeded local app.
async page => {
  const origin = new URL(page.url()).origin
  if (!['localhost', '127.0.0.1'].includes(new URL(origin).hostname)) throw new Error('Use a disposable local app.')
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  const connected = page.waitForEvent('websocket', { predicate: socket => socket.url().includes('/realtime'), timeout: 20000 })
  await page.context().clearCookies()
  await page.setViewportSize({ width: 1280, height: 900 })
  await page.goto(origin + '/login')
  await page.getByRole('textbox', { name: 'E-mail' }).fill('inertia.demo42.carparker.customer@yopmail.com')
  await page.getByRole('textbox', { name: 'Password' }).fill('Password@12345')
  await page.getByRole('button', { name: 'Log in', exact: true }).click()
  await page.waitForURL(origin + '/lots')
  const lotUrl = await page.locator('main a[href*="/lots/"]').first().getAttribute('href')
  if (!lotUrl) throw new Error('Seed demo lots first.')
  await page.locator(`main a[href="${lotUrl}"]`).first().click()
  await page.waitForURL(url => !url.pathname.endsWith('/lots'))
  await page.locator('a[href$="/book"]').first().click()
  await page.waitForURL('**/book')
  await connected
  await page.getByRole('button', { name: 'Open calendar' }).click()
  const calendar = page.getByRole('application')
  await calendar.waitFor()
  await page.screenshot({ path: '.playwright/shots/booking-calendar.png', animations: 'disabled' })
  await page.keyboard.press('Escape')
  const hour = page.locator('#single-start [role="spinbutton"]').first()
  await hour.click()
  await page.keyboard.press('ArrowUp')
  if ((await hour.textContent()) !== '15') throw new Error('Time keyboard editing failed.')

  // A staff change must refresh the booking page after Inertia navigation and queued delivery.
  const free = page.getByRole('button', { name: /^Spot .*, Free/ }).first()
  const label = (await free.getAttribute('aria-label')).split(',')[0].slice(5)
  const lotId = new URL(page.url()).pathname.split('/')[2]
  const initial = JSON.parse(await page.locator('script[data-page="app"]').textContent())
  const current = await page.request.get(page.url(), { headers: { 'X-Inertia': 'true', 'X-Inertia-Version': initial.version } })
  const spot = (await current.json()).props.spots.find(item => item.label === label)
  if (!spot) throw new Error('A free spot is required.')
  const admin = await page.context().browser().newContext()
  try {
    const staff = await admin.newPage()
    await staff.goto(origin + '/login')
    await staff.getByRole('textbox', { name: 'E-mail' }).fill('inertia.demo42.carparker.admin@yopmail.com')
    await staff.getByRole('textbox', { name: 'Password' }).fill('Password@12345')
    await staff.getByRole('button', { name: 'Log in', exact: true }).click()
    await staff.waitForURL(origin + '/lots')
    await staff.goto(origin + '/admin')
    const token = (await admin.cookies()).find(cookie => cookie.name === 'XSRF-TOKEN')
    if (!token) throw new Error('Missing antiforgery cookie.')
    const override = async action => {
      const response = await admin.request.post(`${origin}/admin/lots/${lotId}/spots/${spot.id}/override`, {
        data: { action, note: 'Local browser verification' },
        headers: { 'X-XSRF-TOKEN': decodeURIComponent(token.value), Referer: origin + '/admin' },
      })
      if (!response.ok()) throw new Error(`Spot override failed: ${response.status()}`)
    }
    try {
      await override('block')
      await page.getByRole('button', { name: new RegExp(`^Spot ${label}, Blocked`) }).waitFor({ timeout: 20000 })
    } finally { await override('clear') }
    await page.getByRole('button', { name: new RegExp(`^Spot ${label}, Free`) }).waitFor({ timeout: 20000 })
    if ((await hour.textContent()) !== '15') throw new Error('Realtime refresh overwrote the edited booking time.')
  } finally { await admin.close() }

  for (const width of [1280, 820, 390]) {
    await page.setViewportSize({ width, height: 900 })
    if (await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)) throw new Error(`Overflow at ${width}px`)
    await page.screenshot({ path: `.playwright/shots/booking-${width}.png`, animations: 'disabled' })
  }
  await page.emulateMedia({ colorScheme: 'dark', reducedMotion: 'reduce' })
  await page.reload()
  await page.screenshot({ path: '.playwright/shots/booking-dark.png', animations: 'disabled' })
  if (errors.length) throw new Error(errors.join('\n'))
}
