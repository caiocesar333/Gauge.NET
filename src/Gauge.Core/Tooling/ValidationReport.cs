namespace Gauge.Core.Tooling;

public sealed record ValidationIssue(
    string Code,
    string Message,
    string? ToolName = null,
    int? CallIndex = null,
    string? JsonPath = null
);

public sealed record ValidationReport(
    bool IsValid,
    IReadOnlyList<ValidationIssue> Issues
)
{
    public static ValidationReport Valid() => new(true, Array.Empty<ValidationIssue>());

    public static ValidationReport Invalid(IEnumerable<ValidationIssue> issues)
        => new(false, issues.ToArray());
}