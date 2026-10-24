# PgDog for TechLandierDistributed

Research and implementation notes for the PostgreSQL layer:

- one PostgreSQL primary;
- two physical read replicas;
- PgDog as the connection pooler, load balancer and single application endpoint;
- `postgres:18-alpine` for all three servers;
- `ghcr.io/pgdogdev/pgdog:v0.1.61`.

This is the PostgreSQL companion to
[REPLICA_SCALING_RESEARCH.md](REPLICA_SCALING_RESEARCH.md) and
[INERTIA_CACHE_MULTI_REPLICA.md](INERTIA_CACHE_MULTI_REPLICA.md). The working sample is
`samples/TechLandierDistributed`; its operator notes are in
`samples/TechLandierDistributed/docs/PGDOG.md`.

## 1. Why PgDog

PgDog sits between the application and PostgreSQL and speaks the PostgreSQL wire protocol. For this sample it
provides:

| Capability | Relevance |
|---|---|
| Transaction-mode pooling | Many app/worker/realtime connections share a small number of Postgres backends |
| Read/write split | `SELECT` traffic can use the two replicas; writes and manual transactions go to the primary |
| Prepared statements | Npgsql/EF Core can keep using its normal prepared-statement behavior |
| Health checks | Broken replicas are temporarily removed from the read pool |
| Replication monitoring | `SHOW REPLICATION` exposes lag and `pg_is_in_recovery()` |
| Failover detection | With `role="auto"`, PgDog follows a database that has already been promoted |
| Pool/client/server stats | `SHOW POOLS`, `SHOW CLIENTS`, `SHOW SERVERS` on the admin database |
| OpenMetrics | Prometheus-style metrics on a separate HTTP port |

PgDog does **not** promote a replica. The PostgreSQL layer still needs Patroni, a managed service or a manual
promotion procedure for real failover. The sample uses static `primary`/`replica` roles; production can switch to
`role="auto"` when an external orchestrator owns promotion.

## 2. Topology

```text
              mainapp-1..N     worker-1..N     realtime-1..N
                    \               |               /
                     \--------------+--------------/
                                    |
                              PgDog :6432
                    transaction pool + read/write split
                         /            |            \
                        v             v             v
                 postgres-primary  replica-1   replica-2
                    (writes)        (reads)     (reads)
                         |
              /var/lib/postgresql (PGDATA)
```

- `migrate` connects **directly** to `postgres-primary`, not through PgDog. DDL and migrations should bypass the
  pooler and the read/write split.
- Normal app/worker/realtime traffic uses PgDog.
- The PgDog admin database is `admin` on the same 6432 port. It supports `SHOW ...` commands and `RELOAD`; it does
  not support transactions or prepared statements.

## 3. PostgreSQL 18 Alpine

All three servers use `postgres:18-alpine`. PostgreSQL 18's default `PGDATA` is
`/var/lib/postgresql/18/docker`, so the Compose volume is mounted at `/var/lib/postgresql` (the parent), not
`/var/lib/postgresql/data`.

Primary settings:

```text
wal_level=replica
max_wal_senders=10
max_replication_slots=10
hot_standby=on
```

Primary initialization:

1. create a `replicator` role with `REPLICATION LOGIN`;
2. append `host replication replicator all scram-sha-256` to `$PGDATA/pg_hba.conf`.

The second step is required. Without it `pg_basebackup` fails with:

```text
FATAL: no pg_hba.conf entry for replication connection ... user "replicator"
```

Replica startup:

```bash
until pg_isready -h "$PRIMARY_HOST" -U replicator -d postgres; do sleep 1; done
rm -rf "${PGDATA:?}/"*
install -d -o postgres -g postgres -m 700 "$PGDATA"
PGPASSWORD="$REPLICATOR_PASSWORD" gosu postgres pg_basebackup \
  -h "$PRIMARY_HOST" -U replicator -D "$PGDATA" -Fp -Xs -P -R
exec docker-entrypoint.sh postgres
```

`postgres:18-alpine` includes `pg_basebackup`, `gosu` and the standard entrypoint. The basebackup writes
`standby.signal` and `primary_conninfo`; `PGPASSWORD` is available to the server process for the streaming
connection. The replicas are physical streaming replicas, not logical.

Health:

```text
pg_isready -U techlandier -d techlandier
```

## 4. PgDog configuration

Baseline `pgdog.toml` used by the sample:

```toml
[general]
host = "0.0.0.0"
port = 6432
default_pool_size = 20
min_pool_size = 2
pooler_mode = "transaction"
load_balancing_strategy = "round_robin"
read_write_split = "include_primary_if_replica_banned"
read_write_strategy = "conservative"
healthcheck_port = 8080
openmetrics_port = 9090
lsn_check_delay = 0
lsn_check_interval = 1000
# Byte-based lag avoids the idle-time false positives of the transaction-time formula.
ban_replica_lag_bytes = 16777216
prepared_statements = "extended"

[admin]
password = "pgdog-admin"

[[databases]]
name = "techlandier"
host = "postgres-primary"
port = 5432
role = "primary"
database_name = "techlandier"

[[databases]]
name = "techlandier"
host = "postgres-replica-1"
port = 5432
role = "replica"
database_name = "techlandier"

[[databases]]
name = "techlandier"
host = "postgres-replica-2"
port = 5432
role = "replica"
database_name = "techlandier"
```

