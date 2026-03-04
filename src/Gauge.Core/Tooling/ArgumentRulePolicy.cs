namespace Gauge.Core.Tooling;

/// <summary>
/// Regras semânticas adicionais sobre ArgsJson (além do JSON Schema).
/// Usa JSON Pointer (RFC 6901) para navegar no JSON.
/// </summary>
public sealed record ArgumentRulePolicy
{
    /// <summary>Lista de regras.</summary>
    public List<ArgumentRule> Rules { get; init; } = new();
}

public sealed record ArgumentRule
{
    /// <summary>Tool alvo (case-insensitive). Ex: "search".</summary>
    public string ToolName { get; init; } = "";

    /// <summary>JSON Pointer para extrair valor de ArgsJson. Ex: "/query" ou "/topK".</summary>
    public string Pointer { get; init; } = "";

    /// <summary>Operador da regra.</summary>
    public ArgOp Op { get; init; } = ArgOp.Exists;

    /// <summary>Valor de comparação (string). Ex: "10", "abc".</summary>
    public string? Value { get; init; }

    /// <summary>Valores para IN/NOT_IN.</summary>
    public List<string>? Values { get; init; }

    /// <summary>Mensagem customizada (opcional).</summary>
    public string? Message { get; init; }

    /// <summary>Se true, regra é invertida (nega o resultado).</summary>
    public bool Negate { get; init; } = false;

    /// <summary>Se true, falha quando o ponteiro não existe (default: true para Exists, false para outros).</summary>
    public bool? FailIfMissing { get; init; }

    /// <summary>Se definido, usa Context.Variables[ValueFromContextKey] como valor esperado.</summary>
    public string? ValueFromContextKey { get; init; }
}

public enum ArgOp
{
    Exists,
    Equals,
    Contains,
    StartsWith,
    EndsWith,
    Regex,
    NumberGte,
    NumberLte,
    NumberBetween,
    In,
    NotIn
}