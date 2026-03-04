using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gauge.Core.Tooling;

namespace Gauge.Tools.ToolValidation;

internal static class ArgumentRuleValidator
{
    public static IEnumerable<ValidationIssue> ValidateRules(
     ToolCall call,
     int callIndex,
     ArgumentRulePolicy rulesPolicy,
     IReadOnlyDictionary<string, string>? contextVars = null)
    {
        if (rulesPolicy.Rules.Count == 0)
            yield break;

        if (!TryParseJson(call.ArgsJson, out var argsDoc, out var argsErr))
        {
            yield return new ValidationIssue(
                "tool.args.json.invalid",
                $"ArgsJson is not valid JSON: {argsErr}",
                call.Name,
                callIndex);
            yield break;
        }

        var root = argsDoc.RootElement;

        foreach (var rule in rulesPolicy.Rules)
        {
            if (!string.Equals(rule.ToolName, call.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            var pointer = rule.Pointer ?? "";
            var found = JsonPointer.TryResolve(root, pointer, out var element);

            var failIfMissing = rule.FailIfMissing
                ?? (rule.Op == ArgOp.Exists); // default: Exists falha se missing; outros não (a menos que definido)

            if (!found)
            {
                if (failIfMissing)
                {
                    yield return Issue(rule, call, callIndex, pointer,
                        $"Missing pointer '{pointer}' in args.");
                }
                continue;
            }

            var expected = ResolveExpectedValue(rule, contextVars);

            var ok = Evaluate(rule, element, expected);

            if (rule.Negate) ok = !ok;

            if (!ok)
            {
                var msg = rule.Message ?? DefaultMessage(rule, pointer);
                yield return new ValidationIssue(
                    Code: "tool.args.rule.failed",
                    Message: msg,
                    ToolName: call.Name,
                    CallIndex: callIndex,
                    JsonPath: "$args" + pointer // representação consistente
                );
            }
        }
    }
    private static string? ResolveExpectedValue(ArgumentRule rule, IReadOnlyDictionary<string, string>? contextVars)
    {
        if (!string.IsNullOrWhiteSpace(rule.ValueFromContextKey)
            && contextVars is not null
            && contextVars.TryGetValue(rule.ValueFromContextKey, out var v))
            return v;

        return rule.Value;
    }
    private static bool Evaluate(ArgumentRule rule, JsonElement el, string? expected)
    {
        switch (rule.Op)
        {
            case ArgOp.Exists:
                return true;

            case ArgOp.Equals:
                return CompareString(el, expected) == 0;

            case ArgOp.Contains:
                return GetString(el).Contains(expected ?? "", StringComparison.OrdinalIgnoreCase);

            case ArgOp.StartsWith:
                return GetString(el).StartsWith(expected ?? "", StringComparison.OrdinalIgnoreCase);

            case ArgOp.EndsWith:
                return GetString(el).EndsWith(expected ?? "", StringComparison.OrdinalIgnoreCase);

            case ArgOp.Regex:
                return Regex.IsMatch(GetString(el), expected ?? "", RegexOptions.IgnoreCase);

            case ArgOp.NumberGte:
                return GetNumber(el) >= ParseNumber(expected);

            case ArgOp.NumberLte:
                return GetNumber(el) <= ParseNumber(expected);

            case ArgOp.NumberBetween:
                var (min, max) = ParseBetween(expected);
                var n = GetNumber(el);
                return n >= min && n <= max;

            case ArgOp.In:
                return InSet(el, rule.Values);

            case ArgOp.NotIn:
                return !InSet(el, rule.Values);

            default:
                return false;
        }
    }

    private static int CompareString(JsonElement el, string? other)
    {
        var a = GetString(el);
        var b = other ?? "";
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetString(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString() ?? "",
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "",
            _ => el.GetRawText()
        };
    }

    private static double GetNumber(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d))
            return d;

        if (el.ValueKind == JsonValueKind.String && double.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var ds))
            return ds;

        throw new InvalidOperationException("Element is not a number.");
    }

    private static double ParseNumber(string? s)
    {
        if (s is null) throw new InvalidOperationException("Rule.Value is required for numeric ops.");
        if (!double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            throw new InvalidOperationException($"Invalid numeric value '{s}'.");
        return d;
    }

    private static (double Min, double Max) ParseBetween(string? s)
    {
        if (s is null) throw new InvalidOperationException("Rule.Value is required for NumberBetween.");
        var parts = s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) throw new InvalidOperationException("NumberBetween expects Value='min,max'.");
        return (ParseNumber(parts[0]), ParseNumber(parts[1]));
    }

    private static bool InSet(JsonElement el, List<string>? values)
    {
        values ??= new List<string>();
        var s = GetString(el);
        return values.Any(v => string.Equals(v, s, StringComparison.OrdinalIgnoreCase));
    }

    private static ValidationIssue Issue(ArgumentRule rule, ToolCall call, int callIndex, string pointer, string reason)
        => new(
            Code: "tool.args.rule.error",
            Message: rule.Message ?? reason,
            ToolName: call.Name,
            CallIndex: callIndex,
            JsonPath: "$args" + pointer
        );

    private static string DefaultMessage(ArgumentRule rule, string pointer)
        => $"Argument rule failed: tool='{rule.ToolName}', pointer='{pointer}', op='{rule.Op}'.";

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