`users.toml` must contain the application user/database pair. In v0.1.61, enabling `passthrough_auth` did not remove
the need for a configured user/database entry; without it PgDog logged:

```text
auth error: no user or database in config
```

For the local sample, render the password from `POSTGRES_PASSWORD` at container start instead of committing it.
The image has `bash`, `sed`, `curl` and `pg_isready`; a small entrypoint can generate `/pgdog/users.toml` and then
`exec /usr/local/bin/pgdog --config /pgdog/pgdog.toml --users /pgdog/users.toml`.

`read_write_split` choices:

- `include_primary` — reads may use the primary and replicas;
- `exclude_primary` — reads use replicas only;
- `include_primary_if_replica_banned` — reads use replicas, falling back to the primary when replicas are banned;
- `prefer_primary` — all reads go to the primary unless explicitly routed.

The sample uses `include_primary_if_replica_banned`. Application correctness does not depend on automatic split:
the main application connection is pinned to the primary role, and selected lag-tolerant read paths use the replica
role explicitly.

## 5. Application connections

Use two Npgsql connection strings, both pointing at PgDog:

```text
DATABASE_URL=Host=pgdog;Port=6432;Database=techlandier;Username=techlandier;Password=...;Options=-c pgdog.role=primary;Maximum Pool Size=10
DATABASE_READ_URL=Host=pgdog;Port=6432;Database=techlandier;Username=techlandier;Password=...;Options=-c pgdog.role=replica;Maximum Pool Size=10
DATABASE_MIGRATE_URL=Host=postgres-primary;Port=5432;Database=techlandier;Username=techlandier;Password=...
```

I verified the Npgsql `Options` form against PgDog v0.1.61:

```text
primary: replica=False
replica: replica=True
```

This is the reliable way to select a role with Npgsql/EF Core. The equivalent psql test is:

```bash
PGOPTIONS='-c pgdog.role=replica' psql -h pgdog -p 6432 -U techlandier -d techlandier \
  -c 'select pg_is_in_recovery()'
```

### 5.1 Read-path policy

Reads that must see the latest write stay on `DATABASE_URL` (primary):

- authentication/user validation;
- checkout, orders, payments, refunds, installments;
- bookings/reservations and inventory decisions;
- notifications after a write;
- reviews and support/care immediately after a mutation;
- any page that redirects straight from a mutation.

Lag-tolerant reads can use `DATABASE_READ_URL` (replicas):

- public catalog, categories, home rails, deals and product search;
- product detail and related products;
- cached admin analytics/sales rollups;
- other explicitly read-only reporting pages.

The sample uses `IReadDbFactory`/`ReadOnlyDbFactory` to create `AppDb` contexts with separate
`DbContextOptions<AppDb>` built from `DATABASE_READ_URL`. `StoreService` (public catalog/search/detail/
related) and `SalesService` (analytics) use it; write/fresh-read services keep the primary `AppDb`. Do not use
the read path for authorization or business invariants.

### 5.2 Query comments are not the EF Core path

PgDog supports query comments such as:

```sql
/*pgdog_role: replica*/ SELECT ...
```

In v0.1.61 the accepted spelling is sensitive: `/*pgdog_role: replica*/` and
`/* pgdog_role = replica */` worked in the probe, while `/* pgdog_role: replica */` did not. EF Core's
`.TagWith("...")` emits a line comment (`-- ...`), which PgDog ignored in the probe. Use the connection-level
`pgdog.role` option or a dedicated read context instead of relying on EF Core tags. If a future PgDog release
normalizes comments, a command interceptor can be added; do not put correctness-critical reads behind a comment
syntax that has not been tested with the pinned version.

## 6. Pool sizing

PgDog multiplexes app connections, so the database no longer needs one backend per Npgsql pool slot.

- `default_pool_size = 20`, `min_pool_size = 2` in the local sample.
- Npgsql `Maximum Pool Size=10` per app/worker/realtime process is enough because PgDog queues/checkouts
  transactions rather than pinning a backend per client.
- Keep `default_pool_size * 3 servers` below PostgreSQL `max_connections`, with headroom for migrations, admin
  access and maintenance.
- `pooler_mode = "transaction"` is the right choice for EF Core; manual transactions are pinned to the primary.
- `prepared_statements = "extended"` matches Npgsql's extended protocol. Test after changing it; EF Core may also
  use anonymous prepared statements depending on configuration.

## 7. Health, metrics and admin

PgDog:

