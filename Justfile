build_path := "build"
src_path := "src"
test_path := "test"
app_path := "app"

# Support local Fable dev: just dev=true build
dev := "false"
fable := if dev == "true" { "dotnet run --project ../Fable/src/Fable.Cli --" } else { "dotnet fable" }

default:
    @just --list

clean:
    rm -rf {{build_path}}

build: clean
    mkdir -p {{build_path}}
    {{fable}} {{src_path}} --exclude Fable.Core --lang Python --outDir {{build_path}}/lib

app: clean
    mkdir -p {{build_path}}
    {{fable}} {{app_path}} --exclude Fable.Core --lang Python
    cd {{app_path}} && uv run uvicorn program:app --port 8080 --workers 1 --log-level error

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
    dotnet fantomas {{src_path}} -r
    dotnet fantomas {{test_path}} -r

setup:
    dotnet tool restore
    uv sync
