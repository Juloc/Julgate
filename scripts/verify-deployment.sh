#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${JULGATE_BASE_URL:-http://127.0.0.1:8088}"
WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

curl --fail --silent --show-error "$BASE_URL/healthz" > "$WORK_DIR/health.json"
grep --quiet '"status":"healthy"' "$WORK_DIR/health.json"

curl --fail --silent --show-error \
  --dump-header "$WORK_DIR/headers.txt" \
  --output "$WORK_DIR/login.html" \
  "$BASE_URL/login"
grep --ignore-case --quiet '^X-Content-Type-Options: nosniff' "$WORK_DIR/headers.txt"
grep --quiet 'Julgate' "$WORK_DIR/login.html"
! grep --quiet '>Matgate<' "$WORK_DIR/login.html"

curl --fail --silent --show-error \
  --output "$WORK_DIR/manifest.json" \
  "$BASE_URL/manifest.webmanifest"
grep --quiet 'Julgate' "$WORK_DIR/manifest.json"
! grep --quiet 'Matgate' "$WORK_DIR/manifest.json"

website_status="$(curl --silent --output /dev/null --write-out '%{http_code}' "$BASE_URL/website/test")"
tools_status="$(curl --silent --output /dev/null --write-out '%{http_code}' "$BASE_URL/tools")"
archive_status="$(curl --silent --output /dev/null --write-out '%{http_code}' \
  --request POST "$BASE_URL/api/files/00000000-0000-0000-0000-000000000001/extract")"
traversal_status="$(curl --silent --output /dev/null --write-out '%{http_code}' \
  "$BASE_URL/?path=folder%252f..%252fsecret")"
csrf_status="$(curl --silent --output /dev/null --write-out '%{http_code}' \
  --request POST --header 'Origin: https://evil.invalid' "$BASE_URL/login")"

test "$website_status" = "404"
test "$tools_status" = "404"
test "$archive_status" = "404"
test "$traversal_status" = "400"
test "$csrf_status" = "403"

printf 'Julgate deployment verification passed for %s\n' "$BASE_URL"
