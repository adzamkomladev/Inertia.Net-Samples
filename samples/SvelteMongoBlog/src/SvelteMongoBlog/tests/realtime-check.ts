import assert from 'node:assert/strict'
import { applyCounterEvent } from '../ClientApp/lib/realtime'

const post = { id: '0199c9a0-0000-7000-8000-000000000004', likeCount: 1, commentCount: 2, viewCount: 3, repostCount: 0, liked: true, bookmarked: true, reposted: false }
const event = { id: '0199c9a0-0000-7000-8000-000000000004', sequence: 8, likeCount: 5, commentCount: 6, viewCount: 7, repostCount: 2 }
const updated = applyCounterEvent([post], event, 7)
assert.equal(updated.applied, true)
assert.deepEqual(updated.items[0], { ...post, likeCount: 5, commentCount: 6, viewCount: 7, repostCount: 2 })
const stale = applyCounterEvent(updated.items, { ...event, sequence: 7, likeCount: 0 }, updated.sequence)
assert.equal(stale.applied, false)
assert.equal(stale.items[0]?.likeCount, 5)
console.log('Realtime counter checks passed')
