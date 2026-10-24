#!/bin/sh
set -eu

: "${PGDATA:=/var/lib/postgresql/18/docker}"
: "${REPLICATOR_PASSWORD:?REPLICATOR_PASSWORD is required}"

if [ ! -s "$PGDATA/PG_VERSION" ]; then
  echo "Waiting for postgres-primary to accept replication connections..."
  until PGPASSWORD="$REPLICATOR_PASSWORD" psql "host=postgres-primary port=5432 user=replicator dbname=postgres" -c 'SELECT 1' >/dev/null 2>&1; do
    sleep 2
  done

  # pg_basebackup requires an empty directory. A previous failed attempt leaves
  # partial files, so start clean on every retry.
  rm -rf "$PGDATA"
  mkdir -p "$PGDATA"
  chown -R postgres:postgres "$PGDATA"

  echo "Taking base backup from postgres-primary..."
  PGPASSWORD="$REPLICATOR_PASSWORD" gosu postgres pg_basebackup \
    -h postgres-primary -U replicator -D "$PGDATA" -Fp -Xs -P -R
fi

exec docker-entrypoint.sh postgres
