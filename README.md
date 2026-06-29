# cslint

A C# linter with three distinct tiers:

1. **EditorConfig enforcement** — reads your `.editorconfig` and enforces every key it finds. If the key is not in your config, the rule is silent. Zero opinions of its own.
2. **Syntactic SAST** (`--sast`) — always-on security and safety checks that require no build step.
3. **Semantic analysis** (`--deep`) — loads your project via Roslyn's `MSBuildWorkspace`, enabling `dotnet_diagnostic.CSXXXX.severity` overrides from `.editorconfig` and higher-accuracy style checks.
4. **Opinionated scan** (`--scan`) — categorical pattern checks (magic numbers, flag arguments, missing `CancellationToken`). Quantitative metrics live in the companion `codemetrics` tool.

---

## Installation

Requires the .NET 10 SDK.

**From source (works today):**

```bash
dotnet pack && dotnet tool install --global --add-source ./nupkg Dependably.CsLint
```

**Once published to nuget.org:**

```bash
dotnet tool install --global Dependably.CsLint
```

---

## Quickstart

```bash
# Default: enforce .editorconfig on staged .cs files
cslint

# EditorConfig + SAST on staged files, fail on warnings
cslint --sast --strict

# Full audit of all files in the repo
cslint --scan --global --format json > findings.json

# Understand which rules apply to a specific file
cslint --explain src/MyService.cs

# Post-build semantic analysis
cslint --deep --project src/MyApp.csproj --strict --format github
```

---

## CI integration

Add two jobs to your GitHub Actions workflow. The first runs without a build, the second runs after `dotnet build` completes:

```yaml
# ci/github-actions.yml — copy to .github/workflows/cslint.yml in your repo
name: cslint

on:
  pull_request:
    paths: ['**.cs', '.editorconfig']

jobs:
  editorconfig-sast:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.x'
      - run: dotnet tool install --global Dependably.CsLint
      - run: cslint --sast --strict --format github --unstaged

  semantic:
    runs-on: ubuntu-latest
    needs: editorconfig-sast
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.x'
      - run: dotnet build
      - run: dotnet tool install --global Dependably.CsLint
      - run: cslint --deep --project src/MyApp.csproj --strict --format github
```

> The `dotnet tool install --global Dependably.CsLint` steps above assume the package is published to nuget.org. Until then, install from source instead (`dotnet pack && dotnet tool install --global --add-source ./nupkg Dependably.CsLint`). `setup-dotnet` can pin `8.x` or `10.x` — the tool ships for both.

The `--format github` flag emits `::error file=...` and `::warning file=...` annotations that appear as inline PR comments.

---

## Pre-commit hook

```bash
cslint --install-hook
```

Installs a `pre-commit` hook that runs `cslint --sast --strict`. It also blocks commits where `.editorconfig` is staged, preventing AI agents from silently relaxing rules through config changes.

---

## `--explain` mode

Shows which `.editorconfig` files were loaded, the effective merged properties, and which rules are active:

```
File: src/MyService.cs

EditorConfig files (lowest to highest priority):
  /home/user/.editorconfig
  /home/user/project/.editorconfig

Effective properties:
  csharp_style_namespace_declarations = file_scoped:warning
  dotnet_style_readonly_field = true:warning
  indent_style = space
  ...

Active EditorConfig rules:
  [EC001] [EC002] [EC003] [FMT] [CS010] [CS020] [CS033] ...

SAST rules (active when --sast):
  [SAST001] [SAST002] [SAST003] [SAST004] [SAST005] [SAST006] [SAST007] [SAST008]
```

---

## Rule reference

### Tier 1: EditorConfig enforcement

| Rule | EditorConfig keys covered |
|------|--------------------------|
| EC001 | `indent_style`, `indent_size`, `tab_width` |
| EC002 | `trim_trailing_whitespace` |
| EC003 | `insert_final_newline` |
| EC004 | `end_of_line` |
| EC005 | `max_line_length` |
| EC006 | `charset` (utf-8, utf-8-bom, utf-16be/le) |
| FMT | All `csharp_new_line_*`, `csharp_indent_*`, `csharp_space_*`, `csharp_preserve_*` via Roslyn Formatter |
| CS010 | `csharp_style_var_for_built_in_types`, `var_when_type_is_apparent`, `var_elsewhere` |
| CS011 | `csharp_style_expression_bodied_methods/constructors/operators/properties/indexers/accessors/lambdas/local_functions` |
| CS020 | `csharp_style_namespace_declarations` (file_scoped vs block_scoped) |
| CS021 | `csharp_style_pattern_matching_over_is_with_cast_check`, `_over_as_with_null_check`, `prefer_not_pattern` |
| CS022 | `csharp_style_throw_expression` |
| CS023 | `csharp_style_conditional_delegate_call` |
| CS024 | `csharp_style_unused_value_assignment_preference` |
| CS030 | `dotnet_style_qualification_for_field/property/method/event` |
| CS031 | `dotnet_style_predefined_type_for_locals_parameters_members`, `_for_member_access` |
| CS032 | `dotnet_style_require_accessibility_modifiers` |
| CS033 | `dotnet_style_readonly_field` |
| CS034 | `dotnet_style_object_initializer`, `collection_initializer` |
| CS035 | `dotnet_style_prefer_is_null_check_over_reference_equality_method` |
| CS036 | `dotnet_style_namespace_match_folder` |
| CS040 | `dotnet_naming_rule.*`, `dotnet_naming_symbols.*`, `dotnet_naming_style.*` |

### Tier 2: Syntactic SAST (`--sast`)

