using System.Text.Json;
using FluentAssertions;
using Gauge.Core.Tooling;
using Gauge.Core.Tracing;
using Gauge.Core.Tracing.Payloads;
using Gauge.Tools.ToolValidation;

namespace Gauge.Tools.Tests;

public class ToolFlowValidatorTests
{
    [Fact]
    public void Flow_passes_for_search_then_answer()
    {
        var trace = MakeTrace(new[] { "search", "answer" });

        var policy = new ToolValidationPolicy
        {
            Flow = new ToolFlowPolicy
            {
                StartState = "start",
                AcceptStates = new() { "answered" },
                Strict = true,
                ToolToAction = new()
                {
                    ["search"] = "searched",
                    ["answer"] = "answered"
                },
                Transitions = new()
                {
                    new("start","searched","searched"),
                    new("searched","answered","answered")
                }
            }
        };

        var flowReport = new ToolFlowValidator(policy.Flow!).Validate(trace);
        flowReport.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Flow_fails_for_answer_without_search()
    {
        var trace = MakeTrace(new[] { "answer" });

        var flow = new ToolFlowPolicy
        {
            StartState = "start",
            AcceptStates = new() { "answered" },
            Strict = true,
            ToolToAction = new() { ["answer"] = "answered" },
            Transitions = new()
            {
                // não existe start + answered => invalid
            }
        };

        var report = new ToolFlowValidator(flow).Validate(trace);
        report.IsValid.Should().BeFalse();
        report.Issues.Should().Contain(i => i.Code == "flow.transition.invalid");
    }

    private static AgentRunTrace MakeTrace(string[] tools)
    {
        var steps = tools.Select(t =>
            new TraceStepEnvelope(
                Kind: "tool_call",
                At: DateTimeOffset.Now,
                Payload: JsonSerializer.SerializeToElement(
                    new ToolCallPayload(new Gauge.Core.Tooling.ToolCall(
                        Name: t,
                        ArgsJson: "{}"
                    ))
                )
            )
        ).ToList();

        return new AgentRunTrace(
            RunId: "run-x",
            TestId: "tc-x",
            StartedAt: DateTimeOffset.Now,
            CompletedAt: DateTimeOffset.Now,
            Metadata: new TraceMetadata(),
            Steps: steps
        );
    }
}