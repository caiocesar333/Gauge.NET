using System.Security;
using System.Text;
using Gauge.Core.Tooling;

namespace Gauge.Cli;

internal static class JUnitWriter
{
    internal sealed record TestcaseResult(
        string Name,
        string ClassName,
        TimeSpan Duration,
        ValidationReport Report
    );

    public static string WriteSuite(string suiteName, IReadOnlyList<TestcaseResult> cases)
    {
        var tests = cases.Count;
        var failures = cases.Count(c => !c.Report.IsValid);
        var totalTime = cases.Sum(c => c.Duration.TotalSeconds);

        var sb = new StringBuilder();
        sb.AppendLine(@"<?xml version=""1.0"" encoding=""utf-8""?>");
        sb.AppendLine($@"<testsuite name=""{Xml(suiteName)}"" tests=""{tests}"" failures=""{failures}"" time=""{totalTime:0.###}"">");

        foreach (var c in cases)
        {
            sb.AppendLine($@"  <testcase classname=""{Xml(c.ClassName)}"" name=""{Xml(c.Name)}"" time=""{c.Duration.TotalSeconds:0.###}"">");

            if (!c.Report.IsValid)
            {
                var msg = $"Gauge validation failed with {c.Report.Issues.Count} issue(s).";
                var body = string.Join("\n", c.Report.Issues.Select(FormatIssue));
                sb.AppendLine($@"    <failure message=""{Xml(msg)}"">{Xml(body)}</failure>");
            }

            sb.AppendLine(@"  </testcase>");
        }

        sb.AppendLine(@"</testsuite>");
        return sb.ToString();
    }

    private static string FormatIssue(ValidationIssue i)
    {
        var at = i.CallIndex is null ? "" : $" call#{i.CallIndex}";
        var tool = i.ToolName is null ? "" : $" tool={i.ToolName}";
        var path = i.JsonPath is null ? "" : $" path={i.JsonPath}";
        return $"{i.Code}:{at}{tool}{path} - {i.Message}";
    }

    private static string Xml(string s) => SecurityElement.Escape(s) ?? "";
}