| Rule | What it catches |
|------|----------------|
| SAST001 | Empty catch blocks and discard-only catches |
| SAST002 | `Console.WriteLine` / `Debug.Write` in non-test files |
| SAST003 | SQL injection via interpolated strings in `ExecuteSqlRaw`, `ExecuteNonQuery`, etc. |
| SAST004 | Hardcoded credentials and placeholder values |
| SAST005 | Fire-and-forget async (unawaited `*Async()` calls) |
| SAST006 | `#pragma warning disable` without a justification comment |
| SAST007 | `Thread.Sleep()` inside async methods |
| SAST008 | `dynamic` used in type positions |

### Tier 3: Semantic analysis (`--deep --project foo.csproj`)

Runs after `dotnet build`. Requires `--project` pointing to a `.csproj` or `.sln`.

| Rule | What it catches |
|------|----------------|
| CS010-S | Accurate `var` style enforcement using resolved types |
| CS033-S | Accurate `readonly` field detection using data flow |
| IDE0005 | Unnecessary `using` directives (CS8019); enabled by `dotnet_diagnostic.IDE0005.severity` in `.editorconfig` |
| CSXXXX | `dotnet_diagnostic.CSXXXX.severity` overrides from `.editorconfig` applied to all Roslyn diagnostics |

### Tier 4: Opinionated scan (`--scan`)

Categorical, pass/fail *pattern* checks. (Quantitative *metric* gates — method length,
cyclomatic complexity, nesting depth, parameter count — were removed in v4.0.0 and are now
owned by the [`codemetrics`](https://github.com/dependably/codemetrics) tool, which measures
them more rigorously.)

| Rule | What it catches |
|------|----------------|
| OP004 | Magic numbers (not in const/enum/attribute) |
| OP005 | Boolean flag parameters in public methods |
| OP006 | Missing `CancellationToken` in public async APIs |

---

## `--scan` configuration

Each opinionated rule can be disabled at the command line:

```bash
cslint --scan --no-magic-numbers    # disable OP004
cslint --scan --no-bool-flags       # disable OP005
cslint --scan --no-cancellation     # disable OP006
```

---

## Configuration (`.dependably-check`)

cslint reads project-level defaults from a repo-root `.dependably-check` JSON file (shared
across the Dependably suite), discovered by walking up from `--root` to the repo boundary, or
passed explicitly with `--config <file>`. CLI flags always win over the file.

```json
{
  "common": { "exclude": ["tests/fixtures/**"] },
  "cslint": {
    "strict": false,
    "exclude": ["**/Generated/**"],
    "scan": { "magicNumbers": true, "boolFlags": true, "cancellation": true }
  }
}
```

### Excluding paths

`--exclude <glob>` (repeatable) and the `exclude` config arrays skip files from any selection
mode. A pattern with no wildcard is a substring match; otherwise `**`/`*`/`?` glob against the
path relative to `--root`. Use it for test fixtures, generated code, vendored sources, etc.

### Suppressing or retuning a finding

Any rule's severity is controllable per file/glob from `.editorconfig` — the same mechanism
cslint already uses for everything else:

```ini
[*.cs]
dotnet_diagnostic.SAST002.severity = none     # silence console-output in a CLI app
[src/Critical/*.cs]
dotnet_diagnostic.OP004.severity   = error    # promote magic numbers to an error here
```

Levels: `none`/`silent` (drop), `suggestion`, `warning`, `error`. This applies to every rule
(EC/CS/FMT/SAST/OP), so a by-design pattern is silenced with a visible, scoped justification
rather than left to nag.

---

## How `FormattingRule` (FMT) works

Rather than reimplementing 37 `csharp_space_*` / `csharp_indent_*` / `csharp_new_line_*` keys individually, `FormattingRule` delegates to Roslyn's own formatter:

1. Creates an `AdhocWorkspace`
2. Loads your `.editorconfig` files as `AnalyzerConfigDocument` entries
3. Runs `Formatter.FormatAsync(document)`
4. Diffs the original against the formatted output line by line

This means Roslyn's formatter is the source of truth — the same engine Visual Studio and `dotnet format` use.

---

## Architecture

| Component | Responsibility |
|-----------|---------------|
| `EditorConfigLoader` | Full EditorConfig spec: glob matching with `{}` brace expansion, hierarchy walking, `root = true` detection, caching. Returns `FileConfig` with merged properties **and** raw file content for Roslyn. |
| `LintEngine` | Routes files through the three rule tiers based on mode. Parallel execution via `Parallel.ForEachAsync`. |
| `SemanticEngine` | Loads project via `MSBuildWorkspace`. Applies `dotnet_diagnostic` severity overrides. Runs semantic `readonly` and `var` checks with full type information. |
| `EditorConfigLoader` | Caches `(sections, rawContent)` tuples so the raw file content can be fed to Roslyn's `AdhocWorkspace` without re-reading from disk. |
| `StyleHelper` | Parses `value:severity` style option format. Returns false for `none`/`silent` severity, suppressing the rule entirely. |

---

## Running tests

**Unit tests (no global install needed):**

```bash
dotnet test tests/CsLint.Tests/CsLint.Tests.csproj
```

The xUnit suite exercises the rule/engine/config/CLI surface directly.

**Integration fixtures (requires the packed tool installed globally):**

```bash
dotnet pack && dotnet tool install --global --add-source ./nupkg Dependably.CsLint
bash tests/RunTests.sh
```

Each integration test invokes the installed `cslint` against a known-bad fixture file and asserts the expected rule ID appears in the output.

---

## License

Apache-2.0. See [LICENSE](LICENSE).
