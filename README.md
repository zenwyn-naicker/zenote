# Zenote

Let your documents talk. A simpler, cleaner, fully local take on NotebookLM for macOS.

> Early development. Inspired by Google's NotebookLM; not affiliated with Google.

## Requirements

- Apple Silicon Mac
- .NET 10 SDK
- [Ollama](https://ollama.com/download), running, with these models:

```bash
ollama pull llama3.2
ollama pull nomic-embed-text
```

## Run

```bash
dotnet run --project src/Zenote.App
```

The status bar shows whether Ollama, both models, sqlite-vec and page rendering are working.
To print the same checks without opening a window:

```bash
dotnet run --project src/Zenote.App -- --self-check
```

## Test

```bash
dotnet test --solution Zenote.slnx
```

Live model tests skip themselves when Ollama isn't running.
