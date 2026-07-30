#!/usr/bin/env bash
set -euo pipefail

ENV_FILE="${1:?Usage: scripts/rotate-credential-key.sh <path-to-.env>}"
ENV_FILE="$(cd "$(dirname "$ENV_FILE")" && pwd)/$(basename "$ENV_FILE")"
COMPOSE_FILE="${JULGATE_COMPOSE_FILE:-$(dirname "$ENV_FILE")/docker-compose.yml}"
VERIFY_URL="${JULGATE_VERIFY_URL:-http://127.0.0.1:18088/healthz}"

test -f "$ENV_FILE"
test -f "$COMPOSE_FILE"
command -v openssl >/dev/null
command -v python3 >/dev/null
command -v docker >/dev/null

CURRENT_KEY="$(python3 - "$ENV_FILE" <<'PY'
import pathlib, sys
path = pathlib.Path(sys.argv[1])
values = {}
for raw in path.read_text(encoding='utf-8').splitlines():
    if raw.strip() and not raw.lstrip().startswith('#') and '=' in raw:
        key, value = raw.split('=', 1)
        values[key.strip()] = value.strip()
print(values.get('JULGATE_CREDENTIAL_KEY', ''))
PY
)"

if [[ -z "$CURRENT_KEY" || "$CURRENT_KEY" == "CHANGE_ME" ]]; then
  printf 'JULGATE_CREDENTIAL_KEY is missing or still a placeholder.\n' >&2
  exit 1
fi

NEW_KEY="$(openssl rand -base64 32 | tr -d '\n')"
cp --preserve=mode,timestamps "$ENV_FILE" "$ENV_FILE.before-key-rotation"

update_env() {
  local primary="$1"
  local previous="$2"
  python3 - "$ENV_FILE" "$primary" "$previous" <<'PY'
import pathlib, sys
path = pathlib.Path(sys.argv[1])
primary, previous = sys.argv[2], sys.argv[3]
lines = path.read_text(encoding='utf-8').splitlines()
updates = {
    'JULGATE_CREDENTIAL_KEY': primary,
    'JULGATE_CREDENTIAL_KEY_PREVIOUS': previous,
}
seen = set()
out = []
for line in lines:
    stripped = line.lstrip()
    if stripped and not stripped.startswith('#') and '=' in line:
        key = line.split('=', 1)[0].strip()
        if key in updates:
            out.append(f'{key}={updates[key]}')
            seen.add(key)
            continue
    out.append(line)
for key, value in updates.items():
    if key not in seen:
        out.append(f'{key}={value}')
path.write_text('\n'.join(out) + '\n', encoding='utf-8')
PY
}

wait_for_health() {
  for attempt in {1..60}; do
    if curl --fail --silent --show-error "$VERIFY_URL" >/dev/null; then
      return 0
    fi
    sleep 2
  done
  return 1
}

update_env "$NEW_KEY" "$CURRENT_KEY"
docker compose --env-file "$ENV_FILE" --file "$COMPOSE_FILE" up --detach
if ! wait_for_health; then
  printf 'Health verification failed. Restoring the original environment file.\n' >&2
  cp "$ENV_FILE.before-key-rotation" "$ENV_FILE"
  docker compose --env-file "$ENV_FILE" --file "$COMPOSE_FILE" up --detach || true
  exit 1
fi

# Startup rewrites every stored credential using the new primary key.
update_env "$NEW_KEY" ""
docker compose --env-file "$ENV_FILE" --file "$COMPOSE_FILE" up --detach
if ! wait_for_health; then
  printf 'Restart without the previous key failed. Restore the pre-rotation environment and data backup.\n' >&2
  exit 1
fi

printf 'Credential key rotation completed and verified.\n'
printf 'Keep %s only until stored connections have been tested.\n' "$ENV_FILE.before-key-rotation"
