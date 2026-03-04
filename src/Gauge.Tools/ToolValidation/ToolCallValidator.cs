using Gauge.Core.Tooling;
using Gauge.Core.Tracing;
using Gauge.Core.Tracing.Payloads;
using Json.Schema;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gauge.Tools.ToolValidation;

public sealed class ToolCallValidator
{
    private readonly Dictionary<string, ToolContract> _contractsByName;
    private readonly Dictionary<string, JsonSchema> _argsSchemaByTool;
    private readonly Dictionary<string, JsonSchema?> _outputSchemaByTool;

    public ToolCallValidator(IEnumerable<ToolContract> contracts)
    {
        _contractsByName = contracts.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        _argsSchemaByTool = new(StringComparer.OrdinalIgnoreCase);
        _outputSchemaByTool = new(StringComparer.OrdinalIgnoreCase);

        foreach (var c in _contractsByName.Values)
        {
            _argsSchemaByTool[c.Name] = JsonSchemaLoader.Parse(c.ArgsJsonSchema);
            _outputSchemaByTool[c.Name] = c.OutputJsonSchema is null ? null : JsonSchemaLoader.Parse(c.OutputJsonSchema);
        }
    }

    public ValidationReport Validate(
        IReadOnlyList<ToolCall> calls,
        ToolValidationPolicy? policy = null)
    {
        policy ??= new ToolValidationPolicy();
        var issues = new List<ValidationIssue>();

        // Policy: total calls limit
        if (policy.MaxTotalCalls is int maxTotal && calls.Count > maxTotal)
        {
            issues.Add(new ValidationIssue(
                Code: "policy.max_total_calls.exceeded",
                Message: $"Total tool calls ({calls.Count}) exceeded MaxTotalCalls ({maxTotal})."));
        }

        // Counts
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < calls.Count; i++)
        {
            var call = calls[i];
            counts[call.Name] = counts.TryGetValue(call.Name, out var cur) ? cur + 1 : 1;

            // Policy: allow/deny
            if (policy.AllowList is { Count: > 0 } allow && !allow.Contains(call.Name))
            {
                issues.Add(new ValidationIssue(
                    Code: "policy.allowlist.violation",
                    Message: $"Tool '{call.Name}' is not in allow-list.",
                    ToolName: call.Name,
                    CallIndex: i));
            }

            if (policy.DenyList is { Count: > 0 } deny && deny.Contains(call.Name))
            {
                issues.Add(new ValidationIssue(
                    Code: "policy.denylist.violation",
                    Message: $"Tool '{call.Name}' is in deny-list.",
                    ToolName: call.Name,
                    CallIndex: i));
            }

            // Contract known?
            var hasContract = _contractsByName.ContainsKey(call.Name);
            if (policy.RequireKnownToolContracts && !hasContract)
            {
                issues.Add(new ValidationIssue(
                    Code: "tool.contract.unknown",
                    Message: $"No contract registered for tool '{call.Name}'.",
                    ToolName: call.Name,
                    CallIndex: i));
                continue;
            }

            // Args JSON parse
            if (!TryParseJsonNode(call.ArgsJson, out var argsNode, out var argsErr))
            {
                issues.Add(new ValidationIssue(
                    Code: "tool.args.json.invalid",
                    Message: $"ArgsJson is not valid JSON: {argsErr}",
                    ToolName: call.Name,
                    CallIndex: i));
            }
            else
            {
                // Validate args schema
                if (_argsSchemaByTool.TryGetValue(call.Name, out var schema))
                {
                    var eval = schema.Evaluate(argsNode);
                    if (!eval.IsValid)
                    {
                        issues.Add(new ValidationIssue(
                            Code: "tool.args.schema.invalid",
                            Message: $"Args do tool '{call.Name}' falharam no schema.",
                            ToolName: call.Name,
                            CallIndex: i));
                    }
                }
            }

            // Output schema (se existir e contrato tiver schema)
            if (call.OutputJson is not null &&
                _outputSchemaByTool.TryGetValue(call.Name, out var outSchema) &&
                outSchema is not null)
            {
                if (!TryParseJsonNode(call.OutputJson, out var outNode, out var outErr))
                {
                    issues.Add(new ValidationIssue(
                        Code: "tool.output.json.invalid",
                        Message: $"OutputJson is not valid JSON: {outErr}",
                        ToolName: call.Name,
                        CallIndex: i));
                }
                else
                {
                    var eval = outSchema.Evaluate(outNode);
                    if (!eval.IsValid)
                    {
                        issues.Add(new ValidationIssue(
                            Code: "tool.output.schema.invalid",
                            Message: $"Output do tool '{call.Name}' falhou no schema.",
                            ToolName: call.Name,
                            CallIndex: i));
                    }
                }
            }

            // Argument rules (além do schema)
            if (policy.ArgumentRules is not null)
            {
                foreach (var issue in ArgumentRuleValidator.ValidateRules(call, i, policy.ArgumentRules))
                    issues.Add(issue);
            }
        }

