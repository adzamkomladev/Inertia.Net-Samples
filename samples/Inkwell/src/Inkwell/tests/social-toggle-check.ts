import assert from 'node:assert/strict'
import { mock } from 'bun:test'

const slots: any[] = []
let cursor = 0
let visit: { method: string; url: string; options: any } | undefined
let replaced: any
let flushed = false
mock.module('react', () => ({
  useCallback: (callback: any) => callback,
  useRef: (value: any) => slots[cursor++] ??= { current: value },
  useState: (value: any) => {
    const index = cursor++
    if (!(index in slots)) slots[index] = value
    return [slots[index], (next: any) => { slots[index] = typeof next === 'function' ? next(slots[index]) : next }]
  },
}))
mock.module('@inertiajs/react', () => ({ router: {
  put: (url: string, _data: any, options: any) => { visit = { method: 'PUT', url, options } },
  delete: (url: string, options: any) => { visit = { method: 'DELETE', url, options } },
  replace: (params: any) => { replaced = params },
  flushAll: () => { flushed = true },
} }))
const { useSocialToggle } = await import('../ClientApp/hooks/use-social-mutation')
const first = '0199c7a4-3b2e-7c41-9a6d-2f4e8b1c5d70'
const second = '0199c7a4-3b2f-7d02-8e5a-6c1f9b3d2e81'
const render = (active = false, count = 2, url = `/posts/${first}/like`) => {
  cursor = 0
  return useSocialToggle({ url, id: first, field: 'liked', active, count, countField: 'likeCount' })
}

const control = render()
control.toggle()
assert.equal(visit!.method, 'PUT')
assert.deepEqual(visit!.options.only, ['auth'], 'Toggles must not reload the list props')
assert.equal(visit!.options.async, true, 'Toggles must not cancel each other')
assert.equal(render().active, true, 'The control flips immediately')
assert.equal(render().count, 3)
const request = visit!
visit = undefined
control.toggle()
assert.equal(visit, undefined, 'Rapid clicks must not issue duplicate writes')
assert.equal(render().pending, true)

// Rejected or cancelled: Inertia calls onFinish without onSuccess, so the server value returns.
// Refused (e.g. rate limited): a redirect with an error flash is not a success.
request.options.onSuccess({ flash: { error: 'Too many attempts.' } })
request.options.onFinish()
assert.equal(render().pending, false)
assert.equal(render().active, false, 'Rejected visits keep the server state')
assert.equal(render().count, 2)

// Confirmed: the entity is patched in place in every list that holds it.
render().toggle()
visit!.options.onSuccess({ flash: {} })
visit!.options.onFinish()
const props = replaced.props({ posts: { data: [{ id: first, liked: false, likeCount: 2 }, { id: second, liked: false, likeCount: 7 }] }, post: { id: first, liked: false, likeCount: 2 }, liked: false })
assert.deepEqual(props.posts.data, [{ id: first, liked: true, likeCount: 3 }, { id: second, liked: false, likeCount: 7 }])
assert.deepEqual(props.post, { id: first, liked: true, likeCount: 3 })
assert.equal(props.liked, true)
assert.equal(replaced.preserveScroll, true)
assert.equal(flushed, true, 'Prefetched pages are dropped after a confirmed toggle')
// A live counter event already counted this like: the patch must not add another.
assert.equal(replaced.props({ posts: { data: [{ id: first, liked: false, likeCount: 3 }] } }).posts.data[0].likeCount, 3)

// Fresh server props (patch or live counter event) replace the local value.
render(true, 9) // React re-renders after a state update during render; the mock needs a second pass.
assert.equal(render(true, 9).count, 9, 'Live counters win over the local count')
render(true, 9).toggle()
assert.equal(visit!.method, 'DELETE')
console.log('Social toggle checks passed')
