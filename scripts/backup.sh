#!/usr/bin/env bash
set -euo pipefail

DATA_VOLUME="${JULGATE_DATA_VOLUME:-julgate-data}"
DRIVES_VOLUME="${JULGATE_DRIVES_VOLUME:-julgate-drives}"
BACKUP_ROOT="${1:-backups/julgate-$(date -u +%Y-%m-%d_%H%M%S)}"
IMAGE="${JULGATE_BACKUP_IMAGE:-alpine:3.22}"
JULGATE_IMAGE="${JULGATE_IMAGE:-unknown}"

mkdir -p "$BACKUP_ROOT"
BACKUP_ROOT="$(cd "$BACKUP_ROOT" && pwd)"

for volume in "$DATA_VOLUME" "$DRIVES_VOLUME"; do
  docker volume inspect "$volume" >/dev/null
  archive="$BACKUP_ROOT/${volume}.tar.gz"
  docker run --rm \
    --volume "$volume:/source:ro" \
    --volume "$BACKUP_ROOT:/backup" \
    "$IMAGE" \
    sh -eu -c "cd /source && tar -czf /backup/$(basename "$archive") ."
done

cat > "$BACKUP_ROOT/manifest.env" <<EOF
JULGATE_BACKUP_CREATED_UTC=$(date -u +%Y-%m-%dT%H:%M:%SZ)
JULGATE_IMAGE=$JULGATE_IMAGE
JULGATE_DATA_VOLUME=$DATA_VOLUME
JULGATE_DRIVES_VOLUME=$DRIVES_VOLUME
EOF

(
  cd "$BACKUP_ROOT"
  sha256sum "${DATA_VOLUME}.tar.gz" "${DRIVES_VOLUME}.tar.gz" manifest.env > SHA256SUMS
  sha256sum --check SHA256SUMS
)

printf 'Backup created: %s\n' "$BACKUP_ROOT"
printf 'Back up JULGATE_CREDENTIAL_KEY separately from this directory.\n'
