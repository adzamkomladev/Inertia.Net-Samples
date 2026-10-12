import assert from 'node:assert/strict'
import { randomUUID } from 'node:crypto'
import { spawnSync } from 'node:child_process'

// Real HTTP flows against Compose or Aspire; only verification and fixture cleanup use SQL.
// The reserved demo prefix captures generated mail locally instead of sending it.
const base = new URL(process.argv[2] ?? 'http://localhost:8183')
const run = randomUUID().replaceAll('-', '')
const password = randomUUID() + 'Aa1!'
const fixtures = []
const posts = []
const affinity = new Map()

function sql(query) {
  const container = process.env.ACCEPTANCE_POSTGRES_CONTAINER
  const args = container
    ? ['exec', '-i', '-e', 'ACCEPTANCE_DATABASE=vue_inertia_advanced', container, 'sh', '-c',
      'export PGPASSWORD="$POSTGRES_PASSWORD"; exec psql -U "$POSTGRES_USER" -d "$ACCEPTANCE_DATABASE" -X -q -A -t -v ON_ERROR_STOP=1']
    : ['compose', 'exec', '-T', 'postgres', 'sh', '-c',
      'export PGPASSWORD="$POSTGRES_PASSWORD"; exec psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -X -q -A -t -v ON_ERROR_STOP=1']
  const result = spawnSync('docker', args,
    { input: query, encoding: 'utf8', timeout: 30000 })
  assert.equal(result.status, 0, 'PostgreSQL acceptance query must succeed')
  return result.stdout.trim()
}

class Session {
  cookies = new Map()
  version
  backend
  constructor(backend) { if (backend) this.move(backend) }
  move(backend) {
    this.backend = backend
    this.cookies.set('REPLICA', affinity.get(backend))
  }
  async request(path, init = {}) {
    const headers = new Headers(init.headers)
    if (this.cookies.size) headers.set('Cookie', [...this.cookies].map(([k, v]) => `${k}=${v}`).join('; '))
    const response = await fetch(new URL(path, base), { ...init, headers, redirect: 'manual', signal: AbortSignal.timeout(20000) })
    for (const cookie of response.headers.getSetCookie()) {
      const pair = cookie.split(';')[0], at = pair.indexOf('=')
      this.cookies.set(pair.slice(0, at), pair.slice(at + 1))
    }
    if (path.startsWith('/realtime')) {
      assert.ok(response.headers.get('X-Realtime-Replica'), 'SignalR must reach a dedicated realtime host')
      assert.equal(response.headers.get('X-Replica'), null, 'MainApp must not handle SignalR')
    } else if (this.backend) assert.equal(response.headers.get('X-Replica'), this.backend, 'Request must reach the selected replica')
    return response
  }
  async page(path) {
    const headers = this.version === undefined ? {} : { 'X-Inertia': 'true', 'X-Inertia-Version': this.version }
    const response = await this.request(path, { headers })
    assert.equal(response.status, 200, `Page ${path} must load`)
    const body = await response.text()
    let page
    if (response.headers.get('Content-Type')?.includes('application/json')) page = JSON.parse(body)
    else {
      const script = body.match(/<script[^>]*\bdata-page=[^>]*>([\s\S]*?)<\/script>/)
      if (script) page = JSON.parse(script[1])
      else {
        const match = body.match(/data-page="([^"]+)"/)
        assert.ok(match, 'Server must render an Inertia page')
        page = JSON.parse(match[1].replaceAll('&quot;', '"').replaceAll('&#x27;', "'").replaceAll('&#39;', "'")
          .replaceAll('&lt;', '<').replaceAll('&gt;', '>').replaceAll('&amp;', '&'))
      }
    }
    this.version = page.version ?? ''
    return page
  }
  async mutate(path, body = {}, method = 'POST', expectedStatus) {
    await this.page('/posts')
    const send = () => this.request(path, {
      method, body: JSON.stringify(body), headers: {
        'Content-Type': 'application/json', 'X-Inertia': 'true',
        'X-XSRF-TOKEN': decodeURIComponent(this.cookies.get('XSRF-TOKEN') ?? ''),
        Referer: new URL('/posts', base).href, Origin: base.origin,
      },
    })
    let response = await send()
    if (response.status === 429 || response.headers.has('Retry-After')) {
      const wait = Math.min(60, Math.max(1, Number(response.headers.get('Retry-After')) || 60))
      console.log(`Waiting ${wait}s for the existing shared rate window`)
      await response.text()
      await new Promise(resolve => setTimeout(resolve, (wait + 1) * 1000))
      response = await send()
    }
    if (expectedStatus) assert.equal(response.status, expectedStatus)
    else assert.ok([200, 204, 302, 303].includes(response.status), `${method} ${path} returned ${response.status}`)
    await response.text()
    return response
  }
}

