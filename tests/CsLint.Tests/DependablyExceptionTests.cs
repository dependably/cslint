using System.Text.Json;
using CsLint.Config;
using Xunit;

namespace CsLint.Tests;

public class DependablyExceptionTests
{
    private static readonly string[] CsLintOwnSelectors = DependablyExceptions.CsLintSelectors;

    private static IReadOnlyList<DependablyException> Parse(string json, string source = "own",
        IReadOnlyCollection<string>? applicable = null, IReadOnlyCollection<string>? knownRules = null)
    {
        using var doc = JsonDocument.Parse(json);
        return DependablyExceptions.Parse(doc.RootElement, source, applicable ?? CsLintOwnSelectors, knownRules);
    }

    private static DependablyException One(string entryJson, string source = "common",
        IReadOnlyCollection<string>? applicable = null)
        => Parse($"[{entryJson}]", source, applicable ?? DependablyExceptions.Selectors)[0];

    // ---- glob ----

    [Theory]
    [InlineData("src/**", "src/a/b.cs", true)]
    [InlineData("src/**", "src", true)]
    [InlineData("src/**", "lib/a.cs", false)]
    [InlineData("src/*.cs", "src/a.cs", true)]
    [InlineData("src/*.cs", "src/a/b.cs", false)]
    [InlineData("a?.cs", "ab.cs", true)]
    [InlineData("a?.cs", "abc.cs", false)]
    public void MatchGlob_portable_subset(string glob, string value, bool expected)
        => Assert.Equal(expected, DependablyExceptions.MatchGlob(glob, value));

    [Fact]
    public void MatchGlob_normalizes_backslashes()
        => Assert.True(DependablyExceptions.MatchGlob("src/**", "src\\a\\b.cs"));

    // ---- parse validation ----

    [Fact]
    public void Parse_accepts_wellformed_entry_with_path()
    {
        var ex = Parse("""[{ "rule": "SAST001", "path": "src/**", "reason": "test file" }]""");
        Assert.Single(ex);
        Assert.Equal("SAST001", ex[0].Rule);
        Assert.Equal("src/**", ex[0].Path);
    }

    [Fact]
    public void Parse_accepts_wellformed_entry_with_symbol()
    {
        var ex = Parse("""[{ "rule": "OP004", "symbol": "MyClass.Method", "reason": "build tool" }]""");
        Assert.Single(ex);
        Assert.Equal("OP004", ex[0].Rule);
        Assert.Equal("MyClass.Method", ex[0].Symbol);
    }

    [Fact]
    public void Parse_accepts_wellformed_entry_with_id()
    {
        var ex = Parse("""[{ "rule": "OP004", "id": "SAST004-001", "reason": "grandfathered" }]""");
        Assert.Single(ex);
        Assert.Equal("SAST004-001", ex[0].Id);
    }

    [Fact]
    public void Parse_undefined_is_empty()
    {
        using var doc = JsonDocument.Parse("null");
        Assert.Empty(DependablyExceptions.Parse(doc.RootElement, "own", CsLintOwnSelectors, null));
    }

    [Theory]
    [InlineData("""[{ "path": "src/**", "reason": "y" }]""", "EXCEPTION_MISSING_RULE")]
    [InlineData("""[{ "rule": "OP004", "path": "src/**" }]""", "EXCEPTION_MISSING_REASON")]
    [InlineData("""[{ "rule": "OP004", "reason": "y" }]""", "EXCEPTION_NO_SELECTOR")]
    [InlineData("""[{ "rule": "OP004", "path": "src/**", "reason": "y", "expires": "31-12-2026" }]""", "EXCEPTION_BAD_EXPIRES")]
    public void Parse_own_section_rejects(string json, string expectedCode)
    {
        var ex = Assert.Throws<DependablyConfigException>(() => Parse(json, "own", CsLintOwnSelectors));
        Assert.Equal(expectedCode, ex.Code);
    }

    [Fact]
    public void Parse_own_section_rejects_package_selector_as_inapplicable()
    {
        // package is not applicable to cslint (it's file+symbol based, not package-based).
        var ex = Assert.Throws<DependablyConfigException>(
            () => Parse("""[{ "rule": "OP004", "package": "SomeLib", "reason": "y" }]""", "own", CsLintOwnSelectors));
        Assert.Equal("EXCEPTION_BAD_SELECTOR", ex.Code);
    }

