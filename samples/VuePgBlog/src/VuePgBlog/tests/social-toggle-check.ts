import assert from 'node:assert/strict'
import { mock } from 'bun:test'
import { nextTick, reactive } from 'vue'

let visit: { method: string; url: string; options: any } | undefined
let replaced: any
let flushed = false
mock.module('@inertiajs/vue3', () => ({ router: {
  put: (url: string, _data: any, options: any) => { visit = { method: 'PUT', url, options } },
  delete: (url: string, options: any) => { visit = { method: 'DELETE', url, options } },
  replace: (params: any) => { replaced = params },
  flushAll: () => { flushed = true },
} }))
const { useSocialToggle } = await import('../ClientApp/composables/useSocialToggle')
const props = reactive({ active: false, count: 2, url: '/posts/0199c9a0-0000-7000-8000-000000000001/like' })
const control = useSocialToggle({ url: () => props.url, id: () => '0199c9a0-0000-7000-8000-000000000001', field: 'liked', active: () => props.active, count: () => props.count, countField: 'likeCount' })

control.toggle()
assert.equal(visit!.method, 'PUT')
assert.deepEqual(visit!.options.only, ['auth'], 'Toggles must not reload the list props')
assert.equal(visit!.options.async, true, 'Toggles must not cancel each other')
assert.equal(control.active.value, true, 'The control flips immediately')
assert.equal(control.count.value, 3)
const request = visit!
visit = undefined
control.toggle()
assert.equal(visit, undefined, 'Rapid clicks must not issue duplicate writes')
assert.equal(control.pending.value, true)

// Rejected or cancelled: Inertia calls onFinish without onSuccess, so the server value returns.
// Refused (e.g. rate limited): a redirect with an error flash is not a success.
request.options.onSuccess({ flash: { toast: { type: 'error', message: 'Too many attempts.' } } })
request.options.onFinish()
assert.equal(control.pending.value, false)
assert.equal(control.active.value, false, 'Rejected visits keep the server state')
assert.equal(control.count.value, 2)

// Confirmed: the entity is patched in place in every list that holds it.
control.toggle()
visit!.options.onSuccess({ flash: {} })
visit!.options.onFinish()
const patched = replaced.props({ posts: { data: [{ id: '0199c9a0-0000-7000-8000-000000000001', liked: false, likeCount: 2 }, { id: '0199c9a0-0000-7000-8000-000000000002', liked: false, likeCount: 7 }] }, post: { id: '0199c9a0-0000-7000-8000-000000000001', liked: false, likeCount: 2 }, users: { data: [{ id: '0199c9a0-0000-7000-8000-000000000001', liked: false, likeCount: 2 }] } })
assert.deepEqual(patched.posts.data, [{ id: '0199c9a0-0000-7000-8000-000000000001', liked: true, likeCount: 3 }, { id: '0199c9a0-0000-7000-8000-000000000002', liked: false, likeCount: 7 }])
assert.deepEqual(patched.post, { id: '0199c9a0-0000-7000-8000-000000000001', liked: true, likeCount: 3 })
assert.equal('liked' in patched, false, 'Unrelated top-level props are not invented')
assert.deepEqual(patched.users.data, [{ id: '0199c9a0-0000-7000-8000-000000000001', liked: false, likeCount: 2 }], 'A post toggle must not touch a user that shares the id')
assert.equal(flushed, true, 'Prefetched pages are dropped after a confirmed toggle')
// A live counter event already counted this like: the patch must not add another.
assert.equal(replaced.props({ posts: { data: [{ id: '0199c9a0-0000-7000-8000-000000000001', liked: false, likeCount: 3 }] } }).posts.data[0].likeCount, 3)

// Fresh server props (patch or live counter event) replace the local value.
props.active = true
props.count = 9
await nextTick()
assert.equal(control.count.value, 9, 'Live counters win over the local count')
control.toggle()
assert.equal(visit!.method, 'DELETE')
console.log('Social toggle checks passed')
