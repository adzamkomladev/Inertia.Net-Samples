#!/bin/sh
# Creates the dev buckets: uploads (public read, CORS for browser PUTs) and backups (private).
set -e
S3="aws --endpoint-url http://s3:9000"
until $S3 s3api list-buckets >/dev/null 2>&1; do sleep 1; done
$S3 s3api create-bucket --bucket uploads 2>/dev/null || true
$S3 s3api create-bucket --bucket backups 2>/dev/null || true
$S3 s3api put-bucket-policy --bucket uploads --policy file:///setup/public-read.json
$S3 s3api put-bucket-cors --bucket uploads --cors-configuration file:///setup/cors.json
echo "buckets uploads and backups ready"
