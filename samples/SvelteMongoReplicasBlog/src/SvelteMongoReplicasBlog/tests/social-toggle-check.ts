import assert from 'node:assert/strict'
import { patchEntityProps, refused, socialPatch } from '../ClientApp/lib/social-toggle'

// The visit only reloads the tiny `auth` prop, so feeds and infinite-scroll pages keep their items and scroll position.
const posts = {
  data: [
    { id: '0199c9a0-0000-7000-8000-000000000001', liked: false, likeCount: 2 },
    { id: '0199c9a0-0000-7000-8000-000000000002', liked: false, likeCount: 7 },
  ],
}
const users = { data: [{ id: '0199c9a0-0000-7000-8000-000000000001', liked: false, likeCount: 2 }] }

// Confirmed like: the entity is patched in place in every list that holds it.
const patch = socialPatch({ field: 'liked', next: true, base: 2, countField: 'likeCount' })
const patched = patchEntityProps(
  { posts, post: { id: '0199c9a0-0000-7000-8000-000000000001', liked: false, likeCount: 2 }, users, liked: false },
  { kind: 'post', id: '0199c9a0-0000-7000-8000-000000000001', patch, extra: { liked: true } },
)
assert.deepEqual(patched.posts.data, [
  { id: '0199c9a0-0000-7000-8000-000000000001', liked: true, likeCount: 3 },
  { id: '0199c9a0-0000-7000-8000-000000000002', liked: false, likeCount: 7 },
])
assert.deepEqual(patched.post, { id: '0199c9a0-0000-7000-8000-000000000001', liked: true, likeCount: 3 })
assert.equal(patched.liked, true, 'An existing top-level prop is updated')
assert.equal('bookmarked' in patched, false, 'Unrelated top-level props are not invented')
assert.deepEqual(patched.users.data, [{ id: '0199c9a0-0000-7000-8000-000000000001', liked: false, likeCount: 2 }], 'A post toggle must not touch a user that shares the id')

// A live counter event already counted this like: the patch must not add another.
assert.equal(socialPatch({ field: 'liked', next: true, base: 2, countField: 'likeCount' })({ id: 'x', liked: false, likeCount: 3 }).likeCount, 3)

// Removing a bookmark while viewing the bookmarks feed drops the row from that list.
const removed = patchEntityProps(
  { mode: 'bookmarks', posts },
  { kind: 'post', id: '0199c9a0-0000-7000-8000-000000000001', patch: socialPatch({ field: 'bookmarked', next: false, base: 0 }), drop: props => props.mode === 'bookmarks' },
)
assert.deepEqual(removed.posts.data, [posts.data[1]])

// Unfollow patches a user in the users list without touching same-id posts.
const followed = patchEntityProps(
  { posts, users: { data: [{ id: '0199c9a0-0000-7000-8000-000000000001', following: false, followerCount: 4 }] } },
  { kind: 'user', id: '0199c9a0-0000-7000-8000-000000000001', patch: socialPatch({ field: 'following', next: true, base: 4, countField: 'followerCount' }) },
)
assert.deepEqual(followed.users.data, [{ id: '0199c9a0-0000-7000-8000-000000000001', following: true, followerCount: 5 }])
assert.deepEqual(followed.posts.data, posts.data)

// A refused write (redirect plus error flash) is not a success.
assert.equal(refused({ flash: { toast: { type: 'error', message: 'Too many attempts.' } } }), true)
assert.equal(refused({ flash: { toast: { type: 'success', message: 'Liked' } } }), false)
assert.equal(refused({ flash: {} }), false)
assert.equal(refused({}), false)

console.log('Social toggle checks passed')
