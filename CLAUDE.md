# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build
dotnet build
dotnet build -c Release

# Unit tests (xUnit; no global install needed) + coverage
dotnet test tests/CsLint.Tests/CsLint.Tests.csproj
dotnet test tests/CsLint.Tests/CsLint.Tests.csproj --collect:"XPlat Code Coverage"

# Integration tests (requires the packed cslint installed globally first)
dotnet pack && dotnet tool install --global --add-source ./nupkg Dependably.CsLint
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

`Program.cs` parses CLI args into `CliOptions`, resolves target files (staged via git, or explicit paths), then delegates to `LintEngine`. Target resolution drops paths matching any `--exclude`/`.dependably-check` `exclude` glob via `PathFilter` (`src/PathFilter.cs`). The engine selects rules based on `LintMode` (EditorConfig / EditorConfigAndSast / All), runs them in parallel via `Parallel.ForEachAsync`, and returns diagnostics. If `--deep` is passed, `SemanticEngine` additionally opens the `.csproj` via `MSBuildWorkspace` and surfaces Roslyn compiler diagnostics. `Reporter` formats the result as text, JSON, or GitHub Actions annotations.

**Per-finding severity / suppression:** `LintEngine.ApplySeverityOverride` consults `dotnet_diagnostic.<RuleId>.severity` from `.editorconfig` for EVERY rule (EC/CS/FMT/SAST/OP), not just `--deep` Roslyn diagnostics: `none`/`silent` drops the finding, otherwise it retunes severity. This is the sanctioned way to silence by-design findings (e.g. SAST002 console output in a CLI).

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
| `src/Reporting.cs` | Human/JSON/GitHub output formatting. JSON is the shared Dependably finding schema v1 envelope (`tool`/`toolVersion`/`schemaVersion`/`target`/`summary`/`findings`); severity uses the suite ladder (error→high, warning→low). |
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

Two complementary layers:

- **Unit tests** (`tests/CsLint.Tests/`, xUnit) exercise the internal rule/engine/config/CLI surface directly — no global install needed. The main project exposes its internals to the test assembly via `InternalsVisibleTo("CsLint.Tests")`. `TestSupport.cs` (`T`) has helpers: `T.Run(rule, code, cfg)` runs a rule against an in-memory snippet, `T.Cfg(...)` builds a `FileConfig` from editorconfig-style pairs, `T.CaptureOut(...)` captures `Console.Out`. The suite is run **serially** (`AssemblyInfo.cs` disables xUnit parallelization) because Reporter/CLI tests redirect the process-global `Console.Out`. Run with `--collect:"XPlat Code Coverage"` for a Cobertura report.
- **Integration tests** (`tests/RunTests.sh`) invoke the installed `cslint` against fixture files and check that expected rule IDs appear (or are absent) in stdout. Each fixture in `tests/fixtures/{tier}/` is a `.cs` file containing deliberate violations.

To add a new rule: implement `IRule`, register it in `LintEngine`, add a unit test under `tests/CsLint.Tests/`, add a fixture file under `tests/fixtures/`, and add `check()` calls in `RunTests.sh`.

### Conventions

- All I/O is `async`; rule analysis uses `Task<IReadOnlyList<Diagnostic>>`
- Core data types (`Diagnostic`, `FileConfig`, `EditorConfigSection`, `ScanConfig`) are immutable `record` types
- Nullable reference types are enabled project-wide
- The pre-commit hook blocks commits that stage `.editorconfig` changes (prevents silent rule relaxation)
