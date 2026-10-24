# Working on CarParkerReplicas with AI

Start Codex, OpenCode or Claude in this sample directory. This sample is standalone; copy the whole directory when sharing it. AGENTS.md owns project rules; CLAUDE.md imports it. Codex/OpenCode discover .agents/skills; Claude receives generated copies.

Canonical role instructions are in .agents/roles; custom backend, frontend and reviewer agents are generated into .codex/agents, .opencode/agents and .claude/agents. Use a role when its scope helps; these files do not require delegation on every task or choose a paid model for you.

Use the carparker-feature and carparker-verify skills (Codex: $carparker-feature / $carparker-verify). Claude/OpenCode also have /review and /ui commands. Edit .agents sources, run `node scripts/ai/sync.mjs`, and commit generated adapters. `--check` detects drift without writing. The script's node:test check verifies generation and drift detection.

Current application flows and check selection: [FLOWS.md](FLOWS.md). Read this map for cache invalidation, concurrent data loading, deferred props, notification behavior and role-specific business flows before implementing or reviewing a change.

To update this AI setup:

1. Edit AGENTS.md for project rules, .agents/skills for workflows, .agents/roles for custom agent instructions and .agents/commands for /review and /ui. CLAUDE.md imports AGENTS.md.
2. Run `node scripts/ai/sync.mjs` from this sample root to regenerate Codex agents and Claude/OpenCode adapters. Keep each sample self-contained.
3. Run `node scripts/ai/sync.mjs --check` and `node --test scripts/ai/sync.test.mjs`; review and commit the canonical and generated files together. For instruction-only changes these are the relevant automated checks; application changes also use the flow map.

The only configured MCP is Microsoft Learn's public documentation server, for .NET/EF reference lookup. No database credentials, browser MCP, production access or account tokens are committed. Browser validation uses playwright-cli as requested. No lifecycle hooks are installed: verification is an explicit workflow, so a hook cannot start servers, run migrations or prevent a user from ending a session.

Optional tools need to be installed by the sample consumer: .NET 10, Bun, Node (for AI adapter scripts), Docker for disposable databases, and playwright-cli with a browser. Keep .env private. MCP connections and custom agents load according to the harness's project trust settings.

Reference formats: [Codex agents](https://learn.chatgpt.com/docs/agent-configuration/subagents), [Codex MCP](https://learn.chatgpt.com/docs/extend/mcp?surface=cli), [Claude agents](https://code.claude.com/docs/en/sub-agents), [OpenCode agents](https://opencode.ai/docs/agents/), [OpenCode MCP](https://opencode.ai/docs/mcp-servers/), [Microsoft Learn MCP](https://learn.microsoft.com/en-us/training/support/mcp).


Query policy: use the MongoDB driver's direct query, projection, sort and update APIs. Normal name/price listings page in the database. Occupancy sorting still ranks filtered lots in memory; replace it with an aggregation only if the catalogue outgrows that explicit limit. Preserve session transactions and atomic find-and-update job claims.
