namespace Gauge.Core.Tooling;

public sealed record ToolValidationPolicy
{
    /// <summary>Se preenchido, apenas tools presentes aqui são permitidas.</summary>
    public HashSet<string>? AllowList { get; init; }

    /// <summary>Tools explicitamente proibidas.</summary>
    public HashSet<string>? DenyList { get; init; }

    /// <summary>Tools que DEVEM ocorrer ao menos 1 vez.</summary>
    public HashSet<string>? MustCall { get; init; }

    /// <summary>Quantidade máxima total de tool calls.</summary>
    public int? MaxTotalCalls { get; init; }

    /// <summary>Quantidade máxima por tool (Name -> max).</summary>
    public Dictionary<string, int>? MaxCallsPerTool { get; init; }

    /// <summary>Se true, toda tool call deve ter um contrato (ToolContract) conhecido.</summary>
    public bool RequireKnownToolContracts { get; init; } = true;

    /// <summary>
    /// Regras de ordem simples: A deve acontecer antes de B.
    /// Ex: ("search", "get_details") => search deve ocorrer antes de get_details.
    /// </summary>
    public List<(string Before, string After)>? MustOccurBefore { get; init; }
    public HashSet<string>? MustNotCall { get; init; }
    public Dictionary<string, int>? MinCallsPerTool { get; init; }
    public bool RequireOutputWhenSchemaProvided { get; init; } = false;
    public ToolFlowPolicy? Flow { get; init; }
    public ArgumentRulePolicy? ArgumentRules { get; init; }
    public CrossStepRulePolicy? CrossStepRules { get; init; }
    public ThresholdPolicy? Thresholds { get; init; }
}