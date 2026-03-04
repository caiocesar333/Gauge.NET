using FluentAssertions;
using Gauge.Core.Tooling;
using Gauge.Tools.ToolValidation;

namespace Gauge.Tools.Tests;

public class ToolCallValidatorTests
{
    [Fact]
    public void Validates_args_schema_ok()
    {
        var contracts = new[]
        {
            new ToolContract(
                Name: "search",
                Version: "1.0.0",
                ArgsJsonSchema: """
                {
                  "$schema": "https://json-schema.org/draft/2020-12/schema",
                  "type": "object",
                  "properties": {
                    "query": { "type": "string", "minLength": 1 },
                    "topK": { "type": "integer", "minimum": 1, "maximum": 20 }
                  },
                  "required": ["query"],
                  "additionalProperties": false
                }
                """
            )
        };

        var calls = new[]
        {
            new ToolCall("search", """{ "query": "c# json schema", "topK": 5 }""")
        };

        var validator = new ToolCallValidator(contracts);
        var report = validator.Validate(calls, new ToolValidationPolicy
        {
            AllowList = new HashSet<string> { "search" },
            MustCall = new HashSet<string> { "search" },
            MaxTotalCalls = 3
        });

        report.IsValid.Should().BeTrue();
        report.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Fails_when_args_missing_required_property()
    {
        var contracts = new[]
        {
            new ToolContract(
                "search",
                "1.0.0",
                """
                {
                  "$schema": "https://json-schema.org/draft/2020-12/schema",
                  "type": "object",
                  "properties": { "query": { "type": "string" } },
                  "required": ["query"],
                  "additionalProperties": false
                }
                """
            )
        };

        var calls = new[]
        {
            new ToolCall("search", """{ }""")
        };

        var validator = new ToolCallValidator(contracts);
        var report = validator.Validate(calls);

        report.IsValid.Should().BeFalse();
        report.Issues.Should().Contain(i => i.Code == "tool.args.schema.invalid");
    }

    [Fact]
    public void Fails_when_tool_not_in_allowlist()
    {
        var contracts = new[]
        {
            new ToolContract("search","1.0.0","""{ "type":"object" }"""),
            new ToolContract("get_details","1.0.0","""{ "type":"object" }""")
        };

        var calls = new[]
        {
            new ToolCall("get_details", """{ }""")
        };

        var validator = new ToolCallValidator(contracts);
        var report = validator.Validate(calls, new ToolValidationPolicy
        {
            AllowList = new HashSet<string> { "search" }
        });

        report.IsValid.Should().BeFalse();
        report.Issues.Should().Contain(i => i.Code == "policy.allowlist.violation");
    }

    [Fact]
    public void Fails_when_order_rule_violated()
    {
        var contracts = new[]
        {
            new ToolContract("search","1.0.0","""{ "type":"object" }"""),
            new ToolContract("get_details","1.0.0","""{ "type":"object" }""")
        };

        var calls = new[]
        {
            new ToolCall("get_details", """{ }"""),
            new ToolCall("search", """{ }""")
        };

        var validator = new ToolCallValidator(contracts);
        var report = validator.Validate(calls, new ToolValidationPolicy
        {
            MustOccurBefore = new() { ("search", "get_details") }
        });

        report.IsValid.Should().BeFalse();
        report.Issues.Should().Contain(i => i.Code == "policy.order.violation");
    }
}