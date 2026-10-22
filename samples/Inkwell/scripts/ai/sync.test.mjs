import { test } from 'node:test'
import assert from 'node:assert/strict'
import { cpSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { resolve, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'
import { spawnSync } from 'node:child_process'

test('generates adapters and detects drift without repairing it', () => {
  const source = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
  const root = mkdtempSync(resolve(tmpdir(), 'sample-ai-'))
  try {
    cpSync(resolve(source, '.agents'), resolve(root, '.agents'), { recursive: true })
    cpSync(resolve(source, 'scripts/ai'), resolve(root, 'scripts/ai'), { recursive: true })
    const run = (...args) => spawnSync(process.execPath, ['scripts/ai/sync.mjs', ...args], { cwd: root, encoding: 'utf8' })
    assert.equal(run().status, 0)
    assert.equal(run('--check').status, 0)
    const target = resolve(root, '.codex/agents/backend.toml')
    writeFileSync(target, 'drift')
    assert.equal(run('--check').status, 1)
    assert.equal(readFileSync(target, 'utf8'), 'drift')
  } finally { rmSync(root, { recursive: true, force: true }) }
})