    [Fact]
    public void Parse_common_tolerates_inapplicable_selector()
    {
        // In common, package selector is tolerated even though cslint doesn't use it.
        var ex = Parse("""[{ "rule": "OP004", "package": "SomeLib", "reason": "y" }]""", "common", CsLintOwnSelectors);
        Assert.Single(ex);
    }

    [Fact]
    public void Parse_own_rejects_unknown_rule_when_known_given()
    {
        var ex = Assert.Throws<DependablyConfigException>(
            () => Parse("""[{ "rule": "no-such", "path": "src/**", "reason": "y" }]""", "own", CsLintOwnSelectors, DependablyExceptions.KnownRules));
        Assert.Equal("UNKNOWN_RULE", ex.Code);
    }

    // ---- matching ----

    [Fact]
    public void Matches_path_glob()
    {
        var ex = One("""{ "rule": "SAST001", "path": "src/**", "reason": "x" }""");
        Assert.True(DependablyExceptions.Matches(ex, new ExceptionTarget("SAST001", Path: "src/Foo/Bar.cs")));
        Assert.False(DependablyExceptions.Matches(ex, new ExceptionTarget("SAST001", Path: "tests/Bar.cs")));
    }

    [Fact]
    public void Matches_symbol_type_matches_member()
    {
        var ex = One("""{ "rule": "OP004", "symbol": "Parser", "reason": "x" }""");
        Assert.True(DependablyExceptions.Matches(ex, new ExceptionTarget("OP004", Symbol: "Parser.Parse")));
        Assert.False(DependablyExceptions.Matches(ex, new ExceptionTarget("OP004", Symbol: "Other.Parse")));
    }

    [Fact]
    public void Matches_symbol_exact()
    {
        var ex = One("""{ "rule": "OP004", "symbol": "Parser.Parse", "reason": "x" }""");
        Assert.True(DependablyExceptions.Matches(ex, new ExceptionTarget("OP004", Symbol: "Parser.Parse")));
        Assert.False(DependablyExceptions.Matches(ex, new ExceptionTarget("OP004", Symbol: "Parser.Other")));
    }

    [Fact]
    public void Matches_selectors_are_and()
    {
        var ex = One("""{ "rule": "OP004", "path": "src/Parser/**", "symbol": "Parser.Parse", "reason": "x" }""");
        Assert.True(DependablyExceptions.Matches(ex, new ExceptionTarget("OP004", Path: "src/Parser/P.cs", Symbol: "Parser.Parse")));
        Assert.False(DependablyExceptions.Matches(ex, new ExceptionTarget("OP004", Path: "src/Parser/P.cs", Symbol: "Parser.Other")));
    }

    [Fact]
    public void Matches_id_exact()
    {
        var ex = One("""{ "rule": "OP004", "id": "SAST004-001", "reason": "x" }""");
        Assert.True(DependablyExceptions.Matches(ex, new ExceptionTarget("OP004", Id: "SAST004-001")));
        Assert.False(DependablyExceptions.Matches(ex, new ExceptionTarget("OP004", Id: "SAST004-002")));
    }

    [Fact]
    public void Matches_package_case_insensitive()
    {
        // package is used in common exceptions (cross-tool). Matching is case-insensitive.
        var ex = One("""{ "rule": "OP004", "package": "SomeLib", "reason": "x" }""");
        Assert.True(DependablyExceptions.Matches(ex, new ExceptionTarget("OP004", Package: "somelib")));
    }

    // ---- expiry ----

    [Fact]
    public void IsExpired_respects_date()
    {
        var ex = One("""{ "rule": "OP004", "path": "src/**", "reason": "x", "expires": "2026-01-01" }""");
        Assert.True(DependablyExceptions.IsExpired(ex, new DateOnly(2026, 7, 3)));
        Assert.False(DependablyExceptions.IsExpired(ex, new DateOnly(2025, 12, 1)));
    }

    [Fact]
    public void IsExpired_false_without_expires()
    {
        var ex = One("""{ "rule": "OP004", "path": "src/**", "reason": "x" }""");
        Assert.False(DependablyExceptions.IsExpired(ex, new DateOnly(2999, 1, 1)));
    }
}
