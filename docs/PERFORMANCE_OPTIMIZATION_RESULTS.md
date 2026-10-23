# Performance optimization results — Inkwell, CarParker, TechLandier

**Before:** Inertia.Net 1.5.1 at `ea0b3d5` (the upgrade baseline).
**After:** `60cc487`, the performance work from
[`PERFORMANCE_OPTIMIZATION_PLAN.md`](PERFORMANCE_OPTIMIZATION_PLAN.md): shared notifications and
Inkwell auth caches, CarParker parallel dashboard queries and single-read lot spots, Inkwell
feed/profile parallel loads, and TechLandier deferred widgets.

This report is the measured companion to the plan. It uses the same host, databases and harness as
[`INERTIA_1_5_UPGRADE.md`](INERTIA_1_5_UPGRADE.md); only the application commit changes.

## 1. What was measured

| Layer | Method |
| --- | --- |
| Every page, public and signed in | `benchmarks/page-probe.ps1`: one warm-up request, then 40 sequential requests per mode; p50/p95/p99 and DB operations |
| Prop names, shapes, shared props and deferred groups | Parsed from the real `data-page` JSON and recorded in the page-probe summaries |
| Public load | k6 `load.js`, mixed HTML / Inertia JSON / partial reloads, 150 req/s and 8 VUs |
| Signed-in load | k6 `auth-load.js`, admin and customer cookies, 50/100 req/s and 8 VUs |
| Process and managed Bun SSR | 250 ms sampler for the .NET process and its Bun child; `dotnet-counters` for GC/allocation/CPU |
| Database | EF Core command log (Inkwell, TechLandier), MongoDB `serverStatus` opcounters (CarParker), `pg_stat_database` |

The page probe is a **single sequential client**, so its `rps` is not a capacity result. The k6
runs are the throughput/load result. Both are reported because they answer different questions:
"what does one page cost?" and "what does the app deliver under load?".

### Environment and data

| Item | Value |
| --- | --- |
| Host | Windows 11 Pro 10.0.26300, Intel Core i5-8265U (4 cores / 8 threads), 15.9 GB |
| Runtime | .NET SDK 10.0.402, ASP.NET Core 10.0.13 |
| Load generator | k6 v1.3.0 |
| Bun | 1.3.14 |
| App mode | Release publish, `ASPNETCORE_ENVIRONMENT=Production`, server GC off, jobs and cron off |
| Inkwell data | 5,000 users / 40,002 posts (demo seed plus the two verification posts already in the local DB) |
| CarParker data | 5 lots / 144 spots / 30 reservations / 13 sessions / 3 refunds |
| TechLandier data | 6 users / 10 categories / 121 products / 400 orders / 31 sales-stat days |

Before and after use the same database contents and the same cache settings. The host runs the app,
database, k6 and counters together, so absolute values are not a capacity guarantee; compare only
same-host deltas. The app was restarted between every run.

### Limitations

- The page probe uses 40 sequential requests per mode. Its p99 is therefore the 40th sample; the
  k6 percentiles are computed over thousands of requests and are the better tail result.
- The page probe is one client, not a virtual-user load. It is the right tool for per-page cost and
  prop metadata, not for RPS or saturation.
- The page probe's app RSS is a running process snapshot and grows with JIT and caches. Use the load
  medians for the memory comparison.
- MongoDB `opcounters` include command/session overhead, so CarParker's fractional ops are not exact
  query counts. Inkwell and TechLandier use EF Core command counts.
- The public k6 workload is anonymous; signed-in results come from `auth-load.js` only.
- The signed-in load sets `User-Agent: k6-benchmark-bot`, so TechLandier's background view tracker
  skips ProductViews writes. The public load keeps the default user-agent and does write views.
- The SSR cache is warmed for the steady-state runs. The `opt-ssr0` runs set the cache lifetime to
  zero so every public HTML visit renders in Bun; that is the Bun component probe.

### Workloads

Public k6 mix (`load.js`):

| App | HTML | Inertia JSON | Partial reload |
| --- | --- | --- | --- |
| Inkwell | feed, page 2, post | feed, post | feed `posts` |
| CarParker | home, lots, lot | lots | lots `lots` |
| TechLandier | home, products, product | home, products | products `products` |

Signed-in k6 mix (`auth-load.js`) covers the main shell and dashboard pages:

| App | Signed-in routes |
| --- | --- |
| Inkwell | `/posts`, post detail, `/analytics`, `/notifications`, `/settings`, `/users`, `/feed`, `/admin/team` |
| CarParker | `/account`, `/account/bookings`, `/lots`, lot detail, `/guard`, `/admin`, `/admin/analytics`, `/admin/bookings` |
| TechLandier | `/account`, `/account/orders`, product detail, `/buy/{id}`, `/admin`, `/admin/analytics`, `/admin/orders`, `/admin/catalog` |

## 2. Page inventory and prop types

Every Inertia response in these samples shares four props: `errors`, `appName` (`Once`), `auth`
and `notifications`. The last two are the interesting ones:

| Prop | Inkwell | CarParker | TechLandier |
| --- | --- | --- | --- |
| `appName` | `Once` | `Once` | `Once` |
| `auth` | per-user cached, 3 min fresh / 1 min stale | cookie claims (prop zero queries; one cookie-validation lookup remains) | cookie claims (prop zero queries; one cookie-validation lookup remains) |
| `notifications` | per-user cached after P0, 5 min / 1 min | per-user cached after P0, 5 min / 1 min | per-user cached after P0, 5 min / 1 min |
| Page caches | analytics summary/series/top posts | admin analytics KPIs/charts | home categories/rails, product related, admin KPIs/charts |
| Deferred groups | post comments; analytics summary/charts | admin analytics KPIs/charts | account widgets; product reviews/related/canReview; buy credit; admin KPIs/charts |

