using System.Text.Json;
using Gauge.Core.Tooling;
using Gauge.Core.Tracing;
using Gauge.Core.Tracing.Payloads;

namespace Gauge.Tools.ToolValidation;

internal static class LlmThresholdValidator
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static IEnumerable<ValidationIssue> Validate(AgentRunTrace trace, ThresholdPolicy thresholds)
    {
        var llmSteps = new List<(int StepIndex, LlmCallPayload Payload)>();

        for (var i = 0; i < trace.Steps.Count; i++)
        {
            var step = trace.Steps[i];
            if (!string.Equals(step.Kind, "llm_call", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var payload = JsonSerializer.Deserialize<LlmCallPayload>(step.Payload, JsonOpts);
                if (payload is not null)
                    llmSteps.Add((i, payload));
            }
            catch
            {
                var yield = new ValidationIssue(
                   Code: "threshold.llm.payload.invalid",
                   Message: $"Failed to deserialize llm_call payload at step#{i}.",
                   ToolName: null,
                   CallIndex: null,
                   JsonPath: $"$.steps[{i}].payload"
               );
                Console.WriteLine($"Caught Exception:{yield.ToString()} ");
            }
        }

        if (thresholds.MaxLlmCalls is int maxCalls && llmSteps.Count > maxCalls)
        {
            yield return new ValidationIssue(
                "threshold.llm_calls.exceeded",
                $"LLM calls ({llmSteps.Count}) exceeded MaxLlmCalls ({maxCalls})."
            );
        }

        var totalTokens = llmSteps.Sum(s => s.Payload.TotalTokens ?? 0);
        var promptTokens = llmSteps.Sum(s => s.Payload.PromptTokens ?? 0);
        var completionTokens = llmSteps.Sum(s => s.Payload.CompletionTokens ?? 0);
        var totalLatency = llmSteps.Sum(s => s.Payload.LatencyMs ?? 0);

        if (thresholds.MaxTotalTokens is int mtt && totalTokens > mtt)
            yield return new ValidationIssue("threshold.total_tokens.exceeded",
                $"TotalTokens ({totalTokens}) exceeded MaxTotalTokens ({mtt}).");

        if (thresholds.MaxPromptTokens is int mpt && promptTokens > mpt)
            yield return new ValidationIssue("threshold.prompt_tokens.exceeded",
                $"PromptTokens ({promptTokens}) exceeded MaxPromptTokens ({mpt}).");

        if (thresholds.MaxCompletionTokens is int mct && completionTokens > mct)
            yield return new ValidationIssue("threshold.completion_tokens.exceeded",
                $"CompletionTokens ({completionTokens}) exceeded MaxCompletionTokens ({mct}).");

        if (thresholds.MaxTotalLatencyMs is int mtl && totalLatency > mtl)
            yield return new ValidationIssue("threshold.total_latency.exceeded",
                $"TotalLatencyMs ({totalLatency}) exceeded MaxTotalLatencyMs ({mtl}).");

        // por-call thresholds
        foreach (var (stepIndex, payload) in llmSteps)
        {
            if (thresholds.MaxTokensPerCall is int mtpc)
            {
                var t = payload.TotalTokens ?? 0;
                if (t > mtpc)
                    yield return new ValidationIssue(
                        "threshold.tokens_per_call.exceeded",
                        $"TotalTokens per call ({t}) exceeded MaxTokensPerCall ({mtpc}).",
                        JsonPath: $"$.steps[{stepIndex}]"
                    );
            }

            if (thresholds.MaxLatencyMsPerCall is int mlpc)
            {
                var l = payload.LatencyMs ?? 0;
                if (l > mlpc)
                    yield return new ValidationIssue(
                        "threshold.latency_per_call.exceeded",
                        $"LatencyMs per call ({l}) exceeded MaxLatencyMsPerCall ({mlpc}).",
                        JsonPath: $"$.steps[{stepIndex}]"
                    );
            }
        }
    }
}