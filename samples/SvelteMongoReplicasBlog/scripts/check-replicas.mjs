import assert from 'node:assert/strict'

// Run against the local Compose stack. No accounts or persisted application rows are created.
const base = new URL(process.argv[2] ?? 'http://localhost:8181')
const expected = Number(process.env.EXPECTED_REPLICAS ?? 3)

class Session {
  cookies = new Map()
  constructor(affinity) { if (affinity) this.cookies.set('REPLICA', affinity) }
  async request(path, init = {}) {
    const headers = new Headers(init.headers)
    if (this.cookies.size) headers.set('Cookie', [...this.cookies].map(([k, v]) => `${k}=${v}`).join('; '))
    const response = await fetch(new URL(path, base), { ...init, headers, redirect: 'manual', signal: AbortSignal.timeout(15000) })
    for (const cookie of response.headers.getSetCookie()) {
      const pair = cookie.split(';')[0]
      const at = pair.indexOf('=')
      this.cookies.set(pair.slice(0, at), pair.slice(at + 1))
    }
    return response
  }
}

const backends = new Map()
for (let i = 0; i < expected * 5; i++) {
  const session = new Session()
  // Fresh connections avoid sampling only one proxy thread's keep-alive scheduling.
  const health = await session.request('/health', { headers: { Connection: 'close' } })
  assert.equal(health.status, 200, 'Start the stack and wait for healthy replicas')
  const name = health.headers.get('X-Replica')
  assert.ok(name, 'The request must pass through the supplied HAProxy')
  assert.ok(session.cookies.get('REPLICA'), 'The proxy must issue its affinity cookie')
  backends.set(name, session.cookies.get('REPLICA'))
  await health.text()
}
assert.equal(backends.size, expected, `Expected ${expected} distinct healthy backends`)

const sessions = [...backends].map(([name, affinity]) => ({ name, session: new Session(affinity) }))
for (const { name, session } of sessions) {
  for (let i = 0; i < 3; i++) {
    const response = await session.request('/posts')
    assert.equal(response.status, 200)
    assert.equal(response.headers.get('X-Replica'), name, 'Affinity must preserve request placement')
    assert.match(await response.text(), /data-page=/, 'The app must render an Inertia page')
  }
}
console.log(`Distinct replicas and sticky routing passed: ${[...backends.keys()].join(', ')}`)

const { session } = sessions[0]
const origin = base.origin
const negotiate = await session.request('/realtime/negotiate?negotiateVersion=1', { method: 'POST', headers: { Origin: origin } })
assert.equal(negotiate.status, 200)
const token = (await negotiate.json()).connectionToken
const transport = '/realtime?id=' + encodeURIComponent(token)
try {
  const initial = await session.request(transport)
  assert.equal(initial.status, 200)
  await initial.text()
  const handshake = await session.request(transport, {
    method: 'POST', headers: { Origin: origin, 'Content-Type': 'text/plain' },
    body: '{"protocol":"json","version":1}\u001e',
  })
  assert.equal(handshake.status, 200)
  await handshake.text()
  const accepted = await session.request(transport)
  assert.equal(accepted.status, 200)
  assert.match(await accepted.text(), /\{\}/)
} finally {
  await session.request(transport, { method: 'DELETE', headers: { Origin: origin } })
}
console.log('SignalR negotiation and long-poll affinity passed')

// The rate middleware runs before antiforgery. Missing-CSRF probes use the budget
// without attempting an actual login or sending email. Every request visits a different backend.
for (let attempt = 0; attempt < 2; attempt++) {
  let retry = false
  for (let i = 0; i < 12; i++) {
    const response = await sessions[i % sessions.length].session.request('/login', {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}',
    })
    const delay = Number(response.headers.get('Retry-After'))
    await response.text()
    if (attempt === 0 && i < 10 && response.status === 429 && delay > 0 && delay <= 60) {
      console.log('Waiting for the previously used auth window to expire')
      await new Promise(resolve => setTimeout(resolve, (delay + 1) * 1000))
      retry = true
      break
    }
    assert.equal(response.status, i < 10 ? 419 : 429, 'All backends must share one auth budget')
    if (i >= 10) assert.ok(delay > 0)
  }
  if (!retry) break
}
console.log('One HTTP rate budget across all backends passed (10 allowed probes, 2 rejected)')