class LiveConnection {
  constructor(session, path) { this.session = session; this.path = path }
  static async open(session) {
    const negotiated = await session.request('/realtime/negotiate?negotiateVersion=1', { method: 'POST', headers: { Origin: base.origin } })
    assert.equal(negotiated.status, 200)
    const token = (await negotiated.json()).connectionToken
    const path = '/realtime?id=' + encodeURIComponent(token)
    const initial = await session.request(path)
    assert.equal(initial.status, 200)
    await initial.text()
    const handshake = await session.request(path, { method: 'POST', headers: { Origin: base.origin, 'Content-Type': 'text/plain' },
      body: '{"protocol":"json","version":1}\u001e' })
    assert.equal(handshake.status, 200)
    await handshake.text()
    const accepted = await session.request(path)
    assert.equal(accepted.status, 200)
    assert.match(await accepted.text(), /\{\}/)
    return new LiveConnection(session, path)
  }
  async until(target, postId) {
    const deadline = Date.now() + 20000
    while (Date.now() < deadline) {
      const response = await this.session.request(this.path)
      assert.equal(response.status, 200)
      for (const frame of (await response.text()).split('\u001e').filter(Boolean)) {
        const event = JSON.parse(frame)
        if (event.target === target && (!postId || event.arguments[0]?.id === postId || event.arguments[0] === postId)) return event
      }
    }
    throw new Error(`Timed out waiting for ${target}`)
  }
  async close() {
    await this.session.request(this.path, { method: 'DELETE', headers: { Origin: base.origin } })
  }
}

