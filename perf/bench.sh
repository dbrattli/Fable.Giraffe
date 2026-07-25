#!/usr/bin/env bash
#
# Fair, reproducible throughput benchmark for Fable.Giraffe across every target.
#
# Each target serves an identical `/ping` handler with NO per-request logging
# (the Fable access log is Debug-gated and left off; the .NET reference clears
# its providers). oha then drives the same load at every target.
#
# Usage:
#   perf/bench.sh                    # all targets: dotnet python js beam
#   perf/bench.sh python js          # a subset
#   REQUESTS=20000 CONNECTIONS=200 perf/bench.sh dotnet
#
# Env:
#   REQUESTS     total requests   (default 10000)
#   CONNECTIONS  concurrency      (default 100)
#   FABLE        fable command    (default "dotnet fable")
set -uo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO"

REQUESTS="${REQUESTS:-10000}"
CONNECTIONS="${CONNECTIONS:-100}"
FABLE="${FABLE:-dotnet fable}"

port_of() { case "$1" in dotnet) echo 8083 ;; python) echo 8081 ;; js) echo 8082 ;; beam) echo 8084 ;; esac; }

SERVER_PGID=""
LOG=/tmp/perf-server.log

# Run a command in its own process group so the whole tree can be torn down.
start_bg() {
    setsid bash -c "$1" >"$LOG" 2>&1 &
    SERVER_PGID=$!
}

stop_bg() {
    [ -n "$SERVER_PGID" ] || return 0
    kill -TERM -"$SERVER_PGID" 2>/dev/null
    wait "$SERVER_PGID" 2>/dev/null
    SERVER_PGID=""
}

trap stop_bg EXIT

wait_ready() {
    local port="$1" i
    for i in $(seq 1 150); do
        curl -sf "http://127.0.0.1:$port/ping" >/dev/null 2>&1 && return 0
        # Bail early if the server process already died.
        kill -0 -"$SERVER_PGID" 2>/dev/null || { echo "  !! server exited during startup"; return 1; }
        sleep 0.2
    done
    echo "  !! server on port $port never answered /ping"
    return 1
}

# --- per-target build (foreground) + serve command ---------------------------

build_dotnet() { dotnet build -c Release "$REPO/perf/dotnet" >/dev/null; }
serve_dotnet() { echo "exec dotnet '$REPO/perf/dotnet/bin/Release/net8.0/Perf.Dotnet.dll'"; }

build_python() { $FABLE perf/python --exclude Fable.Core --lang Python >/dev/null; }
serve_python() { echo "cd '$REPO/perf/python' && exec uv run --project '$REPO' uvicorn program:app --port 8081 --workers 1 --log-level error"; }

build_js() {
    npm install >/dev/null 2>&1
    $FABLE perf/js --exclude Fable.Core --lang javascript --outDir build/perf-js >/dev/null
    echo '{"type":"module"}' >build/perf-js/package.json
}
serve_js() { echo "exec node '$REPO/build/perf-js/Program.js'"; }

build_beam() {
    # build-beam does clean-beam + compiles the library into build/apps/giraffe.
    just build-beam >/dev/null 2>&1
    # Compile the perf app alongside it.
    $FABLE perf/beam --exclude Fable.Core --lang beam --outDir build/apps/giraffe_perf >/dev/null
    # The repo rebar.config only declares the example app; point rebar at the perf app instead.
    cat >build/rebar.config <<'EOF'
{erl_opts, [no_debug_info, nowarn_unused_result]}.
{deps, [{cowboy, "2.12.0"}, jsx]}.
{project_app_dirs, [
    "apps/giraffe", "apps/giraffe/fable_modules/*",
    "apps/giraffe_perf", "apps/giraffe_perf/fable_modules/*"
]}.
EOF
    (cd build && rebar3 compile >/dev/null 2>&1)
}
serve_beam() {
    echo "cd '$REPO' && exec erl -pa build/_build/default/lib/*/ebin -noshell \
        -eval 'application:ensure_all_started(cowboy)' \
        -eval 'perf_beam_program:start()' \
        -eval 'receive stop -> ok end'"
}

# --- driver ------------------------------------------------------------------

run_one() {
    local target="$1" port
    port="$(port_of "$target")"
    echo "=================================================================="
    echo " $target  (port $port, $REQUESTS req / $CONNECTIONS conn, logging off)"
    echo "=================================================================="

    echo "-- building..."
    if ! "build_$target"; then
        echo "  !! build failed; see output above. skipping $target."
        return 1
    fi

    echo "-- starting server..."
    start_bg "$(serve_$target)"

    if ! wait_ready "$port"; then
        echo "  --- server log (tail) ---"
        tail -n 20 "$LOG"
        stop_bg
        return 1
    fi

    echo "-- warming up..."
    oha -n 1000 -c "$CONNECTIONS" --no-tui "http://127.0.0.1:$port/ping" >/dev/null 2>&1

    echo "-- benchmarking..."
    oha -n "$REQUESTS" -c "$CONNECTIONS" --no-tui "http://127.0.0.1:$port/ping" \
        | grep -E "Success rate|Requests/sec|Total:|Slowest:|Fastest:|Average:|^\s+50\.00%|^\s+99\.00%" \
        | sed 's/^/   /'

    stop_bg
    echo
}

TARGETS=("$@")
[ ${#TARGETS[@]} -eq 0 ] && TARGETS=(dotnet python js beam)

for t in "${TARGETS[@]}"; do
    run_one "$t" || true
done
