# Fable.Giraffe

[![Build and Test](https://github.com/dbrattli/Fable.Giraffe/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/dbrattli/Fable.Giraffe/actions/workflows/build-and-test.yml)
[![Nuget](https://img.shields.io/nuget/vpre/Fable.Giraffe)](https://www.nuget.org/packages/Fable.Giraffe/)

Giraffe is high performance, functional ASP.NET Core micro web framework
for building rich web applications.

Fable.Giraffe is a port of the
[Giraffe](https://github.com/giraffe-fsharp/Giraffe) F# library to
[Fable](https://github.com/fable-compiler/Fable/) and
[Fable.Python](https://github.com/fable-compiler/Fable.Python). I.e
Fable.Giraffe is written in F# and runs on Python.

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

To run the test server:

```console
> dotnet run App
```

Note that Fable.Giraffe is a valid ASGI application so you can start the
server manually using servers like [uvicorn](https://www.uvicorn.org/):

```console

```console
> poetry run uvicorn program:app  --port "8080" --workers 20
```

## Testing

```console
> dotnet run Test
```
