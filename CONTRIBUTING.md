# Contributing to cslint

Thanks for your interest in improving cslint. This guide covers the essentials for getting set up and landing a change.

## Prerequisites

- .NET 10 SDK

## Build

```bash
dotnet build
```

## Run the unit tests

The xUnit suite needs no global install — it exercises the rule, engine, config, and CLI surface directly:

```bash
dotnet test tests/CsLint.Tests/CsLint.Tests.csproj
```

There is also an integration layer (`tests/RunTests.sh`) that runs the *installed* tool against fixture files. It requires the packed tool on your PATH:

```bash
dotnet pack && dotnet tool install --global --add-source ./nupkg Dependably.CsLint
bash tests/RunTests.sh
```

## How cslint is structured

cslint lints C# across four tiers: EditorConfig enforcement, syntactic SAST (`--sast`), Roslyn semantic analysis (`--deep`), and opinionated pattern scans (`--scan`). All configuration is read from `.editorconfig` — there is no separate rule-config file.

`Program.cs` parses CLI args, resolves the target files (git-staged or explicit paths, minus any `--exclude` globs), and hands off to `LintEngine`, which selects rules by mode and runs them in parallel. `SemanticEngine` opens the project via `MSBuildWorkspace` for `--deep`. `Reporting.cs` formats text, JSON, or GitHub Actions output.

Every rule implements `IRule` (`src/Rules/IRule.cs`). Two base classes are available:

- **`TextRule`** — operates on raw file text (line by line).
- **`SyntaxRule`** — operates on a Roslyn `SyntaxTree`.

`AppliesTo(FileConfig)` gates whether a rule runs for a given file (for example, whether the relevant `.editorconfig` key is present). Use `StyleHelper.TryGet(...)` to parse `value:severity` option strings; it returns `false` when the severity is `none`/`silent`.

## Adding a rule

1. Implement `IRule` (extending `TextRule` or `SyntaxRule`).
2. Register it in `LintEngine`.
3. Add a unit test under `tests/CsLint.Tests/`.
4. Add a fixture file under `tests/fixtures/` and a corresponding `check()` call in `tests/RunTests.sh`.

## Conventions

- Rule analysis is `async` and returns `Task<IReadOnlyList<Diagnostic>>`.
- Core data types (`Diagnostic`, `FileConfig`, `EditorConfigSection`, `ScanConfig`) are immutable `record` types.
- Nullable reference types are enabled project-wide.

## Submitting a pull request

Keep changes focused, and make sure the unit tests pass before you open a PR:

```bash
dotnet test tests/CsLint.Tests/CsLint.Tests.csproj
```

A green test run is the baseline expectation for any PR. If you change or add a rule, include the tests and fixtures that lock in its behavior.
