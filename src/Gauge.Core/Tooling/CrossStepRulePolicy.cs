namespace Gauge.Core.Tooling;

public sealed record CrossStepRulePolicy
{
    public List<CrossStepRule> Rules { get; init; } = new();
}

public sealed record CrossStepRule
{
    /// <summary>Regra identificável (aparece em report)</summary>
    public string Id { get; init; } = "";

    /// <summary>Tool que será validada (target).</summary>
    public string TargetTool { get; init; } = "";

    /// <summary>Pointer no args da target tool (ex.: "/id").</summary>
    public string TargetArgPointer { get; init; } = "";

    /// <summary>Tool de referência (source) de onde vem a evidência (ex.: "search").</summary>
    public string SourceTool { get; init; } = "";

    /// <summary>Pointer no outputJson da source tool (ex.: "/results").</summary>
    public string SourceOutputPointer { get; init; } = "";

    /// <summary>
    /// Modo de comparação:
    /// - "InArray": target value deve existir em array (string/number) extraído do source.
    /// - "Equals": target value deve ser igual ao source value.
    /// </summary>
    public CrossCompareMode Mode { get; init; } = CrossCompareMode.InArray;

    /// <summary>Mensagem custom.</summary>
    public string? Message { get; init; }

    /// <summary>Se true, procura a source tool apenas antes da target (default true).</summary>
    public bool SourceMustBeBeforeTarget { get; init; } = true;

    /// <summary>Se true, falha se não existir source (default true).</summary>
    public bool FailIfSourceMissing { get; init; } = true;
}

public enum CrossCompareMode
{
    InArray,
    Equals
}