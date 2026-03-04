using System.Text.Json;
using FluentAssertions;
using Gauge.Core.Tooling;
using Gauge.Core.Tracing;
using Gauge.Core.Tracing.Payloads;
using Gauge.Tools.ToolValidation;

namespace Gauge.Tools.Tests;

public sealed class CrossStepRuleValidatorTests
{
    [Fact]
    public void Passes_when_target_id_is_in_source_ids()
    {
        // search.outputJson => { "ids": ["A1","B2"] }
        // get_details.args   => { "id": "A1" }  (válido)

        var trace = CreateTraceBuilder()
            .Tool("search", argsJson: """{ "query":"x" }""", outputJson: """{ "ids": ["A1","B2"] }""")
            .Tool("get_details", argsJson: """{ "id":"A1" }""")
            .Build();

        var policy = new CrossStepRulePolicy
        {
            Rules = new()
            {
                new CrossStepRule
                {
                    Id = "details.id_from_search",
                    TargetTool = "get_details",
                    TargetArgPointer = "/id",
                    SourceTool = "search",
                    SourceOutputPointer = "/ids",
                    Mode = CrossCompareMode.InArray,
                    Message = "get_details.id must be returned by search"
                }
            }
        };

        var issues = CrossStepRuleValidator.Validate(trace, policy).ToList();
        issues.Should().BeEmpty();
    }

    [Fact]
    public void Fails_when_target_id_is_not_in_source_ids()
    {
        // get_details.id = "Z9" (não existe no search.ids)
        var trace = CreateTraceBuilder()
            .Tool("search", argsJson: """{ "query":"x" }""", outputJson: """{ "ids": ["A1","B2"] }""")
            .Tool("get_details", argsJson: """{ "id":"Z9" }""")
            .Build();

        var policy = new CrossStepRulePolicy
        {
            Rules = new()
            {
                new CrossStepRule
                {
                    Id = "details.id_from_search",
                    TargetTool = "get_details",
                    TargetArgPointer = "/id",
                    SourceTool = "search",
                    SourceOutputPointer = "/ids",
                    Mode = CrossCompareMode.InArray
                }
            }
        };

        var issues = CrossStepRuleValidator.Validate(trace, policy).ToList();
        issues.Should().Contain(i => i.Code == "cross.rule.failed");
    }

    [Fact]
    public void Fails_when_source_tool_missing_and_configured_to_fail()
    {
        // Não tem "search" no trace
        var trace = CreateTraceBuilder()
            .Tool("get_details", argsJson: """{ "id":"A1" }""")
            .Build();

        var policy = new CrossStepRulePolicy
        {
            Rules = new()
            {
                new CrossStepRule
                {
                    Id = "details.id_from_search",
                    TargetTool = "get_details",
                    TargetArgPointer = "/id",
                    SourceTool = "search",
                    SourceOutputPointer = "/ids",
                    Mode = CrossCompareMode.InArray,
                    FailIfSourceMissing = true
                }
            }
        };

        var issues = CrossStepRuleValidator.Validate(trace, policy).ToList();
        issues.Should().Contain(i => i.Code == "cross.source.missing");
    }

    [Fact]
    public void Can_ignore_missing_source_when_configured()
    {
        var trace = CreateTraceBuilder()
            .Tool("get_details", argsJson: """{ "id":"A1" }""")
            .Build();

        var policy = new CrossStepRulePolicy
        {
            Rules = new()
            {
                new CrossStepRule
                {
                    Id = "details.id_from_search",
                    TargetTool = "get_details",
                    TargetArgPointer = "/id",
                    SourceTool = "search",
                    SourceOutputPointer = "/ids",
                    Mode = CrossCompareMode.InArray,
                    FailIfSourceMissing = false
                }
            }
        };

        var issues = CrossStepRuleValidator.Validate(trace, policy).ToList();
        issues.Should().BeEmpty();
    }

    [Fact]
    public void Can_require_source_before_target()
    {
        // search vem depois do get_details => deve falhar (SourceMustBeBeforeTarget = true)
        var trace = CreateTraceBuilder()
            .Tool("get_details", argsJson: """{ "id":"A1" }""")
            .Tool("search", argsJson: """{ "query":"x" }""", outputJson: """{ "ids": ["A1"] }""")
            .Build();

        var policy = new CrossStepRulePolicy
        {
            Rules = new()
            {
                new CrossStepRule
                {
                    Id = "details.id_from_search",
                    TargetTool = "get_details",
                    TargetArgPointer = "/id",
                    SourceTool = "search",
                    SourceOutputPointer = "/ids",
                    Mode = CrossCompareMode.InArray,
                    SourceMustBeBeforeTarget = true,
                    FailIfSourceMissing = true
                }
            }
        };

        var issues = CrossStepRuleValidator.Validate(trace, policy).ToList();
        issues.Should().Contain(i => i.Code == "cross.source.missing");
    }

    private static TraceBuilder CreateTraceBuilder() => new();

    private sealed class TraceBuilder
    {
        private readonly List<TraceStepEnvelope> _steps = new();

        public TraceBuilder Tool(string name, string argsJson = "{}", string? outputJson = null)
        {
            var call = new ToolCall(
                Name: name,
                ArgsJson: argsJson,
                OutputJson: outputJson,
                StartedAt: DateTimeOffset.Now,
                CompletedAt: DateTimeOffset.Now.AddMilliseconds(10),
                Status: "ok"
            );

            var payload = JsonSerializer.SerializeToElement(new ToolCallPayload(call));

            _steps.Add(new TraceStepEnvelope(
                Kind: "tool_call",
                At: DateTimeOffset.Now,
                Payload: payload
            ));

            return this;
        }

        public AgentRunTrace Build()
        {
            return new AgentRunTrace(
                RunId: "run-test",
                TestId: "tc-test",
                StartedAt: DateTimeOffset.Now,
                CompletedAt: DateTimeOffset.Now,
                Metadata: new TraceMetadata(),
                Steps: _steps
            );
        }
    }
}