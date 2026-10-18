# Inertia.Net 1.2 upgrade measurements

All nineteen local samples and standalone repositories (not the TanStack Start one) moved from Inertia.Net and Inertia.Net.Testing **1.1.0 → 1.2.0** on 2026-10-18. Every solution builds in Release with no new warnings; the only warnings are pre-existing NuGet audit notices for other packages. No app code changed: the samples' `UseInertiaAntiforgeryCookie()` plus their own unsafe-method validation is still a supported 1.2 pattern, and none use root views outside the content root or a custom app encoder.

The 1.2.0 changelog claims SSR responses are parsed from a pooled buffer at "about half the allocation of 1.1 and faster", plus smaller savings elsewhere. To check that, `InertiaPerformanceTests` gained an `ssr` mode that turns SSR on and answers the adapter with a canned ~24 KB UTF-8 page from an in-memory handler. That exercises the rewritten parser without Node/Bun render time. The three repositories below were measured with 1.1.0 and 1.2.0 alternating three times: 15 rounds of 500 sequential requests per version and mode. Their full integration suites pass on 1.2.0 (116, 50 and 54 tests).

| Repository | ssr alloc B/req 1.1 → 1.2 | ssr latency change | html / json / partial latency change |
| --- | ---: | ---: | --- |
| [react-inertia-sqlite](https://github.com/adzamkomladev/react-inertia-sqlite/blob/main/docs/INERTIA_1_2_UPGRADE.md) | 217027 → 204468 | -1.7% (p 0.820) | 11.8% (p 0.130) / 0.6% (p 0.787) / -3.6% (p 0.049) |
| [svelte-inertia-mongodb](https://github.com/adzamkomladev/svelte-inertia-mongodb/blob/main/docs/INERTIA_1_2_UPGRADE.md) | 160742 → 147821 | -11.9% (p 0.059) | -1.4% (p 0.468) / 2.2% (p 0.983) / -7.7% (p 0.290) |
| [vue-inertia-postgresql](https://github.com/adzamkomladev/vue-inertia-postgresql/blob/main/docs/INERTIA_1_2_UPGRADE.md) | 217929 → 204847 | 2.7% (p 0.130) | -2.2% (p 0.520) / -4.7% (p 0.419) / -1.0% (p 0.468) |

Latency change is the reduction in median round mean (positive is faster); p is a two-sided Mann-Whitney U test on round means.

**Is the improvement real?** Partly. The allocation saving is real and consistent: SSR requests allocate about 12.5–13 KB less (6–8% of the whole request, about 12.5% of the SSR-specific cost). In every repository the 15 rounds of each version do not overlap. The "faster" claim did not show up end to end. No mode improved significantly. The one p < 0.05 result is a 3.6% slower React partial reload, and Svelte SSR was 11.9% slower at p = 0.06. With twelve comparisons, both fit run-to-run noise. Database access, routing and TestServer dominate these requests, so a parser saving of microseconds is too small to measure here. Fewer allocations should mean less GC pressure under concurrent load, but this sequential harness does not measure that. The MVC tag-helper streaming and session/TempData savings do not apply: these are Minimal API apps without those state stores.

Not measured: real Node/Bun SSR, seeded feeds, concurrency and replica scaling. Use the seeded k6 harness for those.
