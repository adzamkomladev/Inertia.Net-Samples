#!/bin/sh
set -eu

: "${REPLICATOR_PASSWORD:?REPLICATOR_PASSWORD is required}"

# The official image creates POSTGRES_USER as a superuser. Replication connections
# from another container need both the role and an explicit pg_hba entry.
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v replicator_password="$REPLICATOR_PASSWORD" <<'SQL'
SELECT format('CREATE ROLE replicator WITH REPLICATION LOGIN PASSWORD %L', :'replicator_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'replicator')
\gexec
SQL

if ! grep -q '^host replication replicator all ' "$PGDATA/pg_hba.conf"; then
  echo 'host replication replicator all scram-sha-256' >> "$PGDATA/pg_hba.conf"
fi
