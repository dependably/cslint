// Fixture: IDE0005 — unnecessary using directive.
// Enable with: dotnet_diagnostic.IDE0005.severity = warning
// Detected by: cslint --deep --project <csproj>
//
// System.Text is imported but never used in this file.

using System;         // used: string is in System (required without implicit usings)
using System.Text;    // NOT used → IDE0005

class IDE0005_UnusedUsingFixture
{
    // Uses System.String (via 'string' keyword alias and String type) but not StringBuilder.
    public string? Message { get; set; }
}
