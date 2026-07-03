using System.Text.Json;
using CsLint;
using CsLint.Config;
using Xunit;

namespace CsLint.Tests;

public class ExceptionApplierTests
{
    private const string Root = "/repo";

    private static IReadOnlyList<DependablyException> Exceptions(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return DependablyExceptions.Parse(doc.RootElement, "own", DependablyExceptions.CsLintSelectors, null);
    }

    private static Diagnostic Diag(string relPath, string rule, Severity sev = Severity.Error)
        => new(Path.Combine(Root, relPath), 10, 1, rule, "msg", sev);

    [Fact]
    public void No_exceptions_returns_input_untouched()
    {
        var diags = new[] { Diag("src/A.cs", "OP004") };
        var result = ExceptionApplier.Apply(diags, [], Root);
        Assert.Equal(diags.Length, result.Kept.Count);
        Assert.Equal(0, result.Suppressed);
        Assert.Empty(result.Notices);
    }

    [Fact]
    public void Suppresses_a_diagnostic_matching_rule_and_path()
    {
        var diags = new[]
        {
            Diag("src/Generated/G.cs", "OP004"),  // suppressed
            Diag("src/Hand/H.cs", "OP004"),       // kept (path doesn't match)
            Diag("src/Generated/G.cs", "SAST001"),// kept (rule doesn't match)
        };
        var exceptions = Exceptions(
            """[{ "rule": "OP004", "path": "src/Generated/**", "reason": "generated code" }]""");

        var result = ExceptionApplier.Apply(diags, exceptions, Root);

        Assert.Equal(1, result.Suppressed);
        Assert.Equal(2, result.Kept.Count);
        Assert.DoesNotContain(result.Kept, d => d.Rule == "OP004" && d.File.Contains("Generated"));
        Assert.Contains(result.Kept, d => d.Rule == "OP004" && d.File.Contains("Hand"));
        Assert.Contains(result.Kept, d => d.Rule == "SAST001");
        Assert.Contains(result.Notices, n => n == "1 finding(s) suppressed by exceptions");
    }

    [Fact]
    public void Suppressed_finding_does_not_gate()
    {
        // Only finding is suppressed => kept set is empty => nothing to gate on.
        var diags = new[] { Diag("src/Generated/G.cs", "OP004", Severity.Error) };
        var exceptions = Exceptions(
            """[{ "rule": "OP004", "path": "src/Generated/**", "reason": "generated" }]""");

        var result = ExceptionApplier.Apply(diags, exceptions, Root);

        Assert.Empty(result.Kept);
        Assert.DoesNotContain(result.Kept, d => d.Severity == Severity.Error);
    }

    [Fact]
    public void Unused_exception_emits_notice()
    {
        var diags = new[] { Diag("src/A.cs", "OP004") };
        var exceptions = Exceptions(
            """[{ "rule": "SAST001", "path": "nowhere/**", "reason": "never matches here" }]""");

        var result = ExceptionApplier.Apply(diags, exceptions, Root);

        Assert.Equal(0, result.Suppressed);
        Assert.Single(result.Kept);
        Assert.Contains(result.Notices, n => n == "unused exception for rule \"SAST001\" — never matches here");
    }

    [Fact]
    public void Expired_exception_does_not_suppress_and_emits_notice()
    {
        var diags = new[] { Diag("src/Generated/G.cs", "OP004") };
        var exceptions = Exceptions(
            """[{ "rule": "OP004", "path": "src/Generated/**", "reason": "stale", "expires": "2020-01-01" }]""");

        var result = ExceptionApplier.Apply(diags, exceptions, Root, todayOverride: new DateOnly(2026, 7, 3));

        Assert.Equal(0, result.Suppressed);
        Assert.Single(result.Kept); // not suppressed — the exception is expired
        Assert.Contains(result.Notices, n => n == "exception expired 2020-01-01 for rule \"OP004\" — stale");
    }

    [Fact]
    public void Live_exception_still_suppresses_before_expiry()
    {
        var diags = new[] { Diag("src/Generated/G.cs", "OP004") };
        var exceptions = Exceptions(
            """[{ "rule": "OP004", "path": "src/Generated/**", "reason": "ok", "expires": "2027-01-01" }]""");

        var result = ExceptionApplier.Apply(diags, exceptions, Root, todayOverride: new DateOnly(2026, 7, 3));

        Assert.Equal(1, result.Suppressed);
        Assert.Empty(result.Kept);
    }

    [Fact]
    public void Symbol_and_id_selectors_never_match_a_cslint_diagnostic()
    {
        // cslint Diagnostic carries no symbol/id, so these selectors can't match — the exception
        // is unused rather than suppressing anything. (Validated in the common section here so the
        // symbol selector is tolerated.)
        var diags = new[] { Diag("src/A.cs", "OP004") };
        using var doc = JsonDocument.Parse(
            """[{ "rule": "OP004", "symbol": "A.Method", "reason": "won't match" }]""");
        var exceptions = DependablyExceptions.Parse(doc.RootElement, "common", DependablyExceptions.Selectors, null);

        var result = ExceptionApplier.Apply(diags, exceptions, Root);

        Assert.Equal(0, result.Suppressed);
        Assert.Single(result.Kept);
    }
}
