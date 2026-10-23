---
name: carparker-verify
description: Verify a scoped CarParker change with its relevant backend, frontend and browser checks.
---

Read AGENTS.md and inspect the diff and affected callers. Use docs/ai/FLOWS.md to select the affected business flow, roles and regression checks. From the sample root run node scripts/ai/sync.mjs --check and node --test scripts/ai/sync.test.mjs. For backend changes run dotnet test tests/CarParker.Tests with disposable database infrastructure. From src/CarParker, run bun run typecheck and bun run build for frontend changes. Use relevant package check scripts. For UI changes use playwright-cli to exercise the changed flow on desktop/mobile, inspect screenshots and check overflow, console failures, keyboard operation and form feedback. For cache changes warm the cache, mutate, then check owner and other-user responses. For deferred changes check initial omissions, group metadata and partial reload results, including loading/error feedback. For performance changes measure completed flows and database operations with cold/warm caches; report shell and deferred timings separately. Never use real customer data or send real emails for verification. Distinguish passing checks, skipped checks and environmental failures.

