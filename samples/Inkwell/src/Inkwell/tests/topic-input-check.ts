import assert from 'node:assert/strict'
import { normalizeTopic, withDraft } from '../ClientApp/components/posts/TopicInput'

// Mirrors the server rule in PostsValidators: ^[a-z0-9]+(?:-[a-z0-9]+)*$, 1–32 characters.
const valid = (t: string) => t.length > 0 && t.length <= 32 && /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(t)

assert.equal(normalizeTopic('  Design Notes '), 'design-notes')
assert.equal(normalizeTopic('C# & .NET!!'), 'c-net')
assert.equal(normalizeTopic('---'), '')
// 31 letters then "-b": truncation at 32 must not leave a trailing hyphen.
const truncated = normalizeTopic('a'.repeat(31) + '-b')
assert.equal(truncated, 'a'.repeat(31))
assert.ok(valid(truncated))
for (const raw of ['x'.repeat(40), 'a b c', 'Ünïcode café', '-lead-', 'a'.repeat(32) + '-tail']) {
  const t = normalizeTopic(raw)
  assert.ok(t === '' || valid(t), `${raw} -> ${t}`)
}

assert.deepEqual(withDraft(['a'], ' B '), ['a', 'b'])
assert.deepEqual(withDraft(['a'], 'A'), ['a'])
assert.deepEqual(withDraft(['a', 'b', 'c', 'd', 'e'], 'f'), ['a', 'b', 'c', 'd', 'e'])
assert.deepEqual(withDraft(['a'], '  '), ['a'])
console.log('Topic input checks passed')
