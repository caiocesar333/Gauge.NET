namespace Gauge.Core.Tracing;

public sealed record AgentRunTrace(
    string RunId,
    string TestId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    TraceMetadata Metadata,
    IReadOnlyList<TraceStepEnvelope> Steps,
    TraceContext? Context = null
);

public sealed record TraceMetadata(
    string? Model = null,
    double? Temperature = null,
    int? MaxTokens = null,
    string? PromptHash = null,
    string? AgentVersion = null
);