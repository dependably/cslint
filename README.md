# cslint

A fast, build-free C# linter with four tiers:

1. **EditorConfig enforcement** — enforces every key in your `.editorconfig`. If a key isn't set, the rule stays silent. No opinions of its own.
2. **Syntactic SAST** (`--sast`) — always-on security and safety checks that need no build step.
3. **Semantic analysis** (`--deep`) — loads your project through Roslyn's `MSBuildWorkspace` for higher-accuracy checks and `dotnet_diagnostic.*` severity overrides.
4. **Opinionated scan** (`--scan`) — categorical pattern checks: magic numbers, boolean flag parameters, missing `CancellationToken`.

## Install

Requires the .NET SDK 8.0 or later.

```bash
dotnet tool install --global Dependably.CsLint
```

This puts the `cslint` command on your PATH.

## Usage

```bash
cslint                                     # enforce .editorconfig on staged .cs files
cslint --sast                              # + security/safety checks
cslint --scan --global                     # + opinionated scan, over all files in the repo
cslint --deep --project src/App.csproj     # + semantic analysis (run after dotnet build)
cslint --explain src/MyService.cs          # show which rules apply to a file and why
```

### Failing the build (`--fail-on`)

`--fail-on <key>=<value>` is the CI gate (repeatable). The process exits `1` if any rule trips:

- `severity=<critical|high|moderate|low|info>` — trips when a finding is at or above the level. cslint emits `high` (its errors) and `low` (its warnings), so `severity=high` gates on errors and `severity=warning` gates on warnings too.
- `count=<N>` — trips when the total number of findings exceeds `N`.

```bash
cslint --sast                              # default: errors fail, warnings don't
cslint --sast --fail-on severity=warning   # warnings fail too
cslint --global --fail-on count=0          # any finding fails
```

A bad value exits `2`.

### Output (`--format`)

- `human` (default) — readable, category-grouped console report.
- `json` — one JSON object on stdout (status to stderr); stable, machine-parseable.
- `github` — GitHub Actions `::error`/`::warning` annotations that appear as inline PR comments.

### Pre-commit hook

```bash
cslint --install-hook
```

Installs a hook that runs `cslint --sast --fail-on severity=warning`, and blocks commits that stage `.editorconfig` changes so rules can't be silently relaxed.

### CI

Run one job without a build and, optionally, a second after `dotnet build` for semantic analysis:

```yaml
on:
  pull_request:
    paths: ['**.cs', '.editorconfig']
jobs:
  cslint:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with: { fetch-depth: 0 }
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.x' }
      - run: dotnet tool install --global Dependably.CsLint
      - run: cslint --sast --fail-on severity=warning --format github
```

## Rules

**EditorConfig (always on):** `EC001`–`EC006` (indent, whitespace, final newline, EOL, line length, charset), `FMT` (all `csharp_space_*`/`indent_*`/`new_line_*` keys via Roslyn's formatter), and `CS010`–`CS040` (`var`, expression bodies, namespaces, pattern matching, qualification, naming, and other `csharp_style_*`/`dotnet_style_*` keys). Each rule is active only if the corresponding key is set in your `.editorconfig`.

**SAST (`--sast`):**

| Rule | Catches |
|------|---------|
| SAST001 | Empty or discard-only catch blocks |
| SAST002 | `Console.WriteLine` / `Debug.Write` in non-test files |
| SAST003 | SQL injection via interpolated strings in query sinks |
| SAST004 | Hardcoded credentials and placeholder secrets |
| SAST005 | Fire-and-forget async (unawaited `*Async()` calls) |
| SAST006 | `#pragma warning disable` without a justification |
| SAST007 | `Thread.Sleep()` inside async methods |
| SAST008 | `dynamic` used in type positions |

**Opinionated scan (`--scan`):**

| Rule | Catches |
|------|---------|
| OP004 | Magic numbers outside const/enum/attribute contexts |
| OP005 | Boolean flag parameters on public methods |
| OP006 | Missing `CancellationToken` on public async APIs |

## Configuration

Any rule's severity is set per file or glob from `.editorconfig` — the same mechanism cslint enforces:

```ini
[*.cs]
dotnet_diagnostic.SAST002.severity = none    # silence console output in a CLI app
dotnet_diagnostic.OP004.severity   = error   # promote magic numbers to errors
```

Levels are `none`/`silent` (drop the finding), `suggestion`, `warning`, and `error`, and apply to every rule.

Exclude paths with `--exclude <glob>` (repeatable). A pattern with no wildcard is a substring match; otherwise `**`/`*`/`?` glob against the path.

## License

Apache-2.0. See [LICENSE](LICENSE).
