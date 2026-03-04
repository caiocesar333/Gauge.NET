using FluentAssertions;
using Gauge.Core.Tooling;
using Gauge.Tools.ToolValidation;

namespace Gauge.Tools.Tests;

public class ArgumentRuleValidatorTests
{
    [Fact]
    public void Passes_when_rules_satisfied()
    {
        var contracts = new[]
        {
            new ToolContract("search","1.0.0","""{ "type":"object" }""")
        };

        var calls = new[]
        {
            new ToolCall("search", """{ "query":"Gauge.NET", "topK": 5 }""")
        };

        var policy = new ToolValidationPolicy
        {
            ArgumentRules = new ArgumentRulePolicy
            {
                Rules = new()
                {
                    new ArgumentRule { ToolName="search", Pointer="/query", Op=ArgOp.Regex, Value=".{3,}" },
                    new ArgumentRule { ToolName="search", Pointer="/topK", Op=ArgOp.NumberBetween, Value="1,10" }
                }
            }
        };

        var report = new ToolCallValidator(contracts).Validate(calls, policy);
        report.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Fails_when_topK_out_of_range()
    {
        var contracts = new[]
        {
            new ToolContract("search","1.0.0","""{ "type":"object" }""")
        };

        var calls = new[]
        {
            new ToolCall("search", """{ "query":"Gauge.NET", "topK": 50 }""")
        };

        var policy = new ToolValidationPolicy
        {
            ArgumentRules = new ArgumentRulePolicy
            {
                Rules = new()
                {
                    new ArgumentRule { ToolName="search", Pointer="/topK", Op=ArgOp.NumberBetween, Value="1,10" }
                }
            }
        };

        var report = new ToolCallValidator(contracts).Validate(calls, policy);
        report.IsValid.Should().BeFalse();
        report.Issues.Should().Contain(i => i.Code == "tool.args.rule.failed");
    }

    [Fact]
    public void Can_fail_when_pointer_missing_if_configured()
    {
        var contracts = new[]
        {
            new ToolContract("search","1.0.0","""{ "type":"object" }""")
        };

        var calls = new[]
        {
            new ToolCall("search", """{ "topK": 5 }""")
        };

        var policy = new ToolValidationPolicy
        {
            ArgumentRules = new ArgumentRulePolicy
            {
                Rules = new()
                {
                    new ArgumentRule
                    {
                        ToolName="search",
                        Pointer="/query",
                        Op=ArgOp.Regex,
                        Value=".{3,}",
                        FailIfMissing=true,
                        Message="query is required for search"
                    }
                }
            }
        };

        var report = new ToolCallValidator(contracts).Validate(calls, policy);
        report.IsValid.Should().BeFalse();
        report.Issues.Should().Contain(i => i.Code == "tool.args.rule.error" || i.Code == "tool.args.rule.failed");
    }
}   