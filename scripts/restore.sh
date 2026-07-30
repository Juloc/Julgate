#!/usr/bin/env bash
set -euo pipefail

BACKUP_ROOT="${1:?Usage: scripts/restore.sh <backup-directory>}"
IMAGE="${JULGATE_BACKUP_IMAGE:-alpine:3.22}"
FORCE="${JULGATE_RESTORE_FORCE:-false}"

BACKUP_ROOT="$(cd "$BACKUP_ROOT" && pwd)"
test -f "$BACKUP_ROOT/manifest.env"
test -f "$BACKUP_ROOT/SHA256SUMS"

(
  cd "$BACKUP_ROOT"
  sha256sum --check SHA256SUMS
)

# shellcheck disable=SC1090
source "$BACKUP_ROOT/manifest.env"
DATA_VOLUME="${JULGATE_DATA_VOLUME:-julgate-data}"
DRIVES_VOLUME="${JULGATE_DRIVES_VOLUME:-julgate-drives}"

restore_volume() {
  local volume="$1"
  local archive="$2"

  test -f "$BACKUP_ROOT/$archive"
  docker volume create "$volume" >/dev/null

  if [[ "$FORCE" != "true" ]]; then
    if docker run --rm --volume "$volume:/target" "$IMAGE" \
      sh -eu -c 'test -z "$(find /target -mindepth 1 -print -quit)"'; then
      :
    else
      printf 'Refusing to overwrite non-empty volume %s. Set JULGATE_RESTORE_FORCE=true after taking a backup.\n' "$volume" >&2
      exit 1
    fi
  fi

  docker run --rm \
    --volume "$volume:/target" \
    --volume "$BACKUP_ROOT:/backup:ro" \
    "$IMAGE" \
    sh -eu -c "rm -rf /target/* /target/.[!.]* /target/..?* 2>/dev/null || true; tar -xzf /backup/$archive -C /target"
}

restore_volume "$DATA_VOLUME" "${JULGATE_BACKUP_DATA_ARCHIVE:-${JULGATE_DATA_VOLUME}.tar.gz}"
restore_volume "$DRIVES_VOLUME" "${JULGATE_BACKUP_DRIVES_ARCHIVE:-${JULGATE_DRIVES_VOLUME}.tar.gz}"

printf 'Restore completed into volumes %s and %s.\n' "$DATA_VOLUME" "$DRIVES_VOLUME"
printf 'Restore the matching JULGATE_CREDENTIAL_KEY before starting Julgate.\n'
