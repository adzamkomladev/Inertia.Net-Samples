# Docker acceptance - 2026-10-07

**Provenance:** recorded on the Vue MongoDB predecessor of this Svelte sample; the Svelte port keeps the same multi-stage Dockerfile and runtime layout.

The final source built successfully with `docker build -t inertia-acceptance/vue-mongodb:final .`. Image: `sha256:a1c135aa51dd6e57198c4b791698cea732b560eaa8eeaedf23765947762dfdf5`. Docker Engine: 29.7.2. Client/SSR assets and backend publish completed successfully; the frontend type check had passed separately. The known nonblocking Inertia SSR sourcemap warning remains.

The exact runtime image passed the favicon/template check and reported .NET 10.0.12. Its `mongodump --version` reported 100.19.1; `ldd` found no missing dependencies. ICU and timezone data are present, and invariant globalization is unset.

The existing standalone .NET timezone probe ran inside this exact image and passed all four cases: New York's 2026-03-08 day has 23 hours, its 2026-11-01 day has 25 hours, Lord Howe's 2026-10-04 day has 23.5 hours, and Kathmandu has a +05:45 offset.

Normal, development and combined search/cache profile Compose configurations validated with exit code 0. No stack or local RustFS service was started. Normal application settings continue to use the provided external services.

The image contains the scheduled backup executable. It does not contain `mongorestore`; use host MongoDB Database Tools for a rehearsal restore as described in README. The coordinator separately verified full seeded native database dump/restore. These image probes do not claim the scheduled S3 upload/retention operation was exercised.
Acceptance found and fixed a missing mongo-tools build stage, then added libgssapi-krb5-2 after the exact-image backup probe found an unresolved shared library. The corrected image above passed the repeated executable/dependency probe.