The page probe records the actual prop shape as well. The complete per-page list is in
[`benchmarks/results/`](../benchmarks/results/) (`*-page-probe-*.json`), and
`python benchmarks/analyze-results.py props` prints it as a Markdown table.

Highlights of the shape families:

| Shape | Examples |
| --- | --- |
| Scroll/merge object | `posts`, `users`, `lots`, `bookings`, `products`, `orders` |
| Single entity object | `post`, `lot`, `product`, `order`, `booking`, `ticket`, `detail` |
| Small view model | `filters`, `range`, `stats`, `summary`, `kpis`, `settings`, `profile` |
| Deferred group | `account`: `installment`, `deliveries`, `alerts`; `product`: `reviews`, `related`; `admin`: `kpis`, `charts` |

## 3. Signed-in page results

The tables below are the page probe, medians of 40 sequential requests after one warm-up. DB
operations are per request. "Ops" is EF commands for Inkwell/TechLandier and MongoDB operations for
CarParker. The MongoDB counter includes command/session overhead, so small fractions are expected;
the EF counts are exact.

### Inkwell

Shared caching removes **3 EF commands** from every signed-in page: one `auth` lookup and the two
notification queries. Pages that were already 0-op analytics after the deferred load stay 0-op.

| Page | Mode | p50 before | p50 after | p95 before | p95 after | p99 before | p99 after | ops before | ops after |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `/feed` signed in | HTML | 11.60 | 9.65 | 15.84 | 15.00 | 19.14 | 17.42 | 11.0 | 8.0 |
| `/analytics` shell | HTML | 5.36 | 4.31 | 6.12 | 5.38 | 6.54 | 5.93 | 3.0 | 0.0 |
| `/analytics` full | deferred | 3.36 | 3.15 | 4.86 | 3.63 | 7.00 | 5.07 | 0.0 | 0.0 |
| `/settings` | HTML | 7.64 | 5.27 | 9.74 | 7.55 | 16.06 | 9.02 | 4.0 | 1.0 |
| `/notifications` | HTML | 6.58 | 5.21 | 8.54 | 7.40 | 9.06 | 7.71 | 4.0 | 1.0 |
| `/users` | HTML | 6.12 | 5.11 | 9.08 | 7.64 | 12.63 | 8.61 | 4.0 | 1.0 |
| `/bookmarks` | HTML | 117.24 | 116.27 | 191.83 | 190.88 | 251.32 | 207.57 | 7.0 | 4.0 |
| `/me/posts` | HTML | 78.09 | 78.62 | 101.85 | 107.91 | 102.74 | 113.53 | 8.0 | 5.0 |
| `/me/archives` | HTML | 5.97 | 5.36 | 7.47 | 6.20 | 8.06 | 6.87 | 5.0 | 2.0 |
| post analytics | HTML | 5.77 | 4.95 | 7.49 | 6.19 | 8.20 | 7.69 | 4.0 | 1.0 |
| `/posts/{id}/edit` | HTML | 5.73 | 4.98 | 8.11 | 6.35 | 8.83 | 7.64 | 5.0 | 2.0 |
| `/posts/create` | HTML | 5.39 | 5.02 | 6.51 | 6.13 | 7.40 | 6.65 | 3.0 | 0.0 |
| `/admin/team` | HTML | 10.75 | 8.86 | 13.88 | 13.10 | 14.21 | 16.15 | 5.0 | 2.0 |

The public feed is unchanged (same 3 commands): `/posts` HTML 8.65 → 10.24 ms p50 in this
single-client probe, which is within the run-to-run spread; the k6 public run is the load result.

The public profile page is a finding: it went from 5 to 6 commands because the parallel profile
rewrite starts the timeline before the profile is known and therefore repeats one user lookup. Its
p50 is flat (75.69 → 75.11 ms) and p95 moved 93.99 → 119.68 ms in this 40-request probe. That is not
a win; it should be reviewed before keeping the parallelization as-is. The signed-in profile page
went from 8 to 6 ops because it also loses the two shared notification queries.

### CarParker

| Page | Mode | p50 before | p50 after | p95 before | p95 after | p99 before | p99 after | ops before | ops after |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `/account` | HTML | 24.81 | 21.99 | 28.66 | 25.51 | 30.16 | 26.89 | 10.3 | 9.5 |
| `/account` | JSON | 23.42 | 17.98 | 26.90 | 19.65 | 32.50 | 20.24 | 10.5 | 9.3 |
| `/account/bookings` | JSON | 16.50 | 13.37 | 20.94 | 14.89 | 22.11 | 15.81 | 6.5 | 5.3 |
| `/account/bookings/{id}` | HTML | 18.76 | 16.12 | 27.20 | 17.55 | 31.14 | 18.54 | 9.3 | 8.4 |
| `/account/payments` | HTML | 20.20 | 15.54 | 27.04 | 17.34 | 27.44 | 18.43 | 7.5 | 6.3 |
| `/account/insurance` | HTML | 17.03 | 12.96 | 18.50 | 15.28 | 19.61 | 18.14 | 6.3 | 5.3 |
| `/account/alerts` | HTML | 10.73 | 7.36 | 12.04 | 8.62 | 13.86 | 12.33 | 3.3 | 2.3 |
| `/guard` | HTML | 17.76 | 10.38 | 19.56 | 11.93 | 20.86 | 13.29 | 7.3 | 5.3 |
| `/guard` | JSON | 16.24 | 8.52 | 18.77 | 9.88 | 21.59 | 10.88 | 7.3 | 5.3 |
| `/admin` | HTML | 18.36 | 14.72 | 20.36 | 17.10 | 21.08 | 20.18 | 5.3 | 4.6 |
| `/admin/bookings` | HTML | 26.58 | 18.80 | 31.62 | 21.01 | 34.60 | 21.78 | 7.6 | 6.3 |
| `/admin/sessions` | HTML | 24.35 | 13.27 | 35.89 | 15.28 | 41.51 | 16.15 | 6.5 | 5.3 |
| `/admin/insurance` | HTML | 21.26 | 12.81 | 25.54 | 14.64 | 28.85 | 16.62 | 6.3 | 5.3 |
| `/admin/analytics` | HTML | 10.48 | 8.91 | 11.75 | 13.18 | 12.72 | 15.78 | 3.3 | 2.5 |
| `/lots/{id}` | HTML | 11.05 | 7.25 | 13.15 | 9.96 | 14.87 | 13.60 | 2.3 | 2.3 |
| `/lots/{id}` | JSON | 10.21 | 5.97 | 12.74 | 9.10 | 13.32 | 10.45 | 2.3 | 2.3 |
| `/lots/{id}/book` | HTML | 20.75 | 7.48 | 27.50 | 9.27 | 30.39 | 10.02 | 4.3 | 3.3 |

