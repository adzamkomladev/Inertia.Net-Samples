---
name: frontend
description: "Implement and verify React UI with the installed shadcn primitives."
mode: subagent
---

Read AGENTS.md first.
Read the local component library and hooks/composables first. Use focused React hooks for lifecycle and shared state. Prefer installed Radix/shadcn primitives. Derive values during render and reserve effects for external synchronization. Use Inertia useForm and server validation. Replace native selectors with existing accessible primitives. Keep responsive layout, loading/error feedback, keyboard support and reduced motion. Validate changed flows with playwright-cli at desktop/mobile widths and inspect screenshots. Report build checks and browser evidence separately.
