using Gauge.Core.Tooling;

namespace Gauge.Core.Baselines;

public sealed record BaselineSnapshot(
    string Framework = "Gauge.NET",
    string Version = "0.1",
    DateTimeOffset CreatedAt = default,
    bool IsValid = true,
    int IssueCount = 0,
    IReadOnlyList<string>? IssueCodes = null,
    IReadOnlyList<ValidationIssue>? Issues = null
);