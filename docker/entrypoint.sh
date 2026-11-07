#!/bin/sh
# Drops root privileges before starting the app.
#
# Started as root (default): fixes ownership of the data directories and
# re-executes the command as PUID:PGID (default 1654:1654, the "app" user of
# the .NET base image).
# Started with --user / "user:" in compose: runs the command unchanged; the
# mounted data directories must then already be writable for that user.
set -eu

if [ "$(id -u)" != "0" ]; then
    exec "$@"
fi

PUID="${PUID:-1654}"
PGID="${PGID:-1654}"

case "$PUID:$PGID" in
    *[!0-9:]*|:*|*:)
        echo "entrypoint: PUID and PGID must be numeric (got '$PUID:$PGID')" >&2
        exit 1
        ;;
esac

if [ "$PUID" = "0" ]; then
    echo "entrypoint: PUID=0 is not allowed, the app must not run as root" >&2
    exit 1
fi

# Directories the app writes to: the default locations (database, uploads,
# backups) and the configured Storage__RootPath / Backup__Path (if absolute).
# Every entry with another owner is handed over, also single files copied in
# as root (e.g. a database restored from a backup). find only reads metadata,
# so restarts stay fast with many uploads.
for dir in /app/App_Data /app/data /app/uploads /app/backups "${Storage__RootPath:-}" "${Backup__Path:-}"; do
    case "$dir" in
        /*) ;;
        *) continue ;;
    esac
    mkdir -p "$dir"
    changed=$(find "$dir" \( ! -user "$PUID" -o ! -group "$PGID" \) -exec chown "$PUID:$PGID" {} + -print | wc -l)
    if [ "$changed" -gt 0 ]; then
        echo "entrypoint: changed owner of $changed entries in $dir to $PUID:$PGID"
    fi
done

# The user may not exist in /etc/passwd; give the process a writable HOME.
export HOME=/tmp

exec setpriv --reuid="$PUID" --regid="$PGID" --clear-groups --no-new-privs -- "$@"
