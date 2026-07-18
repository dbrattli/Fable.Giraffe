build_path := "build"
src_path := "src/python"
test_path := "test"
app_path := "app"

# Support local Fable dev: just dev=true build
dev := "false"
fable := if dev == "true" { "dotnet run --project ../Fable/src/Fable.Cli --" } else { "dotnet fable" }

# BEAM compiler: use local Fable checkout when dev=true, otherwise use dotnet fable
fable_beam := if dev == "true" { "dotnet run --project ../fable/main/src/Fable.Cli --" } else { "dotnet fable" }

default:
    @just --list

clean:
    rm -rf {{build_path}}

clean-beam:
    rm -rf {{build_path}}/apps {{build_path}}/_build

build: clean
    mkdir -p {{build_path}}
    {{fable}} {{src_path}} --exclude Fable.Core --lang Python --outDir {{build_path}}/lib

build-beam: clean-beam
    {{fable_beam}} src/beam --exclude Fable.Core --lang beam --outDir {{build_path}}/apps/giraffe
    cp rebar.config {{build_path}}/
    cd {{build_path}} && rebar3 compile

app: clean
    mkdir -p {{build_path}}
    {{fable}} {{app_path}} --exclude Fable.Core --lang Python
    cd {{app_path}} && uv run uvicorn program:app --port 8080 --workers 1 --log-level error

app-beam: build-beam
    {{fable_beam}} app/beam --exclude Fable.Core --lang beam --outDir {{build_path}}/apps/giraffe_app
    cd {{build_path}} && rebar3 compile
    erl -pa {{build_path}}/_build/default/lib/*/ebin -noshell -eval "application:ensure_all_started(cowboy)" -eval "program:start()" -eval "receive stop -> ok end"

# Compile the F# library to JavaScript (output: build/js/)
build-js: clean
    mkdir -p {{build_path}}
    {{fable}} src/js --exclude Fable.Core --lang javascript --outDir {{build_path}}/js

# Build + start the example app on Node's built-in http server (port 8080)
app-js: clean
    mkdir -p {{build_path}}
    {{fable}} app/js --exclude Fable.Core --lang javascript --outDir {{build_path}}/app-js
    echo '{"type":"module"}' > {{build_path}}/app-js/package.json
    node {{build_path}}/app-js/Program.js

# Run the shared behavioral suite across ALL Fable targets
test: test-python test-js test-beam

# Type-check the test projects on .NET (compile smoke only — the backends are Fable-only
# runtimes, so there is no pure-.NET behavioral run).
test-native:
    dotnet build test/python
    dotnet build test/js/Tests.fsproj

# Python target: compile the shared suite to Python and run the explicit runner
test-python:
    {{fable}} test/python --exclude Fable.Core --lang Python --outDir {{build_path}}/tests-py
    uv run python {{build_path}}/tests-py/main.py

# JS target: compile the shared suite to JS and run it under Node
test-js:
    {{fable}} test/js/Tests.fsproj --exclude Fable.Core --lang javascript --outDir {{build_path}}/test-js
    echo '{"type":"module"}' > {{build_path}}/test-js/package.json
    node test/js/run.mjs

# BEAM target: compile the shared suite to Erlang, build with rebar3, run on the BEAM VM
test-beam:
    {{fable}} test/beam --exclude Fable.Core --lang Erlang --outDir {{build_path}}/tests-beam
    cp test/beam/rebar.config {{build_path}}/tests-beam/rebar.config
    cd {{build_path}}/tests-beam && rebar3 compile
    cd {{build_path}}/tests-beam && erl -noshell -pa _build/default/lib/*/ebin -eval 'main:main([])'

pack: build
    dotnet pack -c Release {{src_path}}

format:
    dotnet fantomas src
    dotnet fantomas {{test_path}}

setup:
    dotnet tool restore
    uv sync

# Create NuGet packages with specific version (used in CI)
pack-version version:
    dotnet pack -c Release -p:PackageVersion={{version}} -p:InformationalVersion={{version}} {{src_path}}

# Run EasyBuild.ShipIt for release management
shipit *args:
    dotnet shipit --pre-release rc {{args}}