The public lot list is the one public page that moved the wrong way in the page probe: p50 9.86 →
13.59 ms and JSON 8.80 → 13.47 ms. No code on that page changed, so this is most likely app/JIT
state or Mongo cache noise; the public k6 run is the control.

### TechLandier

TechLandier has exact EF command counts. P0 removes the two notification queries from every
signed-in page; P2 moves the account, product and buy widgets to deferred groups.

| Page | Mode | p50 before | p50 after | p95 before | p95 after | p99 before | p99 after | ops before | ops after |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `/account` | HTML | 14.58 | 9.74 | 18.46 | 10.89 | 36.10 | 11.74 | 8 | 3 |
| `/account` | JSON | 11.56 | 9.29 | 14.98 | 12.40 | 15.42 | 22.92 | 8 | 3 |
| `/account` | deferred | new | 13.60 | new | 16.95 | new | 18.85 | new | 4 |
| `/account/orders` | HTML | 13.16 | 9.20 | 16.64 | 10.10 | 21.93 | 10.31 | 4 | 2 |
| `/account/orders/{number}` | HTML | 16.23 | 14.62 | 18.94 | 17.05 | 21.55 | 17.13 | 10 | 8 |
| `/account/credit` | HTML | 15.09 | 10.77 | 18.18 | 12.16 | 19.93 | 13.40 | 5 | 3 |
| `/account/care/repairs` | HTML | 12.14 | 9.44 | 16.03 | 10.91 | 18.03 | 12.61 | 5 | 3 |
| `/account/support` | HTML | 12.45 | 8.87 | 14.59 | 10.23 | 17.14 | 10.38 | 4 | 2 |
| `/account/alerts` | HTML | 13.45 | 10.68 | 15.94 | 12.44 | 16.17 | 13.43 | 5 | 3 |
| `/buy/{id}` | HTML | 15.60 | 10.97 | 18.60 | 12.19 | 21.32 | 12.83 | 7 | 4 |
| `/buy/{id}` | deferred | new | 12.04 | new | 14.28 | new | 15.88 | new | 5 |
| `/products/{slug}` signed in | HTML | 16.84 | 9.99 | 21.61 | 11.16 | 40.78 | 17.12 | 5 | 2 |
| `/products/{slug}` signed in | deferred | 14.32 | 14.22 | 16.35 | 16.50 | 19.65 | 16.70 | 4 | 4 |
| `/admin` | HTML | 9.45 | 6.19 | 10.80 | 6.75 | 11.13 | 7.66 | 3 | 1 |
| `/admin/analytics` | HTML | 9.89 | 6.29 | 10.82 | 7.18 | 10.95 | 7.45 | 3 | 1 |
| `/admin/catalog` | HTML | 13.66 | 10.11 | 17.01 | 12.48 | 18.41 | 13.58 | 5 | 3 |
| `/admin/orders` | HTML | 13.10 | 9.09 | 15.02 | 10.22 | 16.20 | 11.43 | 4 | 2 |
| `/admin/orders/{number}` | HTML | 16.59 | 14.76 | 19.31 | 17.80 | 20.98 | 18.96 | 12 | 10 |
| `/admin/care` | HTML | 12.01 | 8.74 | 13.66 | 9.74 | 15.47 | 10.29 | 4 | 2 |
| `/admin/support` | HTML | 12.56 | 8.86 | 18.26 | 10.05 | 18.52 | 10.49 | 4 | 2 |
| `/admin/settings` | HTML | 10.89 | 7.64 | 14.27 | 8.97 | 17.84 | 9.34 | 4 | 2 |
| `/admin/team` | HTML | 13.08 | 7.92 | 14.64 | 9.35 | 16.49 | 9.70 | 4 | 2 |
| `/orders/{number}` | HTML | 15.74 | 12.94 | 19.01 | 15.46 | 19.98 | 18.36 | 7 | 5 |

Public TechLandier pages:

