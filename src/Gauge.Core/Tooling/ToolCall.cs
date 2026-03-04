namespace Gauge.Core.Tooling;

public sealed record ToolCall(
    string Name,
    string ArgsJson,
    string? OutputJson = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? CompletedAt = null,
    string? Status = null 
);