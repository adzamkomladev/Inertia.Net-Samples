import { readdirSync, readFileSync, mkdirSync, writeFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const check = process.argv.includes('--check')
let stale = 0
function emit(path, content) {
  const target = resolve(root, path)
  if (check) {
    let actual
    try { actual = readFileSync(target, 'utf8') } catch {}
    if (actual !== content) { console.error('Out of date: ' + path); stale++ }
  } else {
    mkdirSync(dirname(target), { recursive: true })
    writeFileSync(target, content)
  }
}
for (const file of readdirSync(resolve(root, '.agents/roles'))) {
  const name = file.replace(/\.md$/, '')
  const source = readFileSync(resolve(root, '.agents/roles', file), 'utf8')
  const [description, ...body] = source.trim().split('\n')
  const instructions = 'Read AGENTS.md first.\n' + body.join('\n').trim()
  emit('.codex/agents/' + name + '.toml',
    'name = ' + JSON.stringify(name) + '\ndescription = ' + JSON.stringify(description) +
    '\ndeveloper_instructions = ' + JSON.stringify(instructions) + '\n')
  for (const harness of ['claude', 'opencode']) {
    emit('.' + harness + '/agents/' + name + '.md',
      '---\nname: ' + name + '\ndescription: ' + JSON.stringify(description) + '\n' +
      (harness === 'opencode' ? 'mode: subagent\n' : '') + '---\n\n' + instructions + '\n')
  }
}
for (const name of readdirSync(resolve(root, '.agents/skills'))) {
  emit('.claude/skills/' + name + '/SKILL.md',
    readFileSync(resolve(root, '.agents/skills', name, 'SKILL.md'), 'utf8'))
}
for (const file of readdirSync(resolve(root, '.agents/commands'))) {
  const source = readFileSync(resolve(root, '.agents/commands', file), 'utf8')
  for (const harness of ['claude', 'opencode']) emit('.' + harness + '/commands/' + file, source)
}
if (stale) process.exitCode = 1
else console.log(check ? 'AI adapters match canonical sources.' : 'AI adapters updated.')

