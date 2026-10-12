import assert from 'node:assert/strict'
import { applyCounterEvent } from '../ClientApp/hooks/use-realtime'

const id = '0199c7a4-3b2e-7c41-9a6d-2f4e8b1c5d70'
const post = { id, likeCount: 1, commentCount: 2, viewCount: 3, repostCount: 0, liked: true, bookmarked: true, reposted: false }
const event = { id, sequence: 8, likeCount: 5, commentCount: 6, viewCount: 7, repostCount: 2 }
const updated = applyCounterEvent([post], event, 7)
assert.equal(updated.applied, true)
assert.deepEqual(updated.items[0], { ...post, likeCount: 5, commentCount: 6, viewCount: 7, repostCount: 2 })
const stale = applyCounterEvent(updated.items, { ...event, sequence: 7, likeCount: 0 }, updated.sequence)
assert.equal(stale.applied, false)
assert.equal(stale.items[0]?.likeCount, 5)
