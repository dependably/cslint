# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build
dotnet build
dotnet build -c Release

# Test (requires cslint installed globally first)
dotnet tool install --global Dependably.CsLint
bash tests/RunTests.sh

# Run a single fixture test manually
cslint tests/fixtures/sast/SAST001_EmptyCatch.cs --sast
cslint tests/fixtures/editorconfig/EC001_Indent.cs
cslint tests/fixtures/opinionated/OP004_MagicNumbers.cs --scan

# Install as global tool from source
dotnet pack && dotnet tool install --global --add-source ./nupkg Dependably.CsLint
```

## Architecture

cslint is a .NET 10 global CLI tool that lints C# files across four tiers: EditorConfig enforcement, SAST (security), Roslyn semantic analysis, and opinionated pattern scans. It reads all configuration from `.editorconfig` — no separate config file. Quantitative code metrics are a separate concern, owned by the companion `codemetrics` tool.

### Execution Flow

`Program.cs` parses CLI args into `CliOptions`, resolves target files (staged via git, or explicit paths), then delegates to `LintEngine`. The engine selects rules based on `LintMode` (EditorConfig / EditorConfigAndSast / All), runs them in parallel via `Parallel.ForEachAsync`, and returns diagnostics. If `--deep` is passed, `SemanticEngine` additionally opens the `.csproj` via `MSBuildWorkspace` and surfaces Roslyn compiler diagnostics. `Reporter` formats the result as text, JSON, or GitHub Actions annotations.

### Rule System

All rules implement `IRule` (`src/Rules/IRule.cs`). Two base classes exist:
- **`TextRule`** — operates on the raw file text (line-by-line); override `Analyze()` and optionally `ApplyFix()`
- **`SyntaxRule`** — operates on a Roslyn `SyntaxTree`; parses the file and walks `DescendantNodes()`

Rules are instantiated once in `LintEngine` and reused across files. `AppliesTo(FileConfig)` gates whether a rule runs for a given file (e.g., check if the relevant `.editorconfig` key is present).

**EditorConfig value parsing:** Use `StyleHelper.TryGet(config, "key", out var value, out var severity)` to parse `value:severity` strings. Returns `false` if severity is `none`/`silent` (rule suppressed).

### Key Files

| File | Purpose |
|------|---------|
| `src/Program.cs` | CLI entry, arg parsing, main orchestration |
| `src/LintEngine.cs` | Rule registry, parallel execution, mode routing |
| `src/EditorConfigLoader.cs` | Walks directory tree, parses `.editorconfig`, glob matching with `{}` brace expansion, caches results |
| `src/SemanticEngine.cs` | MSBuildWorkspace integration, applies `dotnet_diagnostic.*` severity overrides from `.editorconfig` |
| `src/Reporting.cs` | Text/JSON/GitHub output formatting |
| `src/Rules/IRule.cs` | `IRule` interface, `TextRule`/`SyntaxRule` base classes, `Diagnostic` record |
| `src/Rules/StyleHelper.cs` | Parses `value:severity` format, handles suppression |
| `src/Rules/Sast/SastRules.cs` | SAST001–SAST008 (security checks via syntax tree) |
| `src/Rules/Opinionated/OpinionatedRules.cs` | OP004–OP006 (opinionated patterns: magic numbers, bool flags, CancellationToken). Metric gates OP001–OP003 removed in v4.0.0 — owned by the codemetrics tool. |

### Lint Modes

```
EditorConfig             → EC001-006, FMT, CS010-040
EditorConfigAndSast      → + SAST001-008   (--sast)
All                      → + OP004-006     (--scan, implies --sast)
--deep --project foo.csproj → + Roslyn compiler diagnostics
```

### Testing

Tests are bash-driven integration tests. `tests/RunTests.sh` invokes `cslint` against fixture files and checks that expected rule IDs appear (or are absent) in stdout. Each fixture in `tests/fixtures/{tier}/` is a `.cs` file containing deliberate violations.

To add a new rule: implement `IRule`, register it in `LintEngine`, add a fixture file under `tests/fixtures/`, add `check()` calls in `RunTests.sh`.

### Conventions

- All I/O is `async`; rule analysis uses `Task<IReadOnlyList<Diagnostic>>`
- Core data types (`Diagnostic`, `FileConfig`, `EditorConfigSection`, `ScanConfig`) are immutable `record` types
- Nullable reference types are enabled project-wide
- The pre-commit hook blocks commits that stage `.editorconfig` changes (prevents silent rule relaxation)
