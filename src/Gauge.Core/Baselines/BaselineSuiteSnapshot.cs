namespace Gauge.Core.Baselines;

using Gauge.Core.Tooling;

public sealed record BaselineSuiteSnapshot(
    DateTimeOffset CreatedAt,
    int CaseCount,
    int FailedCount,
    IReadOnlyList<BaselineCaseSnapshot> Cases
);

public sealed record BaselineCaseSnapshot(
    string Key,          // stable identity for matching (default: TestId else Source)
    string Source,       // e.g. relative path under --path, or file name
    string? RunId,
    string? TestId,
    bool IsValid,
    int IssueCount,
    IReadOnlyList<string> IssueCodes,
    IReadOnlyList<ValidationIssue>? Issues = null // optional (can be null to keep file small)
);