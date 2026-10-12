# Multiple app replicas

This sample derives from `svelte-inertia-mongodb` and keeps its Svelte 5 frontend and MongoDB feature implementation. HAProxy routes to three independent ASP.NET processes. MongoDB stores shared users, posts, jobs and cron leases; Redis shares authentication keys, rate budgets and SignalR messages; S3 stores media and backups; Meilisearch is optional.

Compose initializes indexes once before starting the app processes. MongoDB runs as a **single member replica set** so transactions work: this is an app scaling demonstration, not database high availability. Production can use a managed replica set by setting the same `DATABASE_URL` and `DATABASE_NAME` on all processes. Remove the bundled initializer's Mongo dependency if replacing the database service. Keep `MONGO_MODE=replica`.

Redis is required whenever `Replicas:Enabled=true` (the default). `REDIS_URL_DOCKER` is the URL used by containers; `.env.example` points it at Compose Redis. The configured local `.env` carries over the reference sample's external Redis and other service credentials, with separate sample namespaces. The local Redis service remains available for a self-contained setup. Persist Redis data, disable eviction for the key repository, restrict access and use TLS for external services. Protect Data Protection XML with a shared certificate in production. Losing the key ring invalidates existing authentication and verification tokens.

All processes must use the same `Replicas__Namespace`, database, service credentials and analytics salt. Separate applications must use separate namespaces, indexes and databases. Cookie names are scoped to this sample. The proxy removes client-supplied forwarding headers and supplies the actual client IP. Only the proxy port is exposed publicly; cookie affinity supports negotiated SignalR and long polling. Do not directly expose app ports.

SignalR events invalidate visible data and coalesce partial Inertia reloads; clients do not order independent publishers by a process-local sequence. Editor pages retain unsaved state. Private notifications still target the authenticated owner's SignalR user group. Delivery is transient: reconnect prompts a refresh, and offline notifications are not replayed. Redis failure makes readiness fail and shared rate limits deny requests instead of granting independent budgets.

The existing queue atomically claims jobs in MongoDB; other workers poll when a process-local wake is missed. Cron leases protect scheduled occurrences across processes. Queue leases last five minutes and cron leases thirty minutes; they have no renewal. Handlers must finish within their lease, and external side effects must tolerate retries. View buffers are process-local and can lose buffered events on a crash. Scale app processes according to database, Redis and storage capacity.

See [DOCKER_ACCEPTANCE.md](DOCKER_ACCEPTANCE.md) for verification results.
