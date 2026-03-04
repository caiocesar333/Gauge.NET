namespace Gauge.Core.Tracing.Payloads;

public sealed record LlmCallPayload(
    string Prompt,
    string Response,
    string? Model = null,
    double? Temperature = null,
    int? PromptTokens = null,
    int? CompletionTokens = null,
    int? TotalTokens = null,
    int? LatencyMs = null
);