# Giraffe.Python

## Build

To build giraffe.python, run:

```console
> poetry install
> dotnet run Build
```

Building Giraffe.Python for development purposes may require the very
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
