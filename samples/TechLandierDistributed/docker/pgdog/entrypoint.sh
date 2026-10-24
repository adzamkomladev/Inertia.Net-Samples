#!/bin/sh
set -eu

: "${POSTGRES_PASSWORD:?POSTGRES_PASSWORD is required to render users.toml}"

# Escape the characters that are special in a sed replacement, then render the
# app user password into the runtime-only users file.
ESCAPED=$(printf '%s' "$POSTGRES_PASSWORD" | sed 's/[\\&|"]/\\&/g')
mkdir -p /pgdog
sed "s|__POSTGRES_PASSWORD__|$ESCAPED|g" /pgdog/users.toml.template > /pgdog/users.toml

exec /usr/local/bin/pgdog --config /pgdog/pgdog.toml --users /pgdog/users.toml
