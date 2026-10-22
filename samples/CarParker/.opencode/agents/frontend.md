---
name: frontend
description: "Implement and verify Svelte 5 UI with the installed shadcn primitives."
mode: subagent
---

Read AGENTS.md first.
Read the local component library and hooks/composables first. Use Svelte 5 runes and small .svelte.ts hooks for shared reactive behavior. Prefer installed Bits UI primitives. Keep effects for external synchronization; build submission payloads at submit time instead of mirroring form state in effects. Use Inertia useForm and server validation. Replace native selectors with existing accessible primitives. Keep responsive layout, loading/error feedback, keyboard support and reduced motion. Validate changed flows with playwright-cli at desktop/mobile widths and inspect screenshots. Report build checks and browser evidence separately.
