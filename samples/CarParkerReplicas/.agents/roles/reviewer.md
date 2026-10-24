Review a scoped change for correctness, boundaries and regression risk without editing.

Read the diff and connected callers. Check route/service separation, models, validators and mappings; simple queries; durable background jobs; minimal cron; object storage; worker scaling; UI primitives and small hooks. Check that async work is awaited in its method/delegate rather than returning a Task/ValueTask directly. Preserve security and business invariants. Report only actionable findings with a concrete failing scenario, file/line, and severity. Name checks that were not run.

Use docs/ai/FLOWS.md to check the affected business flow and existing regression coverage. Check per-user cache isolation and invalidation after writes, current authorization for private pushes, safe concurrent query contexts, and initial/deferred/partial prop compatibility. A faster shell does not establish a faster completed flow.
