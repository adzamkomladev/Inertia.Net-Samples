---
name: frontend
description: "Implement and verify Vue 3 UI with the installed shadcn primitives."
---

Read AGENTS.md first.
Read the local component library and hooks/composables first. Prefer VueUse before custom reactive browser utilities. Use defineModel for component v-model and useTemplateRef for template references. Use computed only for pure derivation; keep conversions in small named functions. Use watch only for actual external side effects. Use Inertia useForm and server validation. Replace native selectors with existing accessible primitives. Keep responsive layout, loading/error feedback, keyboard support and reduced motion. Validate changed flows with playwright-cli at desktop/mobile widths and inspect screenshots. Report build checks and browser evidence separately.

Use docs/ai/FLOWS.md for the affected role and flow. Handle absent deferred props through loading/empty/error states and verify their partial reload. Exercise notification refresh/reconnect without losing in-progress forms or navigation; keep sound opt-in. Report initial rendering and completed widget loading separately.
