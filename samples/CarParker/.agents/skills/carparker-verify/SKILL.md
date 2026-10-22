---
name: carparker-verify
description: Verify a CarParker change with its relevant backend, frontend and browser checks.
---

Read AGENTS.md and inspect the diff. From the sample root run node scripts/ai/sync.mjs --check and node --test scripts/ai/sync.test.mjs. Run dotnet test tests/CarParker.Tests with disposable database infrastructure. From src/CarParker, run bun run typecheck and bun run build for frontend changes. Use relevant package check scripts. For UI changes use playwright-cli to exercise the changed flow on desktop/mobile, inspect screenshots and check overflow, console failures, keyboard operation and form feedback. Never use real customer data or send real emails for verification. Distinguish passing checks, skipped checks and environmental failures.

