#!/bin/sh
set -eu

root="$(mktemp -d)"
data="$root/data"
backups="$root/backups"
restore="$root/restore"
container="beacon-backup-smoke-$$"

cleanup() {
    docker rm -f "$container" >/dev/null 2>&1 || true
    rm -rf "$root"
}
trap cleanup EXIT INT TERM

mkdir -p "$data/images" "$backups" "$restore"
chmod 0777 "$data" "$data/images" "$backups" "$restore"

docker build --file deploy/backup.Dockerfile --tag beacon-backup:ci .

# An empty SQLite file is internally consistent, but it is not a recoverable Beacon database.
docker run --rm \
    --user 0:0 \
    --entrypoint sqlite3 \
    -v "$data:/data" \
    beacon-backup:ci \
    /data/beacon.db \
    'VACUUM;'

docker run --detach --name "$container" \
    -e BACKUP_INTERVAL_SECONDS=3600 \
    -e BACKUP_RETENTION_DAYS=30 \
    -v "$data:/data:ro" \
    -v "$backups:/backups" \
    beacon-backup:ci >/dev/null
sleep 2
[ -z "$(find "$backups" -maxdepth 1 -type f -name 'beacon-*.tar.gz' -print -quit)" ]
docker rm -f "$container" >/dev/null

docker run --rm \
    --user 0:0 \
    --entrypoint sqlite3 \
    -v "$data:/data" \
    beacon-backup:ci \
    /data/beacon.db \
    "CREATE TABLE __EFMigrationsHistory (MigrationId TEXT NOT NULL PRIMARY KEY, ProductVersion TEXT NOT NULL); INSERT INTO __EFMigrationsHistory VALUES ('smoke', '10.0.0'); CREATE TABLE RecoveryProbe (Value TEXT NOT NULL); INSERT INTO RecoveryProbe VALUES ('recoverable');"

docker run --detach --name "$container" \
    -e BACKUP_INTERVAL_SECONDS=3600 \
    -e BACKUP_RETENTION_DAYS=30 \
    -v "$data:/data:ro" \
    -v "$backups:/backups" \
    beacon-backup:ci >/dev/null

attempt=0
while [ "$attempt" -lt 30 ]; do
    archive="$(find "$backups" -maxdepth 1 -type f -name 'beacon-*.tar.gz' -print -quit)"
    [ -n "$archive" ] && break
    attempt=$((attempt + 1))
    sleep 1
done

[ -n "${archive:-}" ]
[ -f "$archive.sha256" ]
(cd "$backups" && sha256sum -c "$(basename "$archive.sha256")")

tar -xzf "$archive" -C "$restore"
[ -s "$restore/beacon.db" ]
[ "$(docker run --rm --entrypoint sqlite3 -v "$restore:/restore:ro" beacon-backup:ci /restore/beacon.db 'PRAGMA integrity_check;')" = "ok" ]
[ "$(docker run --rm --entrypoint sqlite3 -v "$restore:/restore:ro" beacon-backup:ci /restore/beacon.db 'SELECT Value FROM RecoveryProbe;')" = "recoverable" ]
[ "$(docker run --rm --entrypoint sqlite3 -v "$restore:/restore:ro" beacon-backup:ci /restore/beacon.db 'SELECT COUNT(*) FROM __EFMigrationsHistory;')" = "1" ]

echo 'Beacon backup recovery checks passed.'
