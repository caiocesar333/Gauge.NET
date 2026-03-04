using Gauge.Core.Tooling;
using Gauge.Core.Tracing;
using Gauge.Core.Tracing.Payloads;
using System.Text.Json;

namespace Gauge.Tools.ToolValidation;

public sealed class ToolFlowValidator
{
    private readonly ToolFlowPolicy _flow;
    private readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    public ToolFlowValidator(ToolFlowPolicy flow)
    {
        _flow = flow;
    }

    public ValidationReport Validate(AgentRunTrace trace)
    {
        var issues = new List<ValidationIssue>();

        var transitions = _flow.Transitions.ToDictionary(
            t => (From: t.From, Action: t.Action),
            t => t.To,
            new FlowKeyComparer());

        var state = _flow.StartState;

        for (var stepIndex = 0; stepIndex < trace.Steps.Count; stepIndex++)
        {
            var step = trace.Steps[stepIndex];
            if (!string.Equals(step.Kind, "tool_call", StringComparison.OrdinalIgnoreCase))
                continue;

            ToolCallPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<ToolCallPayload>(step.Payload, _jsonOpts);
            }
            catch (Exception ex)
            {
                issues.Add(new ValidationIssue(
                    "trace.step.payload.invalid",
                    $"Failed to deserialize tool_call payload at step#{stepIndex}: {ex.Message}",
                    ToolName: null,
                    CallIndex: null,
                    JsonPath: $"$.steps[{stepIndex}].payload"
                ));
                continue;
            }

            var call = payload?.Call;
            if (call is null)
            {
                issues.Add(new ValidationIssue(
                    "trace.step.payload.missing_call",
                    $"tool_call payload missing 'call' at step#{stepIndex}",
                    ToolName: null,
                    CallIndex: null,
                    JsonPath: $"$.steps[{stepIndex}].payload.call"
                ));
                continue;
            }

            var action = ResolveAction(call.Name);

            if (!transitions.TryGetValue((state, action), out var nextState))
            {
                if (_flow.Strict)
                {
                    issues.Add(new ValidationIssue(
                        "flow.transition.invalid",
                        $"Invalid transition: state='{state}' + action='{action}' (tool='{call.Name}') at step#{stepIndex}.",
                        ToolName: call.Name,
                        CallIndex: null,
                        JsonPath: $"$.steps[{stepIndex}]"
                    ));
                }

                // Strict = false => não altera estado
                continue;
            }

            state = nextState;
        }

        // Check accept states
        if (_flow.AcceptStates is { Count: > 0 } acc && !acc.Contains(state))
        {
            issues.Add(new ValidationIssue(
                "flow.accept_state.not_reached",
                $"Final state '{state}' is not an accepted state. Accepted: [{string.Join(", ", acc)}]."
            ));
        }

        return issues.Count == 0 ? ValidationReport.Valid() : ValidationReport.Invalid(issues);
    }

    private string ResolveAction(string toolName)
    {
        if (_flow.ToolToAction is not null && _flow.ToolToAction.TryGetValue(toolName, out var mapped))
            return mapped;

        return toolName; // default: action = toolName
    }

    private sealed class FlowKeyComparer : IEqualityComparer<(string From, string Action)>
    {
        public bool Equals((string From, string Action) x, (string From, string Action) y)
            => string.Equals(x.From, y.From, StringComparison.OrdinalIgnoreCase)
               && string.Equals(x.Action, y.Action, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string From, string Action) obj)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.From),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Action));
    }
}