| Page | Mode | p50 before | p50 after | p95 before | p95 after | p99 before | p99 after | ops before | ops after |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `/` | HTML | 8.70 | 6.94 | 11.20 | 8.30 | 11.72 | 9.44 | 1 | 1 |
| `/products` | HTML | 8.56 | 7.27 | 10.31 | 8.89 | 12.85 | 9.27 | 1 | 1 |
| `/products` | JSON | 6.68 | 8.09 | 7.97 | 11.07 | 8.41 | 12.38 | 1 | 1 |
| `/products/{slug}` anonymous | HTML | 6.82 | 6.73 | 9.19 | 8.55 | 10.59 | 9.68 | 1 | 1 |
| `/products/{slug}` anonymous | deferred | 8.75 | 7.39 | 10.42 | 8.61 | 10.63 | 9.32 | 2 | 2 |
| `/deals` | HTML | 7.58 | 9.06 | 8.91 | 12.00 | 9.36 | 14.45 | 1 | 1 |
| `/about` | HTML | 4.70 | 4.68 | 5.94 | 7.09 | 7.23 | 9.88 | 0 | 0 |
| `/support/policy` | HTML | 6.57 | 6.85 | 7.72 | 9.45 | 8.44 | 11.54 | 1 | 1 |

`/deals` and the `/products` JSON variant are the only public rows that moved materially the wrong
way; neither page's server code changed, so use the k6 public run as the control.

## 4. Public anonymous load

Medians of two alternating rounds; 10 s warmup, 30 s measured.

### 150 requests/second

| App | Version | RPS | p50 ms | p95 ms | p99 ms | App RSS MB | App CPU % | Alloc MB/s | GC pause % | Bun RSS MB | Bun CPU % | SSR cache |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Inkwell | 1.5.1 | 149.66 | 4.80 | 10.61 | 14.28 | 164.2 | 13.4 | 23.5 | 1.54 | 92.5 | 0.03 | 100% |
| Inkwell | 1.5.1-opt | 149.91 | 4.12 | 8.90 | 11.94 | 163.0 | 11.7 | 23.6 | 1.38 | 90.8 | 0.04 | 100% |
| CarParker | 1.5.1 | 149.47 | 13.24 | 39.85 | 87.81 | 135.3 | 18.8 | 26.3 | 3.36 | 75.1 | 0.04 | 100% |
| CarParker | 1.5.1-opt | 149.75 | 10.03 | 17.98 | 24.47 | 133.1 | 15.6 | 22.9 | 1.79 | 75.2 | 0.07 | 100% |
| TechLandier | 1.5.1 | 149.42 | 8.17 | 27.21 | 73.99 | 167.1 | 16.3 | 10.7 | 1.09 | 89.4 | 0.06 | 100% |
| TechLandier | 1.5.1-opt | 149.64 | 9.12 | 32.05 | 101.68 | 168.0 | 17.4 | 10.7 | 0.91 | 89.6 | 0.04 | 100% |

CarParker is the noisy one. Its two baseline rounds ranged p95 25.97–53.73 ms and p99 37.67–137.95 ms;
the after rounds ranged p95 16.53–19.44 ms and p99 20.90–28.04 ms. The medians are shown above, but
the range is the honest comparison. The baseline round with p99 138 ms was not reproduced.

### 8 virtual users (saturation)

| App | Version | RPS | p50 ms | p95 ms | p99 ms | App RSS MB | App CPU % | Alloc MB/s | GC pause % | Bun RSS MB | Bun CPU % |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Inkwell | 1.5.1 | 452.89 | 15.51 | 29.87 | 39.27 | 170.8 | 49.6 | 71.1 | 11.25 | 91.7 | 0.04 |
| Inkwell | 1.5.1-opt | 424.22 | 16.29 | 32.77 | 44.43 | 168.8 | 46.6 | 66.6 | 11.00 | 92.2 | 0.07 |
| CarParker | 1.5.1 | 245.56 | 30.10 | 48.45 | 60.15 | 136.9 | 34.5 | 43.0 | 10.29 | 74.9 | 0.04 |
| CarParker | 1.5.1-opt | 261.43 | 27.97 | 46.78 | 59.70 | 136.4 | 34.4 | 39.9 | 9.46 | 74.0 | 0.04 |
| TechLandier | 1.5.1 | 305.02 | 22.28 | 46.94 | 73.03 | 171.0 | 35.2 | 21.7 | 5.16 | 89.6 | 0.06 |
| TechLandier | 1.5.1-opt | 302.26 | 23.08 | 45.80 | 65.04 | 171.3 | 35.9 | 21.4 | 4.99 | 89.7 | 0.07 |

Inkwell's saturation RPS moved −6% and p95 +10%, but every public request path is unchanged. This is
the same host/thermal spread that makes the low-load Inkwell result look better; it is not a code
regression attributable to the optimization. CarParker's ceiling moved +6% with a slightly lower
tail. The public k6 mix does not include the Inkwell profile page or the TechLandier deals page.

### Per-route medians at 150 requests/second

Inkwell:

| Route | p50 before | p50 after | p95 before | p95 after | p99 before | p99 after |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| feed_html | 7.07 | 6.29 | 12.59 | 10.79 | 17.23 | 13.95 |
| feed_page2_html | 7.30 | 6.42 | 12.80 | 10.74 | 16.55 | 13.09 |
| post_html | 5.66 | 5.08 | 9.82 | 8.12 | 12.59 | 11.19 |
| feed_json | 2.98 | 2.70 | 6.37 | 4.85 | 8.30 | 7.03 |
| post_json | 1.89 | 1.70 | 4.04 | 3.07 | 7.05 | 4.84 |
| feed_partial | 2.95 | 2.63 | 6.42 | 5.13 | 10.06 | 7.79 |

CarParker:

| Route | p50 before | p50 after | p95 before | p95 after | p99 before | p99 after |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| home_html | 13.68 | 11.80 | 35.29 | 20.37 | 66.06 | 26.51 |
| lots_html | 13.87 | 11.93 | 39.89 | 20.16 | 88.45 | 26.14 |
| lot_html | 15.97 | 7.73 | 45.11 | 13.32 | 94.06 | 19.12 |
| lots_json | 9.13 | 7.68 | 32.92 | 13.67 | 86.94 | 19.66 |
| lots_partial | 8.98 | 7.56 | 36.37 | 13.68 | 70.53 | 21.42 |