```bash
curl -fsS http://127.0.0.1:8080/        # healthcheck_port; 200 if at least one pool is online
curl -fsS http://127.0.0.1:9090/metrics # OpenMetrics
```

Admin database:

```bash
PGPASSWORD=pgdog-admin psql -h pgdog -p 6432 -U admin -d admin \
  -c 'SHOW POOLS' \
  -c 'SHOW REPLICATION' \
  -c 'SHOW SERVERS'
```

`SHOW POOLS` shows `role`, `cl_waiting`, `sv_idle`, `sv_active`, `sv_total`, `maxwait`, `banned`, `errors`,
`online` and `replica_lag`. `SHOW REPLICATION` shows `replica_lag`, `pg_lsn`, `lsn_age` and
`pg_is_in_recovery()`.

Useful OpenMetrics counters include `total_reads`, `total_writes`, `clients`, `cl_waiting`, `sv_active`,
`sv_idle`, `errors`, `total_xact_count`, `total_query_count` and `prepared_statements`.

## 8. Failure behavior

- **Replica down / lagging:** health checks or `ban_replica_lag_bytes` remove it from reads.
  `include_primary_if_replica_banned` lets reads fall back to the primary.
- **Primary down:** PgDog keeps writing to the configured primary and retries connections; it does not promote a
  replica. The app is writable only after an external promotion (Patroni/managed service) and, if `role="auto"`,
  PgDog observes the new primary via `pg_is_in_recovery()`.
- **Split brain:** PgDog trusts `pg_is_in_recovery()`. Do not promote a replica while the old primary is still
  writable.
- **PgDog down:** all database traffic stops. Run PgDog as a managed/restartable service, and for production put a
  TCP load balancer in front of multiple PgDog instances. Use its HTTP health endpoint for the LB.
- **PgDog restart:** clients reconnect; `shutdown_timeout` (default 60 s) gives in-flight transactions time to
  finish.

## 9. Local verification

The following was verified against PgDog v0.1.61 with `postgres:18` (the sample uses `postgres:18-alpine`):

```text
three databases healthy: primary + 2 replicas
PgDog health 200
SHOW POOLS: 3 pools, primary + 2 replicas, pool_mode=transaction
SHOW REPLICATION: both replicas replica_lag=0, pg_is_in_recovery=t
write through PgDog: INSERT -> primary
read through replica role: pg_is_in_recovery()=true
Npgsql: connect, DDL, insert, select, explicit transaction, read after commit
```

Npgsql transaction test output:

```text
connected
insert: 1
select: replica=True server=172.23.0.3/32 v=hello
transaction committed
after tx: tx
```

The `after tx` read happened to hit a replica with zero lag. That is not a guarantee; use the primary role for
read-after-write flows.

## 10. Production checklist

- Use TLS between app, PgDog and PostgreSQL (`tls_certificate`, `tls_private_key`, `tls_verify`).
- Use SCRAM and secret-rendered `users.toml`; do not commit passwords.
- Use separate hosts/availability zones for primary and replicas.
- Put PgDog behind a TCP load balancer with its HTTP health endpoint; run more than one PgDog instance.
- Configure `ban_replica_lag_bytes` from measured replication lag; add `ban_replica_lag` only with
  headroom for the idle-time formula.
- Back up PostgreSQL independently (PgDog is not a backup).
- Back up/rediscover PgDog config; `RELOAD` after config changes.
- Monitor `SHOW REPLICATION`, `SHOW POOLS`, OpenMetrics `total_reads`/`total_writes`, `cl_waiting`, `maxwait`,
  `errors` and replica lag.
- Use Patroni/managed PostgreSQL for promotion/failover. PgDog detects a promoted database, it does not perform
  the promotion.
- Size PgDog `default_pool_size` and Npgsql pools against PostgreSQL `max_connections` and PgDog CPU/memory.

## References

- <https://pgdog.dev/>
- <https://docs.pgdog.dev/>
- <https://docs.pgdog.dev/installation/>
- <https://docs.pgdog.dev/features/connection-pooler/>
- <https://docs.pgdog.dev/features/load-balancer/>
- <https://docs.pgdog.dev/features/load-balancer/healthchecks/>
- <https://docs.pgdog.dev/features/load-balancer/replication-failover/>
- <https://docs.pgdog.dev/features/load-balancer/transactions/>
- <https://docs.pgdog.dev/features/load-balancer/manual-routing/>
- <https://docs.pgdog.dev/administration/replication/>
- <https://docs.pgdog.dev/administration/pools/>
- <https://docs.pgdog.dev/administration/clients/>
- <https://docs.pgdog.dev/administration/tasks/>
- <https://docs.pgdog.dev/administration/servers/>
- <https://docs.pgdog.dev/configuration/pgdog.toml/general/>
- <https://docs.pgdog.dev/configuration/pgdog.toml/databases/>
- <https://docs.pgdog.dev/configuration/users.toml/users/>
- <https://docs.pgdog.dev/features/metrics/>
- <https://docs.pgdog.dev/client-drivers/>
