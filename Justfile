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

# Create NuGet packages with specific version (used in CI)
pack-version version:
    dotnet pack -c Release -p:PackageVersion={{version}} -p:InformationalVersion={{version}} {{src_path}}

# Run EasyBuild.ShipIt for release management
shipit *args:
    dotnet shipit --pre-release rc {{args}}