let live
try {
  const readyUntil = Date.now() + 90000
  while (true) {
    try {
      const response = await fetch(new URL('/health', base), { signal: AbortSignal.timeout(3000) })
      await response.text()
      if (response.ok) break
    } catch {}
    assert.ok(Date.now() < readyUntil, 'Gateway must become ready')
    await new Promise(resolve => setTimeout(resolve, 1000))
  }
  for (let i = 0; i < 30 && affinity.size < 3; i++) {
    const session = new Session()
    const response = await session.request('/health', { headers: { Connection: 'close' } })
    assert.equal(response.status, 200)
    affinity.set(response.headers.get('X-Replica'), session.cookies.get('REPLICA'))
    await response.text()
  }
  assert.equal(affinity.size, 3, 'Expected three healthy replicas')
  const backends = [...affinity.keys()].sort()
  console.log(`Testing real application flows on ${backends.join(', ')}`)

  for (let i = 0; i < backends.length; i++) {
    const email = `inertia.demo42.replica-test-${run}-${i}@yopmail.com`
    fixtures.push(email)
    const session = new Session(backends[i])
    await session.page('/register')
    const registered = await session.mutate('/register', { name: `Replica acceptance ${i + 1}`, email, password, passwordConfirmation: password })
    assert.equal(registered.headers.get('Location'), '/email/verify')
    const verification = await session.page('/email/verify')
    assert.equal(verification.props.auth.user.email, email)
    assert.equal(verification.props.auth.user.verified, false)
    const blocked = await session.request('/posts/create')
    assert.equal(blocked.status, 302, 'Unverified users cannot publish')
    await blocked.text()
    assert.match(sql(`UPDATE "Users" SET "EmailVerifiedAt" = NOW() WHERE "Email" = '${email}' RETURNING "Id";`), /^[0-9a-f-]{36}$/)
    await session.mutate('/logout')
    assert.equal((await session.page('/posts')).props.auth.user, null)
    session.move(backends[(i + 1) % 3])
    await session.page('/login')
    await session.mutate('/login', { email, password, remember: true })
    for (const backend of backends) {
      session.move(backend)
      const editor = await session.page('/posts/create')
      assert.equal(editor.props.auth.user.email, email)
      assert.equal(editor.props.auth.user.verified, true)
    }
    fixtures[i] = { email, session }
    console.log(`Signup on ${backends[i]}, database verification, signin on ${backends[(i + 1) % 3]}, authenticated access on all replicas: passed`)
  }

  for (let i = 0; i < backends.length; i++) {
    const owner = fixtures[i].session
    owner.move(backends[i])
    const title = `Replica acceptance ${run} ${i}`
    const created = await owner.mutate('/posts', { title, body: 'Original acceptance body', topics: ['replicas'] })
    const url = created.headers.get('Location')
    assert.ok(url?.startsWith('/posts/'))
    const detail = await owner.page(url)
    const id = detail.props.post.id
    assert.match(id, /^[0-9a-f-]{36}$/)
    posts.push({ id, url, owner })
    for (const backend of backends) {
      const guest = new Session(backend)
      const visible = await guest.page(url)
      assert.equal(visible.props.post.title, title)
      assert.equal(visible.props.post.body, 'Original acceptance body')
    }
    owner.move(backends[(i + 1) % 3])
    await owner.mutate(`/posts/${id}`, { title: title + ' edited', body: 'Updated acceptance body', topics: ['replicas'] }, 'PUT')
    assert.equal((await owner.page(url)).props.post.body, 'Updated acceptance body')
    console.log(`Create on ${backends[i]}, read on all replicas, edit on ${backends[(i + 1) % 3]}: passed`)
  }

  const post = posts[0]
  const reader = fixtures[1].session
  post.owner.move(backends[0])
  reader.move(backends[1])
  live = await LiveConnection.open(post.owner)
  await reader.mutate(`/posts/${post.id}/like`, {}, 'PUT')
  assert.equal((await live.until('PostChanged', post.id)).arguments[0].likeCount, 1)
  assert.equal((await reader.page(post.url)).props.post.liked, true)
  await reader.mutate(`/posts/${post.id}/comments`, { body: 'Cross-replica acceptance comment' })
  assert.equal((await live.until('PostChanged', post.id)).arguments[0].commentCount, 1)
  const comments = await reader.request(`/posts/${post.id}/comments`)
  assert.equal(comments.status, 200)
  const commentPage = await comments.json()
  const comment = commentPage.data.find(item => item.body === 'Cross-replica acceptance comment')
  assert.ok(comment)
  await reader.mutate(`/posts/${post.id}/bookmark`, {}, 'PUT')
  assert.equal((await reader.page(post.url)).props.post.bookmarked, true)
  assert.ok((await reader.page('/bookmarks')).props.posts.data.some(item => item.id === post.id))
  await reader.mutate(`/posts/${post.id}`, {}, 'DELETE', 403)
  await reader.mutate(`/comments/${comment.id}`, {}, 'DELETE')
  await reader.mutate(`/posts/${post.id}/like`, {}, 'DELETE')
  const restored = (await reader.page(post.url)).props.post
  assert.equal(restored.likeCount, 0)
  assert.equal(restored.commentCount, 0)
  console.log('Cross-replica likes, comments, bookmarks, ownership checks and live counters: passed')
  await reader.mutate(`/posts/${post.id}/view`, { referrer: 'https://acceptance.example/story' })
  let counters
  do { counters = (await live.until('PostChanged', post.id)).arguments[0] } while (counters.viewCount < 1)
  assert.equal(counters.viewCount, 1, 'Worker must persist and broadcast the queued view')
  assert.equal(sql(`SELECT COUNT(*) FROM "PostViews" WHERE "PostId" = '${post.id}';`), '1')
  console.log('Durable view job processed by Worker and delivered through Realtime: passed')
  await live.close(); live = null

  for (const { id, owner } of posts) {
    owner.move(backends[2])
    await owner.mutate(`/posts/${id}`, {}, 'DELETE')
    for (const backend of backends) {
      const removed = await new Session(backend).request(posts.find(post => post.id === id).url)
      assert.equal(removed.status, 404)
      await removed.text()
    }
  }
  console.log('Post deletion is visible on every replica: passed')

  const emails = fixtures.map(fixture => `'${fixture.email}'`).join(',')
  let mailDone = false
  for (let i = 0; i < 20; i++) {
    mailDone = sql(`SELECT COUNT(*) FROM "Jobs" WHERE "Type" = 'SendEmail' AND "Payload"::jsonb ->> 'To' IN (${emails}) AND "Status" = 'Done';`) >= 3
    if (mailDone) break
    await new Promise(resolve => setTimeout(resolve, 1000))
  }
  assert.ok(mailDone, 'Verification email jobs must be processed (captured demo mail)')
  for (const { session } of fixtures) {
    await session.mutate('/logout')
    assert.equal((await session.page('/posts')).props.auth.user, null)
  }
  console.log('Verification job processing and logout on all accounts: passed')
  console.log('All live application flow checks passed')
} finally {
  if (live) await live.close().catch(() => {})
  const emails = fixtures.map(fixture => `'${typeof fixture === 'string' ? fixture : fixture.email}'`).join(',')
  if (emails) {
    sql(`DELETE FROM "Users" WHERE "Email" IN (${emails});`)
    console.log('Acceptance accounts removed from the database')
  }
}
