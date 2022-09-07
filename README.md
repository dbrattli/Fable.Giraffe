# Fable.Giraffe

[![Build and Test](https://github.com/dbrattli/Fable.Giraffe/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/dbrattli/Fable.Giraffe/actions/workflows/build-and-test.yml)¨

Fable.Giraffe is a port of the
[Giraffe](https://github.com/giraffe-fsharp/Giraffe) F# library to
[Fable](https://github.com/fable-compiler/Fable/) and
[Fable.Python](https://github.com/fable-compiler/Fable.Python). Giraffe
is high performance, functional ASP.NET Core micro web framework for
building rich web applications.

## Build

To build Fable.Giraffe, run:

```console
> poetry install
> dotnet run Build
```

Building Fable.Giraffe for development purposes may require the very
latest Fable compiler. You can build against the latest version of Fable
by running e.g:

```console
> dotnet run --project ..\..\..\Fable\src\Fable.Cli --lang Python
```

Remember to build fable library first if needed e.g run in Fable
directory:

```console
> dotnet fsi build.fsx library-py
```

## Running

```console
> dotnet run Run
```

If you want to start the server manually, you can run:

```console
> poetry run uvicorn program:app  --port "8080" --workers 20

```

## Testing

```console
> dotnet run Test
```
