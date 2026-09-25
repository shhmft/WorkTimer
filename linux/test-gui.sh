#!/bin/bash
set -euo pipefail

test_dir=$(mktemp -d)
export XDG_DATA_HOME="$test_dir/data"
export XDG_CONFIG_HOME="$test_dir/config"
instance_file="$XDG_DATA_HOME/worktimer/.instance.lock"
sessions_file="$XDG_DATA_HOME/worktimer/sessions.csv"

worktimer --hidden &
app_pid=$!
cleanup() {
    if kill -0 "$app_pid" 2>/dev/null; then
        kill -TERM "$app_pid"
        wait "$app_pid" || true
    fi
}
trap cleanup EXIT

for _ in $(seq 1 50); do
    if ! kill -0 "$app_pid" 2>/dev/null; then
        wait "$app_pid"
        echo 'WorkTimer завершился при запуске' >&2
        exit 1
    fi
    if [ -s "$instance_file" ]; then
        break
    fi
    sleep 0.1
done
test -s "$instance_file"
sleep 1

worktimer --toggle
sleep 2
worktimer --toggle

/usr/bin/python3 - "$sessions_file" <<'PY'
import datetime as dt
import pathlib
import sys

rows = pathlib.Path(sys.argv[1]).read_text(encoding="utf-8").splitlines()
assert len(rows) == 1, rows
start, end = (dt.datetime.fromisoformat(value) for value in rows[0].split("|"))
assert end > start, (start, end)
PY

worktimer --hidden
kill -0 "$app_pid"
kill -TERM "$app_pid"
wait "$app_pid"
trap - EXIT

if worktimer --toggle; then
    echo 'Команда --toggle успешно выполнилась без приложения' >&2
    exit 1
fi
