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

- `severity=<error|warning|suggestion|info>` — trips when a finding is at or above the level. This is the canonical severity vocabulary cslint prints on every finding and in the summary. The old shared-suite ladder words are still accepted as aliases: `error`=`high`, `warning`=`low`, `suggestion`=`info`, plus `critical` and `moderate`. So `severity=error` (a.k.a. `high`) gates on errors and `severity=warning` (a.k.a. `low`) gates on warnings too.
- `count=<N>` — trips when the total number of findings exceeds `N`.

```bash
cslint --sast                              # default: errors fail, warnings don't
cslint --sast --fail-on severity=warning   # warnings fail too
cslint --global --fail-on count=0          # any finding fails
```

A bad value exits `2`.

### Output (`--format`)

- `human` (default) — readable console report grouped by severity, **errors first**, so a handful of high-severity findings are never buried under a flood of warnings. Repeats of a noisy rule collapse into a `+N more <RULE> in M files` note, and the report ends with a per-rule frequency table. By default it prints up to 200 findings; `--max-findings <N>` changes the cap and `--no-limit` prints everything (no cap, no collapse).
- `json` — one JSON object on stdout (status to stderr); stable, machine-parseable. The JSON envelope keeps the shared-suite severity ladder (`high`/`low`/`info`) so it stays consistent across the Dependably suite.
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

### `.dependably` (shared suite config)

cslint participates in the Dependably suite's shared config file, `.dependably`, committed at the repo root. It reads the `common` section and its own `cslint` section. Supported keys:

```json
{
  "version": 1,
  "common": {
    "exclude": ["tests/fixtures/**"]
  },
  "cslint": {
    "rules": {
      "OP004": "off",
      "SAST002": "warn"
    },
    "exceptions": [
      {
        "rule": "OP004",
        "path": "src/Generated/**",
        "reason": "generated code, tracked in #42",
        "expires": "2027-01-01"
      }
    ],
    "exclude": ["**/Generated/**"],
    "failOn": { "severity": "warning" }
  }
}
```

- **`rules`** — per-rule severity (`"error"`, `"warn"`, `"off"`), merged with `common.rules` (tool section wins per rule-id). Disabling OP004/005/006 here is the canonical replacement for the deprecated `scan` toggles.
- **`exceptions`** — suppress specific findings without disabling a rule globally. Each entry needs `rule`, at least one selector (`path`, `symbol`, or `id`), and a `reason`. Suppressed findings are still counted in the report; set `expires` to flag stale suppressions.
- **`exclude`** — path globs; union of `common.exclude` and `cslint.exclude`.
- **`failOn`** — file-level CI gate (`severity` and/or `count`); a CLI `--fail-on` overrides it.

The deprecated `.dependably-check` filename is still read (with a stderr warning). The legacy `strict` and `scan` keys still work but emit deprecation warnings; prefer `failOn.severity` and `rules` respectively.

### `.editorconfig` per-file severity

Any rule's severity is set per file or glob from `.editorconfig` — the same mechanism cslint enforces:

```ini
[*.cs]
dotnet_diagnostic.SAST002.severity = none    # silence console output in a CLI app
dotnet_diagnostic.OP004.severity   = error   # promote magic numbers to errors
```

Levels are `none`/`silent` (drop the finding), `suggestion`, `warning`, and `error`, and apply to every rule.

Exclude paths with `--exclude <glob>` (repeatable). A pattern with no wildcard is a substring match; otherwise `**`/`*`/`?` glob against the path.

### `--global` file discovery

Under `--global`, cslint walks the tree without following directory symlinks/junctions (so `node_modules` symlink cycles can't loop), and prunes a built-in set of directories — `node_modules`, `bin`, `obj`, `.git`, `.claude`, `packages` — so vendored dependencies, build output, and throwaway worktrees don't drown first-party code. On top of that, when the root is a git repository it honors `.gitignore` (including nested ignore files and negation) by delegating to `git check-ignore`, so generated code and vendored paths your project already ignores are skipped too. Outside a git repo the built-in excludes are the sole guard. It prints how many files it skipped. Pass `--no-default-excludes` to walk the built-in-excluded and `.gitignore`'d directories too.

Test files (name ending `Test`/`Tests`/`Spec`, or under a `Tests`/`Specs` directory) default `OP004` (magic numbers) and `OP006` (missing `CancellationToken`) to off — literal expected values are idiomatic in assertions, and framework-invoked `[Fact]`/`[Theory]`/`[Test]`/`[TestMethod]` methods can't take a token. Any of these test-attributed methods is skipped by `OP006` even outside a test file. Re-enable per glob with an explicit `dotnet_diagnostic.OP004.severity` / `OP006.severity` in `.editorconfig`.

## License

Apache-2.0. See [LICENSE](LICENSE).