The public lot-detail page is the one route with a clear code-level cause: P3 reads the lot's spots
once and in parallel with the lot, instead of card aggregation followed by a second spots read.
The other CarParker public routes were not changed; their lower medians show that the host was
quieter during the after runs as well.

TechLandier:

| Route | p50 before | p50 after | p95 before | p95 after | p99 before | p99 after |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| home_html | 8.94 | 9.85 | 28.79 | 37.80 | 83.56 | 118.18 |
| products_html | 9.97 | 10.96 | 31.88 | 32.81 | 72.15 | 117.68 |
| product_html | 9.49 | 10.42 | 31.15 | 40.92 | 84.27 | 102.16 |
| home_json | 3.30 | 3.46 | 11.59 | 17.27 | 53.63 | 74.64 |
| products_json | 4.28 | 4.83 | 21.12 | 18.28 | 43.21 | 64.69 |
| products_partial | 4.33 | 4.64 | 15.62 | 20.97 | 63.33 | 94.99 |

TechLandier's public low-load run is the noisiest result. Its two baseline rounds ranged p95
17.04–37.37 ms and p99 51.36–96.61 ms; the after rounds ranged p95 23.01–41.09 ms and p99
65.27–138.09 ms. The p50 difference is about 1 ms. The only public page whose code changed is
product detail (P2 deferred `reviews`, `related` and `canReview`); its page-probe p50 is flat
(6.82 → 6.73 ms) and its load p50 +0.9 ms is inside the host spread. This is not a confirmed
public regression, but it is also not a win; do not attribute the tail difference to the change.
At saturation the after run was −0.9% RPS with p95 −2% and p99 −11%.

## 5. Signed-in load

One 30 s round per version, 10 s warmup. The mix uses the admin and customer demo sessions and the
`k6-benchmark-bot` user-agent, so TechLandier's background view writer does not add ProductViews
writes. Setup/login requests are a tiny fraction of the measured requests and do not move the
percentiles.

### 100 requests/second

| App | Version | RPS | p50 ms | p95 ms | p99 ms | App RSS MB | App CPU % | Alloc MB/s | GC pause % | Bun RSS MB | Bun CPU % | SSR cache |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Inkwell | 1.5.1 | 98.68 | 8.28 | 22.40 | 46.81 | 193.0 | 14.4 | 24.9 | 2.13 | 91.4 | 0.04 | 100% |
| Inkwell | 1.5.1-opt | 98.92 | 6.43 | 14.38 | 25.40 | 186.6 | 11.2 | 16.8 | 0.82 | 90.1 | 0.05 | 100% |
| CarParker | 1.5.1 | 97.42 | 58.73 | 518.41 | 692.91 | 148.0 | 25.4 | 34.8 | 7.34 | 74.3 | 0.06 | 100% |
| CarParker | 1.5.1-opt | 98.89 | 22.61 | 48.18 | 96.48 | 145.0 | 21.2 | 28.7 | 3.82 | 74.2 | 0.03 | 100% |
| TechLandier | 1.5.1 | 96.31 | 36.08 | 652.09 | 999.89 | 184.5 | 24.4 | 28.5 | 9.24 | 83.0 | 0.06 | 100% |
| TechLandier | 1.5.1-opt | 97.03 | 19.83 | 270.66 | 675.94 | 179.2 | 19.3 | 18.5 | 4.11 | 83.0 | 0.02 | 100% |

### 8 virtual users

| App | Version | RPS | p50 ms | p95 ms | p99 ms | App RSS MB | App CPU % | Alloc MB/s | GC pause % | Bun RSS MB | Bun CPU % | SSR cache |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Inkwell | 1.5.1 | 280.99 | 25.09 | 51.97 | 78.18 | 199.4 | 44.4 | 69.9 | 12.40 | 89.5 | 0.05 | 100% |
| Inkwell | 1.5.1-opt | 271.69 | 25.46 | 56.57 | 83.41 | 198.8 | 39.5 | 45.7 | 11.16 | 89.8 | 0.05 | 100% |
| CarParker | 1.5.1 | 87.79 | 80.82 | 159.02 | 226.82 | 144.1 | 24.0 | 31.9 | 7.97 | 75.1 | 0.09 | 100% |
| CarParker | 1.5.1-opt | 134.83 | 55.88 | 100.39 | 132.57 | 146.7 | 29.7 | 39.3 | 9.40 | 74.3 | 0.04 | 100% |
| TechLandier | 1.5.1 | 73.03 | 93.57 | 211.71 | 310.05 | 167.1 | 19.9 | 21.9 | 6.26 | 83.1 | 0.08 | 100% |
| TechLandier | 1.5.1-opt | 164.30 | 42.68 | 91.04 | 142.12 | 174.2 | 29.5 | 29.6 | 5.40 | 83.0 | 0.05 | 100% |

Inkwell's signed-in low-load result is the cleanest cache win: p50 −22%, p95 −36%, p99 −46%,
allocation −33% and GC pause −62%, with RSS −3%. At 8 VUs the delivered rate is 3% lower and the
tail is 7–9% higher, but allocation is still 35% lower and CPU 11% lower; the throughput difference
is within the host/thermal spread seen on the public runs. The signed-in mix is deliberately
dashboard-heavy, so it is not expected to match the public 450 req/s ceiling.

