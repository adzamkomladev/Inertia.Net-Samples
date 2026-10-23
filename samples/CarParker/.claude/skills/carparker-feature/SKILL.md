---
name: carparker-feature
description: Implement a complete scoped CarParker feature across its Inertia routes, services, data contracts and frontend.
---

Read AGENTS.md, docs/CONTRACT.md and the existing feature before editing. Select the affected flow and regression checks from docs/ai/FLOWS.md. Identify the observable acceptance behavior and affected callers. Keep request/Inertia code in routes, behavior and durable enqueueing in services, types in Models, actual validation in Validators, conversions in Mappers and local helpers in Utils. Preserve the sample's providers and transaction guarantees. Use existing UI primitives, Inertia forms and small hooks. Run relevant .NET and Bun checks; exercise changed UI with playwright-cli at desktop/mobile sizes, including invalid and pending states. Warm affected per-user caches before writes and verify invalidation and user isolation. Preserve concurrent loader safety and deferred groups; check initial and partial responses. Update docs/CONTRACT.md for changed contracts, including deferred delivery. For AI setup changes, edit canonical sources and regenerate/check adapters as described in docs/ai/README.md. Report evidence and material limits; do not deploy implicitly.

