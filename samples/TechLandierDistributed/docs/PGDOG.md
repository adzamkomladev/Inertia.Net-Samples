# PostgreSQL, PgDog and read replicas

TechLandierDistributed uses one PostgreSQL 18 primary, two physical streaming replicas, and PgDog
`v0.1.61` in front of them. Normal roles connect through PgDog on port `6432`; the one-shot
migration service connects directly to the primary.

```text
MainApp / Worker / Realtime
        |  DATABASE_URL      pgdog.role=primary
        |  DATABASE_READ_URL pgdog.role=replica
        v
     PgDog :6432  (transaction pooling, round_robin, conservative)
      /        \
 primary      replica-1, replica-2
 writes       pg_basebackup -R streaming
 migrations
     ^
     |
 migrate/seed: DATABASE_MIGRATE_URL (direct primary)
```

## Configuration files

| File | Purpose |
|---|---|
| `docker/pgdog/pgdog.toml` | PgDog pools, primary/replica hosts, transaction mode, health/metrics, admin password (local only), lag ban. |
| `docker/pgdog/users.toml.template` | App user template; rendered at container start. |
| `docker/pgdog/entrypoint.sh` | Renders `/pgdog/users.toml` from `POSTGRES_PASSWORD`, then `exec`s PgDog. |
| `docker/postgres/init-replication.sh` | Creates the `replicator` role and appends `host replication replicator all scram-sha-256` to `pg_hba.conf`. |
| `docker/postgres/replica-entrypoint.sh` | Waits for the primary, runs `pg_basebackup -Fp -Xs -P -R`, then starts PostgreSQL. |

The baseline PgDog settings include `pooler_mode="transaction"`,
`read_write_split="include_primary_if_replica_banned"`, `read_write_strategy="conservative"`,
`load_balancing_strategy="round_robin"`, `prepared_statements="extended"`,
`healthcheck_port=8080`, `openmetrics_port=9090`, `ban_replica_lag_bytes=16777216` and
`lsn_check_interval=1000`.

`users.toml` is rendered from `POSTGRES_PASSWORD`; no real password is committed. The local
`[admin] password="pgdog-admin"` is for development only. Production must inject both secrets from a
secret manager and use TLS/SCRAM.

## Primary and replicas

The primary runs:

```text
postgres -c wal_level=replica -c max_wal_senders=10 -c max_replication_slots=10 -c hot_standby=on
```

The official `postgres:18-alpine` image runs init scripts before the final server start. Our init
script creates `replicator` and adds the replication HBA rule; without that rule `pg_basebackup` from
another container fails with "no pg_hba.conf entry".

Each replica has its own `/var/lib/postgresql` volume. On first boot it waits for the primary, clears
any partial data directory, and runs:

```sh
PGPASSWORD="$REPLICATOR_PASSWORD" gosu postgres pg_basebackup \
  -h postgres-primary -U replicator -D "$PGDATA" -Fp -Xs -P -R
```

`-R` writes `standby.signal` and the `primary_conninfo` in `postgresql.auto.conf`. The replica then
starts through the normal `docker-entrypoint.sh postgres`. A first backup can occasionally race WAL
retention; the container restart policy retries a clean base backup. For a quieter first boot set
`wal_keep_size` on the primary.

## Connection matrix

| Consumer | Variable | PgDog role | Notes |
|---|---|---|---|
| MainApp, Worker, Realtime (normal) | `DATABASE_URL` | `primary` | Writes, auth, checkout, orders, notifications, mutations, read-after-write. |
| Public catalogue/home/search/detail/related/deals; cached analytics | `DATABASE_READ_URL` | `replica` | `IReadDbFactory`/`ReadOnlyDbFactory`; round-robin replicas. |
| `--migrate`, `--seed-demo`, `--create-admin` | `DATABASE_MIGRATE_URL` | none (direct primary) | DDL and `__EFMigrationsHistory` bypass PgDog. |
| `admin` DB | `pgdog-admin` (local) | n/a | `SHOW POOLS`, `SHOW REPLICATION`, `RECONNECT`. |

Npgsql example:

```text
Host=pgdog;Port=6432;Database=techlandier;Username=techlandier;Password=…;Options=-c pgdog.role=primary;Maximum Pool Size=10
Host=pgdog;Port=6432;Database=techlandier;Username=techlandier;Password=…;Options=-c pgdog.role=replica;Maximum Pool Size=5
```

PgDog's conservative strategy sends manual transactions to the primary; read-only transactions may
use replicas. The app connection is pinned to primary by the startup parameter, so no standalone
`SELECT` can accidentally reach a lagging replica.

Read path in this sample:

- `StoreService` public home rails, category tiles, catalogue search, product detail, related
  products, deals and active zones.
- `SalesService` cached admin analytics/sales series/top products/category share.
- `AdminService.KpisAsync` cached KPI summary.
- Reviews, review eligibility, cart/checkout, order reads, stock decisions, notifications and every
  mutation stay on the primary. Auth and management writes are primary-only.

## Read-after-write

`include_primary_if_replica_banned` lets PgDog fall back to the primary when all replicas are
unavailable. That is safe for the selected lag-tolerant reads but means `pgdog.role=replica` can
return a primary connection during an outage. Never rely on the read URL for ownership, payment,
stock or just-written data.