CarParker's 100 req/s baseline was already above its signed-in ceiling: it dropped 33 iterations,
p50 was 58.73 ms and p95 was 518 ms. After the change it delivered 98.9 req/s with **zero dropped
iterations**, p50 22.61 ms and p95 48.18 ms. At 8 VUs the ceiling rose from 87.8 to 134.8 req/s
(+54%), with p50 −31% and p95 −37%. This is the largest capacity change in the suite. The
100 req/s database rate also fell from 562.9 to 464.9 Mongo queries/s because the shared notification
and dashboard queries were removed.

TechLandier's 100 req/s run is close to its signed-in ceiling: the baseline dropped 49 iterations
and the after run 20, so p95/p99 are queue/tail results, not low-load results. The reliable signals
there are p50 (36.08 → 19.83 ms), database statements (1058.9 → 480.6/s, −55%), allocation
(28.5 → 18.5 MB/s) and GC pause (9.24% → 4.11%). At 8 VUs the ceiling rose from 73.0 to 164.3 req/s
(+125%), p50 fell 54% and p95 fell 57%.

### TechLandier signed-in low load (50 requests/second)

| Version | RPS | p50 ms | p95 ms | p99 ms | App RSS MB | App CPU % | Alloc MB/s | GC pause % |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1.5.1 | 49.5 | 20.40 | 40.67 | 92.99 | 171.7 | 13.0 | 14.1 | 1.05 |
| 1.5.1-opt | 49.6 | 10.20 | 21.20 | 37.14 | 167.7 | 8.2 | 9.0 | 0.13 |

At a genuinely low signed-in rate the result is unambiguous: p50 −50%, p95 −48%, p99 −60%,
database statements 557.6 → 248.2/s (−55%), allocation −36%, GC pause −87%, RSS −2%. This is the
post-cache, post-defer TechLandier.

## 6. Bun SSR and database counters

### Bun SSR off the cache (80 requests/second)

Every public HTML visit renders in Bun here; `Inertia__SsrCacheSeconds=0`. TechLandier at 80 req/s
is past its comfortable SSR queue (the earlier report used 40 req/s for that sample), so its absolute
p50 is a queue result, not a render cost; the before/after direction is still valid because both
sides use the same rate.

| App | Version | RPS | p50 ms | p95 ms | p99 ms | App CPU % | Bun CPU % | Bun RSS MB | SSR renders |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Inkwell | 1.5.1 | 79.8 | 7.43 | 20.50 | 30.71 | 9.03 | 3.74 | 150.3 | 1288 |
| Inkwell | 1.5.1-opt | 80.0 | 7.82 | 20.50 | 28.29 | 6.77 | 3.91 | 151.4 | 1351 |
| CarParker | 1.5.1 | 79.7 | 20.27 | 85.47 | 250.69 | 15.64 | 4.89 | 136.1 | 1686 |
| CarParker | 1.5.1-opt | 79.5 | 17.80 | 42.84 | 78.07 | 13.56 | 5.27 | 136.0 | 1688 |
| TechLandier | 1.5.1 | 79.3 | 263.56 | 602.45 | 885.63 | 12.43 | 9.29 | 164.4 | 1595 |
| TechLandier | 1.5.1-opt | 79.1 | 80.24 | 470.15 | 560.12 | 12.34 | 10.65 | 169.3 | 1589 |

The deterministic signal is again the render count: the same number of pages was rendered, so the
Bun process is not being bypassed. TechLandier's p50 fell because the product page now sends its
reviews/related/canReview work as deferred props, so the server-rendered first response carries
less data and the queue drains sooner; CarParker's p50/p95 fell with P3's single spot read.
Inkwell is flat, as expected.

At TechLandier's earlier 40 req/s component rate, where the SSR queue is not the constraint, the
result is 18.23 → 19.39 ms p50, 42.36 → 38.96 ms p95, 67.27 → 52.91 ms p99, with Bun at 5.92 →
5.77% CPU and 167.0 → 165.0 MB RSS (786 → 798 renders). That is a flat p50 and a better tail, which
matches the page-probe result; the 80 req/s p50 gain above is mostly queue draining, not a claim
that the Bun renderer became three times faster.

With the SSR cache on (the steady-state runs), the Bun process is at **0.03–0.07% CPU** and
**74–92 MB RSS** in every app; SSR outcomes are 100% `cache`. That is the same shape as the 1.5.1
upgrade report: the Bun process is idle when the cache hits.

### Database counters

| App | Signal | Before | After |
| --- | --- | ---: | ---: |
| Inkwell | EF commands, anonymous `/posts` | 3/request | 3/request |
| Inkwell | EF commands, signed-in shared props | 3/request (auth + 2 notifications) | 0/request after the cache is warm |
| CarParker | Mongo query ops at 150 req/s | 188.4/s | 188.1/s |
| CarParker | Mongo query ops at 80 req/s, SSR cache off | 100.1/s | 99.6/s |
| TechLandier | PostgreSQL statements at 150 req/s | 306.0/s | 311.1/s (+1.7%) |
| TechLandier | PostgreSQL statements at 80 req/s, SSR cache off | 161.6/s | 161.5/s |
| TechLandier | PostgreSQL statements, signed-in 100 req/s | 1058.9/s | 480.6/s (−55%) |
| TechLandier | PostgreSQL statements, signed-in 50 req/s | 557.6/s | 248.2/s (−55%) |

The public query rates are unchanged within 2%; the only public code change that removes database
work is the CarParker lot-detail duplicate spot read, and it is a small part of the mixed public
traffic. The signed-in page probe is the exact per-page statement count.

## 7. Findings and follow-ups

