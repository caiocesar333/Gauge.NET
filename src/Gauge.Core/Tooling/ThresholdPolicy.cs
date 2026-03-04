namespace Gauge.Core.Tooling;

/// <summary>
/// Limites operacionais (custo/tempo) com base em steps llm_call.
/// </summary>
public sealed record ThresholdPolicy
{
    // Totais por execução
    public int? MaxTotalTokens { get; init; }
    public int? MaxPromptTokens { get; init; }
    public int? MaxCompletionTokens { get; init; }
    public int? MaxTotalLatencyMs { get; init; }
    public int? MaxLlmCalls { get; init; }

    // Limites por chamada
    public int? MaxTokensPerCall { get; init; }
    public int? MaxLatencyMsPerCall { get; init; }
}