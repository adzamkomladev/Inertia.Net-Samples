import assert from 'node:assert/strict'
import { refreshProps } from '../ClientApp/lib/realtime'

const post = { id: 'one', likeCount: 5, liked: true, bookmarked: true }
const props = { posts: { data: [post] }, post }
// Both publishers may restart at sequence 1. Any matching event must refetch.
const events = [{ id: 'one', sequence: 100 }, { id: 'one', sequence: 1 }]
for (const event of events) assert.deepEqual(refreshProps(props, [event.id]), ['posts', 'post'])
assert.deepEqual(refreshProps(props, ['unrelated']), [])
assert.deepEqual(refreshProps({ post }, ['one']), ['post'])
assert.deepEqual(refreshProps({ posts: { data: [post] } }, ['one', 'one']), ['posts'])
assert.deepEqual(post, { id: 'one', likeCount: 5, liked: true, bookmarked: true })
console.log('Replica invalidation checks passed')