1. **P0 is the largest and most consistent win.** Inkwell drops 3 EF commands from every signed-in
   page (auth + two notification queries); CarParker and TechLandier drop the two notification
   queries. At 100 req/s CarParker goes from 33 dropped iterations, p50 58.7 ms and p95 518 ms to
   zero drops, p50 22.6 ms and p95 48.2 ms. TechLandier's 50 req/s p50 halves and its signed-in
   ceiling more than doubles at 8 VUs. The caches are the right first optimization.
2. **P1 CarParker dashboards are a real latency win.** `/account` p50 24.81 → 21.99 ms and
   `/guard` 17.76 → 10.38 ms in the single-client probe; `/admin/sessions` 24.35 → 13.27 ms. The
   ceiling and tail improvements at load are larger than the ops delta alone would suggest because
   the parallel stages stop stacking Mongo round trips.
3. **P2 TechLandier defers work as intended.** `/account` first response falls from 8 to 3 EF
   commands and p50 from 14.58 to 9.74 ms; the deferred group still costs 4 ops when requested.
   `/buy` falls from 7 to 4 ops and 15.60 → 10.97 ms. Signed-in product detail falls from 5 to 2
   ops and 16.84 → 9.99 ms. Admin `/admin` and `/admin/analytics` fall from 3 to 1 op.
4. **P2 Inkwell auth/feed parallelization is mixed.** The signed-in feed drops 3 ops and p50
   11.60 → 9.65 ms; the signed-in profile drops 2 ops but its p50 is flat. The public profile adds
   one op and does not improve p50/p95. Review the extra user lookup before keeping the public
   profile parallelization; the cache work is independent and should stay.
5. **P3 CarParker lot pages are a clear public win.** Lot detail p50 11.05 → 7.25 ms (−34%), JSON
   10.21 → 5.97 ms; booking 20.75 → 7.48 ms (−64%); at 150 req/s lot_html 15.97 → 7.73 ms. The
   server-level Mongo op counter does not resolve the one-query removal because it also counts
   command/session operations, but the latency and code change agree.
6. **Public tail results are noisy.** Inkwell's 150 req/s run looks 14–16% faster with no public
   code change; its 8-VU run looks 6% slower. CarParker's baseline p99 ranged 38–138 ms and the
   after p99 21–28 ms. TechLandier's public p99 ranged 51–97 ms before and 65–138 ms after, with a
   ~1 ms p50 difference. Treat public deltas outside the directly changed routes as host spread;
   the directly changed routes (CarParker lot detail, TechLandier product defer) are supported by
   page-level evidence.
7. **Memory is flat.** Steady-state app RSS moves −3% to +1% in the load runs; CarParker/Inkwell
   are slightly lower, TechLandier slightly higher. The caches are small; this is not a memory
   optimization. Bun RSS is flat at 74–92 MB with the cache on.
8. **The Bun process is not the bottleneck.** With the SSR cache on it is idle at 0.03–0.07% CPU;
   with the cache off it uses 3.7–10.7% CPU. TechLandier at 80 req/s is queue-bound, and the after
   run's p50 263.6 → 80.2 ms is the deferred-props effect. If TechLandier needs a clean SSR
   component number, use 40 req/s as in the earlier report.

**Recommendation:** keep P0, P1, P2 (TechLandier/Inkwell auth) and P3. Before keeping the Inkwell
public profile parallelization, remove the repeated user lookup or measure it with a dedicated
profile load. Do not add public-page caching from these results; the public tail is host-noisy and
the directly changed public pages already improved. If public p99 matters operationally, run four
or more alternating rounds on an otherwise idle host.

## 8. Reproduce

```powershell
# Build artifacts/1.5.1 (baseline) and artifacts/1.5.1-opt (after) as in the Dockerfile:
# publish Release, then copy ssr/ and wwwroot/build beside the DLL.
pwsh benchmarks/page-probe.ps1 -App inkwell -Version 1.5.1 -Requests 40
pwsh benchmarks/page-probe.ps1 -App inkwell -Version 1.5.1-opt -Requests 40
pwsh benchmarks/optimization-matrix.ps1 -App inkwell -Before 1.5.1 -After 1.5.1-opt -Rounds 2 -Auth -Ssr0
python benchmarks/analyze-results.py page
python benchmarks/analyze-results.py load
python benchmarks/analyze-results.py props
```

Raw summaries are in [`benchmarks/results/`](../benchmarks/results/). The page-probe files contain
the per-page percentiles, prop shapes, deferred groups and process/SSR samples; the load files
contain k6 percentiles, per-route percentiles, process samples, `dotnet-counters` aggregates and
database deltas.

## Appendix A. Full page and prop inventory

The shared `errors`, `appName`, `auth` and `notifications` props are omitted from the page-specific
column. `product_auth` uses the same `products/Show` page; it is the signed-in variant measured for
the `canReview` move.

### Inkwell

| Page | Component | Page props | Deferred groups |
| --- | --- | --- | --- |
| login | Auth/Login | - | - |
| register | Auth/Register | - | - |
| feed | Posts/Index | posts, q | - |
| feed_page2 | Posts/Index | posts, q | - |
| post | Posts/Show | post, liked, canEdit | comments: comments |
| users | Users/Index | users, filters | - |
| profile | Users/Show | user, posts | - |
| feed_auth | Feed/Index | posts, mode, title, filters | - |
| bookmarks | Feed/Index | posts, mode, title, filters | - |
| me_posts | Feed/Index | posts, mode, title, filters | - |
| me_archives | Feed/Index | posts, mode, title, filters | - |
| analytics | Analytics/Index | range | summary: summary; charts: series, topPosts |
| post_analytics | Posts/Analytics | post, range | summary: summary; charts: series |
| settings | Settings/Index | profile, preferences | - |
| notifications | Notifications/Index | items | - |
| users_auth | Users/Index | users, filters | - |
| profile_auth | Users/Show | user, posts | - |
| post_edit | Posts/Edit | post | - |
| post_create | Posts/Create | - | - |
| admin_team | Admin/Team | members | - |

