# Docker acceptance - 2026-10-07

The latest working source, including bulk search reindexing, feed enrichment, indexed cursor bounds, seed ANALYZE and PostgreSQL backup support, built successfully with `docker build -t inertia-acceptance/vue-postgresql:final .`. Image: `sha256:437ebdf33f82399a312fd4a8289c9bc5e28cee1a6aeea4d93db5a4009c403ee1`. Docker Engine: 29.7.2.

Normal, development, and combined search/cache Compose configurations validate with exit code 0. Validation does not start a stack. No local RustFS service was started; normal configuration uses the supplied external storage settings.

A standalone .NET 10 `TimeZoneInfo` probe ran as the runtime image's default app user with its DLL mounted read-only. All checks passed:

| IANA zone | Calendar day / offset | Result |
| --- | --- | --- |
| America/New_York | 2026-03-08 | 23-hour day |
| America/New_York | 2026-11-01 | 25-hour day |
| Australia/Lord_Howe | 2026-10-04 | 23.5-hour day |
| Asia/Kathmandu | 2026-01-01 | +05:45 |

`pg_dump --version` in this exact image reports PostgreSQL 16.15. The previous accepted image's `pg_dump --format=custom --no-password` exported an isolated PostgreSQL 16 test database; its `pg_restore --no-owner --no-acl` restored the archive into a separate empty database. The expected row `42|TechLandier backup restore` was verified. Both probe databases and the local archive were removed. Passwords were supplied through child process environment variables. This verifies runtime client tools and round-trip restore; it does not claim that the scheduled S3 upload/retention job was exercised.

Earlier image attempts captured in-progress source updates and failed compilation; the final build above passed after those owner-provided source fixes. A nonblocking Vue SSR sourcemap warning appeared during the frontend stage; client and SSR assets built successfully.

The final refresh includes the seed follow-up queue CLI, served favicon and mobile navigation fixes. This exact refreshed image passed the four IANA/DST probes again, includes the favicon/template declaration, and reports pg_dump 16.15. The earlier round-trip restore used the same runtime base and client version.
