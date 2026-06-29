# jjkWebFunctions2

This project is an Azure Functions app built with .NET.

## Prerequisites

- .NET SDK
- Azure Functions Core Tools

## Run locally

```bash
dotnet build
func start
```

## Project structure

- `HttpTrigger1.cs` - example HTTP trigger
- `Program.cs` - startup configuration
- `host.json` - Azure Functions host configuration
- `jjkWebFunctions2.csproj` - project file

## Notes

The local settings file is not committed by default and should contain your local Azure Functions configuration.