        // Policy: must-call
        if (policy.MustCall is { Count: > 0 } must)
        {
            foreach (var name in must)
            {
                if (!counts.TryGetValue(name, out var n) || n <= 0)
                {
                    issues.Add(new ValidationIssue(
                        Code: "policy.must_call.missing",
                        Message: $"Expected tool '{name}' to be called at least once.",
                        ToolName: name));
                }
            }
        }

        // Policy: max calls per tool
        if (policy.MaxCallsPerTool is { Count: > 0 } maxPer)
        {
            foreach (var kv in maxPer)
            {
                var tool = kv.Key;
                var max = kv.Value;
                var actual = counts.TryGetValue(tool, out var n) ? n : 0;

                if (actual > max)
                {
                    issues.Add(new ValidationIssue(
                        Code: "policy.max_calls_per_tool.exceeded",
                        Message: $"Tool '{tool}' called {actual} times; max allowed is {max}.",
                        ToolName: tool));
                }
            }
        }

        // Policy: ordering (A before B)
        if (policy.MustOccurBefore is { Count: > 0 } orderRules)
        {
            foreach (var (before, after) in orderRules)
            {
                var idxBefore = IndexOfTool(calls, before);
                var idxAfter = IndexOfTool(calls, after);

                if (idxBefore is null || idxAfter is null) continue;

                if (idxBefore.Value > idxAfter.Value)
                {
                    issues.Add(new ValidationIssue(
                        Code: "policy.order.violation",
                        Message: $"Expected '{before}' to occur before '{after}', but it occurred after.",
                        ToolName: after));
                }
            }
        }

        return issues.Count == 0 ? ValidationReport.Valid() : ValidationReport.Invalid(issues);
    }

    private static int? IndexOfTool(IReadOnlyList<ToolCall> calls, string toolName)
    {
        for (var i = 0; i < calls.Count; i++)
            if (string.Equals(calls[i].Name, toolName, StringComparison.OrdinalIgnoreCase))
                return i;

        return null;
    }

    private static bool TryParseJsonNode(string json, out JsonNode node, out string error)
    {
        try
        {
            node = JsonNode.Parse(json) ?? new JsonObject();
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            node = new JsonObject();
            error = ex.Message;
            return false;
        }
    }

    public ValidationReport Validate(AgentRunTrace trace, ToolValidationPolicy? policy = null)
    {
        var issues = new List<ValidationIssue>();
        var calls = new List<ToolCall>();

        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        for (var i = 0; i < trace.Steps.Count; i++)
        {
            var step = trace.Steps[i];

            if (!string.Equals(step.Kind, "tool_call", StringComparison.OrdinalIgnoreCase))
                continue;

            ToolCallPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<ToolCallPayload>(step.Payload, opts);
            }
            catch (Exception ex)
            {
                issues.Add(new ValidationIssue(
                    Code: "trace.step.payload.invalid",
                    Message: $"Failed to deserialize tool_call payload at step#{i}: {ex.Message}",
                    ToolName: null,
                    CallIndex: null,
                    JsonPath: "$.steps[" + i + "].payload"
                ));
                continue;
            }

            if (payload?.Call is null)
            {
                issues.Add(new ValidationIssue(
                    "trace.step.payload.missing_call",
                    $"tool_call payload missing 'call' at step#{i}",
                    JsonPath: "$.steps[" + i + "].payload.call"
                ));
                continue;
            }

            calls.Add(payload.Call);
        }

        // Se já houve erros de payload, retorna junto com os erros de validação das calls:
        var report = Validate(calls, policy);

        if (issues.Count == 0) return report;

        var combined = report.Issues.Concat(issues).ToList();
        if (policy?.Flow is not null)
        {
            var flowReport = new ToolFlowValidator(policy.Flow).Validate(trace);
            if (!flowReport.IsValid)
                combined = combined.Concat(flowReport.Issues).ToList();
        }

        if (policy?.CrossStepRules is not null)
        {
            var crossIssues = CrossStepRuleValidator.Validate(trace, policy.CrossStepRules);
            combined = combined.Concat(crossIssues).ToList();
        }

        if (policy?.Thresholds is not null)
        {
            var thrIssues = LlmThresholdValidator.Validate(trace, policy.Thresholds);
            combined = combined.Concat(thrIssues).ToList();
        }


        return ValidationReport.Invalid(combined);
    }
}