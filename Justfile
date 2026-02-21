build_path := "build"
src_path := "src/python"
test_path := "test"
app_path := "app"

# Support local Fable dev: just dev=true build
dev := "false"
fable := if dev == "true" { "dotnet run --project ../Fable/src/Fable.Cli --" } else { "dotnet fable" }

# BEAM compiler is always the separate fable-beam CLI (not available in standard dotnet fable)
fable_beam := "dotnet run --project ../fable/main/src/Fable.Cli --"

default:
    @just --list

clean:
    rm -rf {{build_path}}

build: clean
    mkdir -p {{build_path}}
    {{fable}} {{src_path}} --exclude Fable.Core --lang Python --outDir {{build_path}}/lib

build-beam: clean
    mkdir -p {{build_path}}/beam
    {{fable_beam}} src/beam --exclude Fable.Core --lang beam --outDir {{build_path}}/beam

app: clean
    mkdir -p {{build_path}}
    {{fable}} {{app_path}} --exclude Fable.Core --lang Python
    cd {{app_path}} && uv run uvicorn program:app --port 8080 --workers 1 --log-level error

app-beam: build-beam
    mkdir -p {{build_path}}/beam/app
    {{fable_beam}} app/beam --exclude Fable.Core --lang beam --outDir {{build_path}}/beam/app
    cp {{build_path}}/beam/*.erl app/beam/src/
    cp {{build_path}}/beam/app/*.erl app/beam/src/
    cp {{build_path}}/beam/fable_modules/fable-library-beam/*.erl app/beam/src/
    cd app/beam && rebar3 compile && rebar3 shell

test: build
    dotnet build {{test_path}}
    dotnet run --project {{test_path}}
    {{fable}} {{test_path}} --lang Python --outDir {{build_path}}/tests
    uv run python -m pytest {{build_path}}/tests

test-native:
    dotnet build {{test_path}}
    dotnet run --project {{test_path}}

test-python: build
    {{fable}} {{test_path}} --lang Python --outDir {{build_path}}/tests
    uv run python -m pytest {{build_path}}/tests

pack: build
    dotnet pack -c Release {{src_path}}

format:
    dotnet fantomas src -r
    dotnet fantomas {{test_path}} -r

setup:
    dotnet tool restore
    uv sync
