#!/bin/bash
# One-shot replica-set initializer. It is idempotent: restarting it only waits for the existing set.
set -euo pipefail

for host in mongo1 mongo2 mongo3; do
  echo "waiting for ${host}:27017"
  until mongosh --host "${host}:27017" --quiet --eval "db.adminCommand('ping').ok" 2>/dev/null | grep -q 1; do
    sleep 1
  done
done

echo "initiating replica set rs0"
mongosh --host mongo1:27017 --quiet --eval '
  try {
    rs.status();
  } catch (e) {
    rs.initiate({
      _id: "rs0",
      members: [
        { _id: 0, host: "mongo1:27017" },
        { _id: 1, host: "mongo2:27017" },
        { _id: 2, host: "mongo3:27017" }
      ]
    });
  }
' >/dev/null

echo "waiting for a writable primary"
for attempt in $(seq 1 60); do
  primary=$(mongosh --host mongo1:27017 --quiet --eval 'db.hello().primary' 2>/dev/null || true)
  if [ -n "${primary}" ] && mongosh --host "${primary}" --quiet --eval 'db.hello().isWritablePrimary' 2>/dev/null | grep -qi true; then
    echo "replica set primary: ${primary}"
    exit 0
  fi
  sleep 2
done

echo "replica set did not elect a writable primary" >&2
exit 1
