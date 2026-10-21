# Docker acceptance - 2026-10-07

The final working source, including search reindexing, reduced feed enrichment queries, indexed cursor bounds and seed ANALYZE, built successfully with `docker build -t inertia-acceptance/react-sqlite:final .`. Image: `sha256:6a18076285e577bd900988a5b186cb24c68e49e178a07b033a958328615dc118`. Docker Engine: 29.7.2. Client and SSR assets were included in the image.

Normal, development, and combined search/cache Compose configurations validate with exit code 0. Validation does not start a stack. No local RustFS service was started; normal configuration uses the supplied external storage settings.

A standalone .NET 10 `TimeZoneInfo` probe ran as the runtime image's default app user with its DLL mounted read-only. All checks passed:

| IANA zone | Calendar day / offset | Result |
| --- | --- | --- |
| America/New_York | 2026-03-08 | 23-hour day |
| America/New_York | 2026-11-01 | 25-hour day |
| Australia/Lord_Howe | 2026-10-04 | 23.5-hour day |
| Asia/Kathmandu | 2026-01-01 | +05:45 |

These checks build the deployable image and verify its timezone support; they do not start the application or claim a full Compose deployment passed. Database, optional-service outage, and endpoint checks are recorded separately by the repository acceptance documentation.

The refreshed image includes the durable CLI profile-job flush, bounded Cron delays, native search worker guard and served favicon. Its favicon file and template declaration were checked inside the runtime image; the IANA/DST probe was repeated successfully against this exact image.
