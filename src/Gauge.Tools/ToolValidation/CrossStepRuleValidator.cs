using System.Text.Json;
using Gauge.Core.Tooling;
using Gauge.Core.Tracing;
using Gauge.Core.Tracing.Payloads;

namespace Gauge.Tools.ToolValidation;

public static class CrossStepRuleValidator
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static IEnumerable<ValidationIssue> Validate(AgentRunTrace trace, CrossStepRulePolicy policy)
    {
        if (policy.Rules.Count == 0)
            yield break;

        // Extrai tool calls com stepIndex para cruzar "antes/depois"
        var toolSteps = new List<(int StepIndex, Gauge.Core.Tooling.ToolCall Call)>();

        for (var i = 0; i < trace.Steps.Count; i++)
        {
            var step = trace.Steps[i];
            if (!string.Equals(step.Kind, "tool_call", StringComparison.OrdinalIgnoreCase))
                continue;

            ToolCallPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<ToolCallPayload>(step.Payload, JsonOpts);
            }
            catch
            {
                continue; // já é coberto por validação de payload em outro lugar
            }

            if (payload?.Call is not null)
                toolSteps.Add((i, payload.Call));
        }

        foreach (var rule in policy.Rules)
        {
            // para cada target call, valida contra a melhor source anterior (ou qualquer, se permitir)
            for (var t = 0; t < toolSteps.Count; t++)
            {
                var (targetStepIndex, targetCall) = toolSteps[t];
                if (!string.Equals(targetCall.Name, rule.TargetTool, StringComparison.OrdinalIgnoreCase))
                    continue;

                // parse target args
                if (!TryParseJson(targetCall.ArgsJson, out var targetArgsDoc, out var targetArgsErr))
                {
                    yield return new ValidationIssue(
                        "tool.args.json.invalid",
                        $"ArgsJson invalid for target '{rule.TargetTool}': {targetArgsErr}",
                        targetCall.Name,
                        CallIndex: null,
                        JsonPath: $"$.steps[{targetStepIndex}].payload.call.argsJson"
                    );
                    continue;
                }

                if (!JsonPointer.TryResolve(targetArgsDoc.RootElement, rule.TargetArgPointer, out var targetValueEl))
                {
                    yield return new ValidationIssue(
                        "cross.target.pointer.missing",
                        rule.Message ?? $"Missing target arg pointer '{rule.TargetArgPointer}' for tool '{rule.TargetTool}'.",
                        targetCall.Name,
                        CallIndex: null,
                        JsonPath: $"$.steps[{targetStepIndex}].payload.call.args{rule.TargetArgPointer}"
                    );
                    continue;
                }

                var targetValue = NormalizeScalar(targetValueEl);

                // achar source
                var candidates = toolSteps.Where(s =>
                    string.Equals(s.Call.Name, rule.SourceTool, StringComparison.OrdinalIgnoreCase)
                    && (!rule.SourceMustBeBeforeTarget || s.StepIndex < targetStepIndex)
                ).ToList();

                if (candidates.Count == 0)
                {
                    if (rule.FailIfSourceMissing)
                    {
                        yield return new ValidationIssue(
                            "cross.source.missing",
                            rule.Message ?? $"No source tool '{rule.SourceTool}' found for cross-step rule '{rule.Id}'.",
                            targetCall.Name,
                            CallIndex: null,
                            JsonPath: $"$.steps[{targetStepIndex}]"
                        );
                    }
                    continue;
                }

                // pega o source mais próximo antes do target (mais comum)
                var source = candidates.OrderByDescending(c => c.StepIndex).First();
                var sourceStepIndex = source.StepIndex;
                var sourceCall = source.Call;

                if (sourceCall.OutputJson is null)
                {
                    yield return new ValidationIssue(
                        "cross.source.output.missing",
                        rule.Message ?? $"Source tool '{rule.SourceTool}' has no outputJson to validate rule '{rule.Id}'.",
                        targetCall.Name,
                        CallIndex: null,
                        JsonPath: $"$.steps[{sourceStepIndex}].payload.call.outputJson"
                    );
                    continue;
                }

                if (!TryParseJson(sourceCall.OutputJson, out var sourceOutDoc, out var sourceOutErr))
                {
                    yield return new ValidationIssue(
                        "tool.output.json.invalid",
                        $"OutputJson invalid for source '{rule.SourceTool}': {sourceOutErr}",
                        sourceCall.Name,
                        CallIndex: null,
                        JsonPath: $"$.steps[{sourceStepIndex}].payload.call.outputJson"
                    );
                    continue;
                }

                if (!JsonPointer.TryResolve(sourceOutDoc.RootElement, rule.SourceOutputPointer, out var sourceValueEl))
                {
                    yield return new ValidationIssue(
                        "cross.source.pointer.missing",
                        rule.Message ?? $"Missing source output pointer '{rule.SourceOutputPointer}' for tool '{rule.SourceTool}'.",
                        sourceCall.Name,
                        CallIndex: null,
                        JsonPath: $"$.steps[{sourceStepIndex}].payload.call.output{rule.SourceOutputPointer}"
                    );
                    continue;
                }

                var ok = rule.Mode switch
                {
                    CrossCompareMode.Equals => NormalizeScalar(sourceValueEl) == targetValue,
                    CrossCompareMode.InArray => InArray(sourceValueEl, targetValue),
                    _ => false
                };

                if (!ok)
                {
                    yield return new ValidationIssue(
                        "cross.rule.failed",
                        rule.Message ?? $"Cross-step rule '{rule.Id}' failed: target '{rule.TargetTool}{rule.TargetArgPointer}'='{targetValue}' not valid against '{rule.SourceTool}{rule.SourceOutputPointer}'.",
                        targetCall.Name,
                        CallIndex: null,
                        JsonPath: $"$.steps[{targetStepIndex}]"
                    );
                }
            }
        }
    }

    private static bool InArray(JsonElement el, string target)
    {
        if (el.ValueKind != JsonValueKind.Array) return false;
        foreach (var item in el.EnumerateArray())
        {
            if (NormalizeScalar(item) == target) return true;
        }
        return false;
    }

    private static string NormalizeScalar(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => (el.GetString() ?? "").Trim(),
            JsonValueKind.Number => el.GetRawText().Trim(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "",
            _ => el.GetRawText().Trim()
        };
    }

    private static bool TryParseJson(string json, out JsonDocument doc, out string error)
    {
        try
        {
            doc = JsonDocument.Parse(json);
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            doc = default!;
            error = ex.Message;
            return false;
        }
    }
}