PgDog's transaction-mode pooler can reuse a server connection between clients. The `pgdog.role`
startup parameter is handled by PgDog; it is not a normal PostgreSQL GUC and is not sent through to
the backend.

## Admin and monitoring

Local admin connection:

```sh
psql "host=127.0.0.1 port=5443 dbname=admin user=admin password=pgdog-admin"
```

Useful commands:

```sql
SHOW POOLS;        -- pools, primary/replica roles, banned/healthy, waiting clients
SHOW REPLICATION;  -- replica_lag, pg_lsn, lsn_age, pg_is_in_recovery
RECONNECT;         -- recreate pool connections; clears health/lag bans
SHOW CONFIG;       -- loaded settings
SHOW STATS;        -- pool statistics
```

Example `SHOW POOLS` output (local):

```text
 id |  database   |    user     |        addr        | port | role    | cl_waiting | sv_idle | sv_active | sv_total | pool_mode   | banned | healthy | online
  1 | techlandier | techlandier | postgres-replica-1 | 5432 | replica |          0 |       2 |         0 |        2 | transaction | f      | t       | t
  2 | techlandier | techlandier | postgres-replica-2 | 5432 | replica |          0 |       2 |         0 |        2 | transaction | f      | t       | t
  0 | techlandier | techlandier | postgres-primary   | 5432 | primary |          0 |       2 |         0 |        2 | transaction | f      | t       | t
```

Example `SHOW REPLICATION` output:

```text
 id |  database   |    user     |        addr        | port | role    | replica_lag |  pg_lsn   | lsn_age | pg_is_in_recovery
  1 | techlandier | techlandier | postgres-replica-1 | 5432 | replica |           0 | 0/50220A0 |     232 | t
  2 | techlandier | techlandier | postgres-replica-2 | 5432 | replica |           0 | 0/50220A0 |     232 | t
  0 | techlandier | techlandier | postgres-primary   | 5432 | primary |           0 | 0/50220A0 |     232 | f
```

OpenMetrics is on `:9090` inside the PgDog container (host `PGDOG_METRICS_PORT`, default `9090`;
use another host port if it is already taken) and the health endpoint is `:8080`.

## Replication lag and bans

The sample uses `ban_replica_lag_bytes=16777216` (16 MiB). Byte-based lag is exact and does not
suffer from the idle-time false positives of PgDog's transaction-time formula, which uses
`now() - pg_last_xact_replay_timestamp()` and can make an idle replica look lagged. A replica that
falls more than 16 MiB behind is banned from reads; reads then use the other replica or the primary
through `include_primary_if_replica_banned`. `SHOW POOLS` shows `banned=t`; `RECONNECT` resets pools
and bans.

If your workload needs a time bound as well, add `ban_replica_lag` (milliseconds) and leave startup
headroom. The lower of the two thresholds wins in PgDog.

PgDog does **not** promote a replica or perform failover. It follows `pg_is_in_recovery()` only when
databases use `role="auto"`; this baseline uses explicit `primary`/`replica`. Use Patroni, CloudNativePG,
RDS/Aurora or another managed HA system for promotion, TLS, backups and split-brain protection.

## Production checklist

- TLS from apps to PgDog and PgDog to PostgreSQL; SCRAM-SHA-256; no plaintext passwords in git.
- Render `users.toml` from a secret manager; restrict file permissions; rotate secrets.
- Separate primary and replica hosts; PgDog replicas behind a TCP load balancer; health checks.
- Persistent primary storage and tested PITR backups; replicas are not backups.
- Monitor `SHOW POOLS`, `SHOW REPLICATION`, OpenMetrics and `replica_lag`; alert on banned replicas.
- Keep `DATABASE_MIGRATE_URL` direct to the current primary; do not run DDL through PgDog.
- Keep read-after-write/auth/checkout on the primary URL. Extend the read factory only for
  explicitly lag-tolerant queries.
- Keep `ban_replica_lag_bytes` as the primary bound; add `ban_replica_lag` only with measured
  headroom for the idle-time formula.
- PgDog admin database is not transactional; do not use it for application transactions.

## Verification recipes

Write through the primary role, then read through the replica role:

```sh
# primary
psql "host=pgdog port=6432 user=techlandier password=… dbname=techlandier options='-c pgdog.role=primary'" \
  -c "CREATE TABLE IF NOT EXISTS pgdog_probe (id int primary key, note text);" \
  -c "INSERT INTO pgdog_probe VALUES (1, 'write-through-pgdog') ON CONFLICT (id) DO UPDATE SET note=EXCLUDED.note;" \
  -c "SELECT pg_is_in_recovery();"

# replica
psql "host=pgdog port=6432 user=techlandier password=… dbname=techlandier options='-c pgdog.role=replica'" \
  -c "SELECT pg_is_in_recovery();" \
  -c "SELECT * FROM pgdog_probe;"
```

Expected: primary `f`, replica `t`, and the row visible. If replicas are banned after an idle
period, run `RECONNECT` in the admin DB first; replication itself remains healthy.

The opt-in `PgDogReadTests` test uses `PGDOG_TEST_PRIMARY_URL` and `PGDOG_TEST_READ_URL` and proves
the same through Npgsql/EF Core. The normal suite does not require PgDog.
