namespace Gauge.Core.Tracing;

public sealed record TraceContext(
    string? UserInput = null,
    Dictionary<string, string>? Variables = null
);