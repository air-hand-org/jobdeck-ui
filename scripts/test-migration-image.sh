#!/usr/bin/env bash
set -euo pipefail

# Run from the repository root against the already built production image.
if [[ $# != 1 ]]; then
  echo "Usage: $0 MIGRATOR_IMAGE" >&2
  exit 1
fi
export MIGRATOR_IMAGE
MIGRATOR_IMAGE=$(docker image inspect --format '{{.Id}}' "$1")
project="migration-smoke-$(cat /proc/sys/kernel/random/uuid)"
compose=(docker compose --project-name "$project" --file scripts/migration-smoke.compose.yaml)
cleanup() {
  local status=$?
  trap - EXIT
  if (( status != 0 )); then
    "${compose[@]}" logs postgres || true
  fi
  "${compose[@]}" down --volumes --remove-orphans || true
  exit "$status"
}
trap cleanup EXIT

sql() {
  "${compose[@]}" exec -T postgres psql -U postgres -d smoke -v ON_ERROR_STOP=1 -Atc "$1"
}
migrate() {
  "${compose[@]}" run --rm --no-deps -e "ConnectionStrings__Postgres=$1" migrator --verbose
}
check_equal() {
  if [[ "$1" != "$2" ]]; then
    printf '%s\nExpected:\n%s\nActual:\n%s\n' "$3" "$2" "$1" >&2
    exit 1
  fi
}

"${compose[@]}" up --detach --wait --wait-timeout 90 postgres
check_equal "$(sql "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public'")" 0 'database must start empty'

echo "Testing production migrator $1: $MIGRATOR_IMAGE"
echo 'Applying image to empty database'
connection='Host=postgres;Database=smoke;Username=postgres;Timeout=5'
migrate "$connection"
expected_tables=$(printf '%s\n' DataProtectionKeys OpenIddictApplications OpenIddictAuthorizations OpenIddictScopes OpenIddictTokens Profiles Sessions __EFMigrationsHistory)
check_equal "$(sql "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name")" "$expected_tables" 'expected application tables and EF history'
expected_migrations=$(
  for file in src/Migrations/*.cs; do
    case "$file" in *.Designer.cs|*ModelSnapshot.cs) continue ;; esac
    file=${file##*/}
    printf '%s\n' "${file%.cs}"
  done | LC_ALL=C sort
)
history_query='SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId"'
history=$(sql "$history_query")
check_equal "$history" "$expected_migrations" 'history must contain every checked-in migration'

echo 'Reapplying the same image ID'
migrate "$connection"
check_equal "$(sql "$history_query")" "$history" 'repeat invocation must preserve migration history'

echo 'Checking that bundle connection errors propagate'
status=0
migrate 'Host=postgres;Port=1;Database=smoke;Username=postgres;Timeout=1' || status=$?
check_equal "$status" 1 'bundle connection failure must exit 1'
echo 'PASS migrator image empty database, schema/history, repeat invocation, and failure exit'