`/posts/{id}/comments` is a cursor JSON endpoint, not an Inertia page, so it is not in this table.

### CarParker

| Page | Component | Page props | Deferred groups |
| --- | --- | --- | --- |
| login | Auth/Login | returnUrl | - |
| register | Auth/Register | - | - |
| home | Home | lots | - |
| lots | lots/Index | lots, filters | - |
| lot | lots/Show | lot, spots, tariff, calendar | - |
| about | about/Policy | doc | - |
| policies | about/Policy | doc | - |
| account | account/Index | account, lots | - |
| account_bookings | account/Bookings | bookings | - |
| account_booking | account/Booking | booking, payment, refund, sessions, policy | - |
| account_payments | account/Payments | payments, refunds | - |
| account_insurance | account/Insurance | policies, claims | - |
| account_alerts | account/Alerts | alerts | - |
| account_profile | account/Preferences | profile, preferences | - |
| account_preferences | account/Preferences | profile, preferences | - |
| lot_book | lots/Book | lot, tariff, spots, prefillSpot | - |
| guard | guard/Console | lots, lotId, spots, queue, openSessions, grace | - |
| guard_board | guard/Console | boardOnly, lots, openSessions, exitedSessions | - |
| guard_incidents | guard/Incidents | lots, filterLot, filterStatus, query, incidents, page, hasMore, kinds | - |
| admin | admin/Index | overview | - |
| admin_lots | admin/Lots | lots | - |
| admin_lot | admin/Lot | lot, spots, upcoming | - |
| admin_bookings | admin/Bookings | bookings, page, hasMore, statusFilter, query | - |
| admin_booking | admin/Booking | booking | - |
| admin_sessions | admin/Sessions | openSessions, openPage, openHasMore, openCount, exitedSessions, exitedPage, exitedHasMore, exitedCount, query | - |
| admin_refunds | admin/Refunds | pending, pendingPage, pendingHasMore, decided, decidedPage, decidedHasMore | - |
| admin_insurance | admin/Insurance | policies, policiesPage, policiesHasMore, claims, claimsPage, claimsHasMore | - |
| admin_claim | admin/Claim | detail | - |
| admin_incidents | admin/Incidents | lots, filterLot, filterStatus, query, incidents, page, hasMore, kinds | - |
| admin_users | admin/Users | users, page, hasMore, query, roleFilter | - |
| admin_user | admin/User | detail | - |
| admin_analytics | admin/Analytics | days, lotFilter, lots | kpis: kpis; charts: series, topLots, refundMix |
| admin_team | admin/Team | members | - |

### TechLandier

| Page | Component | Page props | Deferred groups |
| --- | --- | --- | --- |
| login | Auth/Login | returnUrl | - |
| register | Auth/Register | - | - |
| home | Home | categories, deals, arrivals, settings | - |
| products | products/Index | products, filters, categories | - |
| product / product_auth | products/Show | product | reviews: reviews, canReview; related: related |
| deals | deals/Index | products, sort | - |
| about | pages/About | - | - |
| policy | pages/Policy | zones | - |
| order_track | orders/Track | token, order, events, delivery | - |
| account | account/Index | stats, recentOrders | installment: nextInstallment; deliveries: openDeliveries; alerts: unreadAlerts |
| account_orders | account/Orders | orders | - |
| account_order | account/Order | order, events, delivery, plan, warranties, insurances, reviewSlugs | - |
| account_credit | account/Credit | plans, blocked | - |
| account_warranties | account/Warranties | warranties | - |
| account_insurance | account/Insurance | policies, attachable | - |
| account_repairs | account/Repairs | repairs, categories | - |
| account_repair | account/Repairs | repair, history, repairs, categories | - |
| account_support | account/Support | tickets, categories | - |
| account_alerts | account/Alerts | alerts, unreadCount | - |
| account_profile | account/Preferences | profile, preferences | - |
| account_preferences | account/Preferences | profile, preferences | - |
| buy | products/Buy | product, qty, zones, settings, insure, bnpl | credit: creditState |
| admin | admin/Index | range | kpis: kpis, revenueSeries, lowStock, attention |
| admin_analytics | admin/Analytics | range | kpis: kpis; charts: series, topProducts, categoryShare |
| admin_catalog | admin/Catalog | products, q, categories, editing | - |
| admin_orders | admin/Orders | orders, statusFilter, q | - |
| admin_order | admin/Order | order, events, delivery, customer, payments, plan, activeRiders, warranties, insurances | - |
| admin_deliveries | admin/Deliveries | deliveries, riders, zones | - |
| admin_credit | admin/Credit | plans, statusFilter | - |
| admin_plan | admin/Plan | detail | - |
| admin_care | admin/Care | tab, warrantyClaims, insuranceClaims, repairs | - |
| admin_care_warranty | admin/CareDetail | detail | - |
| admin_care_repair | admin/CareDetail | detail | - |
| admin_support | admin/Support | tickets, filter | - |
| admin_support_ticket | admin/Support | ticket, filter | - |
| admin_reviews | admin/Reviews | reviews, state | - |
| admin_settings | admin/Settings | settings, timeZones | - |
| admin_team | admin/Team | members | - |

The demo database has no insurance-claim row, so `/admin/care/insurance-claims/{id}` was skipped;
`/admin/care/repairs/{id}` uses the customer repair row as its id.
