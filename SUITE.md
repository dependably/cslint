# The Dependably suite

Three independent .NET global tools that together cover the three pillars of C#
code health. They are **one product brand, shipped as three separate tools in three
separate repositories** — deliberately not merged and not a monorepo.

| Pillar | Tool | Package id | Command | Repo | Answers |
|--------|------|-----------|---------|------|---------|
| **Security — dependencies** | nuget-check | `Dependably.NuGetCheck` | `nuget-check` | checker-nuget | "Are my packages vulnerable?" |
| **Style & safety — rules** | cslint (was csedlint, v4) | `Dependably.CsLint` | `cslint` | checker-csedlint | "Does my code break the rules?" |
| **Health — metrics** | codemetrics | `Dependably.CodeMetrics` | `codemetrics` | dotnet-codemetrics | "How complex / maintainable is my code?" |

## Why three tools, not one

`nuget-check` is a different domain entirely — software-composition analysis, not source
analysis. It shares no engine with the other two.

`cslint` and `codemetrics` are both Roslyn source analyzers, so they *look* mergeable, but
they do different jobs:

- **cslint is a gate.** Pass/fail. Runs on every commit/PR, blocks the merge, auto-fixes.
  Audience: the developer at commit time.
- **codemetrics is a measurement.** Numbers, trends, "god class" diagnoses, worst-offender
  tables, streaming JSON for huge repos. Runs periodically or feeds a dashboard. Audience:
  the tech lead / architect reviewing health over time.

A linter and a metrics reporter have different output shapes, run cadences, and consumers.
Keeping them separate also keeps the suite's taxonomy clean and legible — the same way
SonarQube separates *issues* (rules) from *measures* (metrics), and NDepend is distinct
from a linter. If the shared Roslyn parse/walk plumbing ever becomes painful to maintain in
two places, extract a shared internal `Dependably.Roslyn` package — do **not** merge the
tools.

## Conventions (what "being in the suite" means)

- **Naming.** Package id is `Dependably.<Name>`; the command keeps its short form
  (`nuget-check`, `cslint`, `codemetrics`). `Company = Dependably`,
  `Authors = MHiland <michael@dependably.ca>`, `PackageLicenseExpression = Apache-2.0`.
- **Runtime.** All three target `net10.0` — the current .NET LTS (Nov 2025 → Nov 2028). One
  SDK, one CI image (`mcr.microsoft.com/dotnet/sdk:10.0`).
- **CI/CD.** Each repo carries the same pipeline shape, templated from `nuget-check`:
  - GitLab (`.gitlab-ci.yml`) is the source of truth:
    `test → pack → sbom → sbom-publish → publish (manual) → mirror-to-github (manual)`.
  - GitHub Actions (`.github/workflows/ci.yml`) runs on the public mirror and produces
    **SLSA Build L2** signed provenance on `v*` tags.
  - Secret scan (TruffleHog, pinned digest), Snyk SAST (advisory), SonarQube (advisory)
    are shared. Side-effecting jobs are `manual` or rule-gated on their secret being
    present, so a repo can adopt the pipeline before its infra (DT project, Sonar key,
    registry, GitHub mirror) exists.
- **Distribution.** Private NuGet feed at `https://dependably.northwardlabs.ca` for
  internal installs; nuget.org + signed GitHub Releases for public ones.

## Known overlap — the line drawn (DONE in cslint v4.0.0)

`cslint`'s `--scan` (opinionated) tier used to gate on **cyclomatic complexity, nesting
depth, method length, and parameter count** (`--max-complexity`, `--max-nesting`,
`--max-lines`, `--max-params`) — rules OP001–OP003. `codemetrics` computes a strict superset
of those, more rigorously.

**Resolved:** as of **cslint v4.0.0**, OP001–OP003 and the four `--max-*` flags were removed.
`codemetrics` is now the single authoritative owner of "how complex / how coupled / how
maintainable." `cslint`'s `--scan` keeps only categorical pass/fail *pattern* rules
(OP004 magic numbers, OP005 flag arguments, OP006 missing CancellationToken), alongside its
EditorConfig, formatting, C# style, naming, and **SAST** tiers. This was a breaking change,
hence the major version bump (3.0.0 → 4.0.0).

## Shared config — `.dependably-check` (DONE)

All three tools read one repo-root `.dependably-check` JSON file, discovered by walking up
from the working/target directory to the repo boundary (a `.git` entry). Each tool reads the
shared `common` section plus its own section (the tool section overrides `common`). An
explicit path can be passed with `--config <file>`. **CLI options always win over the file.**

```json
{
  "common": {
    "allowedRegistryHosts": ["nuget.mycorp.example"]
  },
  "nuget": {
    "allowedRegistryHosts": ["extra-feed.mycorp.example"]
  },
  "cslint": {
    "strict": false,
    "scan": { "magicNumbers": true, "boolFlags": true, "cancellation": true }
  },
  "codemetrics": {
    "failOn":  { "cyclomatic": 25, "cognitive": 30, "nesting": 5, "lcom4": 4, "coupling": 20, "mi": 20 },
    "exclude": ["**/Generated/**", "Migrations"]
  }
}
```

Per-tool semantics:
- **nuget-check** — `common.allowedRegistryHosts ∪ nuget.allowedRegistryHosts` (trusted feeds).
- **cslint** — `strict` and the `scan` toggles. A CLI flag can only further restrict: `--strict`
  forces strict on; `--no-magic-numbers`/`--no-bool-flags`/`--no-cancellation` force a rule off.
- **codemetrics** — `failOn` thresholds (a per-metric `--fail-on` overrides the same metric) and
  `exclude` globs (CLI `--exclude` adds to them). Unknown metrics/keys are ignored.

Each tool ships its own small loader (`DependablyCheckConfig` / `CsLintConfig` /
`CodeMetricsConfig`) with identical discovery semantics — no shared package yet (see the
keep-separate note above); extract `Dependably.Config` only if these drift.

## Status checklist

- [x] Brand convergence: all three on `Dependably.*` package ids + shared metadata
- [x] `csedlint` renamed to `cslint`
- [x] CI/SLSA/SBOM pipeline templated onto `cslint` and `codemetrics`
- [x] Follow-up #1: trimmed `cslint --scan` metric gates (OP001–OP003 removed in v4.0.0); `codemetrics` owns metrics
- [ ] Create infra for the new repos: GitLab projects, GitHub mirrors
      (`dependably/cslint`, `dependably/codemetrics`), SonarQube projects (replace the
      `REPLACE_WITH_SONAR_PROJECT_KEY` placeholders), Dependency-Track projects
- [ ] Commit `packages.lock.json` to `cslint`/`codemetrics` and enable `--locked-mode`
- [ ] Add a real test project to `codemetrics` (CI currently smoke-runs the tool)
- [x] Follow-up #2: `.dependably-check` config wired into `cslint` and `codemetrics` (with `--config`)
- [x] Standardized all three tools on `net10.0` (current LTS)
