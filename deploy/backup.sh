#!/bin/sh
set -eu

interval="${BACKUP_INTERVAL_SECONDS:-86400}"
retention_days="${BACKUP_RETENTION_DAYS:-30}"

backup_once() {
    [ -s /data/beacon.db ] || return 1

    # A SQLite file can exist before EF has finished applying the production schema.
    # Do not publish a recovery point until it is recognisably a Beacon database.
    [ "$(sqlite3 /data/beacon.db "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name = '__EFMigrationsHistory';")" = "1" ] || return 1
    [ "$(sqlite3 /data/beacon.db 'SELECT COUNT(*) FROM __EFMigrationsHistory;')" -gt 0 ] || return 1

    stamp="$(date -u +%Y%m%dT%H%M%SZ)"
    work="$(mktemp -d)"
    partial="/backups/.beacon-${stamp}.tar.gz.partial"
    final="/backups/beacon-${stamp}.tar.gz"

    trap 'rm -rf "$work" "$partial"' EXIT INT TERM

    sqlite3 /data/beacon.db <<EOF
.timeout 30000
.backup '$work/beacon.db'
EOF

    [ -s "$work/beacon.db" ]
    [ "$(sqlite3 "$work/beacon.db" 'PRAGMA integrity_check;')" = "ok" ]
    [ "$(sqlite3 "$work/beacon.db" "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name = '__EFMigrationsHistory';")" = "1" ]
    [ "$(sqlite3 "$work/beacon.db" 'SELECT COUNT(*) FROM __EFMigrationsHistory;')" -gt 0 ]

    mkdir -p "$work/images"
    if [ -d /data/images ]; then
        cp -a /data/images/. "$work/images/"
    fi

    tar -C "$work" -czf "$partial" beacon.db images
    mv "$partial" "$final"
    sha256sum "$final" | sed 's#  /backups/#  #' > "${final}.sha256"

    rm -rf "$work"
    trap - EXIT INT TERM

    find /backups -maxdepth 1 -type f -name 'beacon-*.tar.gz' -mtime "+$retention_days" -delete
    find /backups -maxdepth 1 -type f -name 'beacon-*.tar.gz.sha256' -mtime "+$retention_days" -delete
}

while :; do
    if backup_once; then
        wait_seconds="$interval"
    else
        # The backup sidecar can win the startup race with the server on a brand-new deployment.
        # Retry quickly until the first database exists instead of waiting a full day.
        wait_seconds=30
    fi
    sleep "$wait_seconds" &
    wait $!
done
