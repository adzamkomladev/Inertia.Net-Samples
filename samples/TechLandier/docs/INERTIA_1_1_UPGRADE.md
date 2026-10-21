# Inertia.Net 1.1 upgrade measurements

Measured on 2026-10-13 on the same Windows x64 host (.NET SDK 10.0.401, runtime 10.0.12), in Release mode. Inertia.Net **1.0.3 → 1.1.0** and Inertia.Net.Testing **1.0.2 → 1.1.0**. Package versions were confirmed against NuGet's live flat-container registry.

The opt-in `InertiaPerformanceTests` runs the actual app through ASP.NET TestServer against an isolated, empty database. Each mode warms up with 100 GET `/posts` requests, then records five rounds of 500 sequential requests. Startup, migrations and disposal are outside the timer. HTML uses the root template with SSR explicitly disabled; JSON sends `X-Inertia` and the correct asset version; partial JSON selects `posts` for the returned component. Every timed request checks HTTP 200 and its expected HTML or JSON shape. The advanced PostgreSQL comparisons were repeated with healthy Redis. No frontend dependencies changed. Vue/Svelte SSR listener configuration now explicitly binds to `127.0.0.1` to follow the 1.1 managed-process guidance.

Tables use the median of five round means, round p95s, rates and process-wide allocation counts. Positive reduction means improvement; negative means regression. Allocation includes the app, database driver, TestServer, response reads and assertions, plus any remaining host background activity. Throughput is the reciprocal of sequential request latency, not measured concurrent capacity. Old runs precede new runs, rather than being randomized or interleaved. These short measurements have host/database noise and do not establish statistically significant package-only gains. They do not measure actual Node/Bun SSR, seeded feeds, browser rendering, remote network latency or replica scaling; use the existing seeded k6 harness for those workloads.

Baseline source commit: `15cdd12860da7826af4790d5746288ef4317cb56` (plus the identical measurement test; Vue MongoDB also uses the test-only connection override in both runs). Raw rounds: [before](inertia-1.1-measurements/before.json), [after](inertia-1.1-measurements/after.json).

| Mode | Mean ms before → after | Latency reduction | p95 ms before → after | req/s before → after | Allocated bytes/request before → after | Allocation reduction |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| html | 10.181 → 7.689 | 24.5% | 12.836 → 9.180 | 98.226 → 130.063 | 117860 → 118358 | -0.4% |
| json | 9.874 → 6.770 | 31.4% | 11.845 → 8.792 | 101.280 → 147.717 | 121045 → 120853 | 0.2% |
| partial | 9.998 → 6.195 | 38.0% | 11.809 → 7.829 | 100.020 → 161.419 | 120602 → 120374 | 0.2% |

Validation: `Passed!  - Failed:     0, Passed:    54, Skipped:     0, Total:    54, Duration: 23 s - TechLandier.Tests.dll (net10.0)`. Frontend typecheck, production client/SSR builds and the existing development SSR render check passed. The built Bun SSR server also passed health, rendered the empty Posts/Index feed and was verified to listen only on 127.0.0.1. Mocked React SSR tests emulate Vite rather than bypassing managed-child readiness. The opt-in benchmark returns without measuring when its output environment variable is unset.

To rerun from the sample root, start the test database (SQLite requires none) and Redis where required. Set `INERTIA_TEST_CONNECTION` and, for replica coordination, `INERTIA_TEST_REDIS` as documented by the existing test fixtures:

```powershell
$env:INERTIA_BENCHMARK_OUTPUT = Join-Path $PWD "rendering.json"
dotnet test tests/TechLandier.Tests/TechLandier.Tests.csproj -c Release --filter FullyQualifiedName~InertiaPerformanceTests
Remove-Item Env:INERTIA_BENCHMARK_OUTPUT
```
