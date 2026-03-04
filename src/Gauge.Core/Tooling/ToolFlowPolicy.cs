namespace Gauge.Core.Tooling;

/// <summary>
/// State machine simples baseado em tool calls.
/// Cada tool call pode gerar uma "ação" (actionKey) que dirige transições entre estados.
/// </summary>
public sealed record ToolFlowPolicy
{
    /// <summary>Estado inicial do fluxo.</summary>
    public string StartState { get; init; } = "start";

    /// <summary>Estados finais aceitáveis (ex.: "answered", "done").</summary>
    public HashSet<string>? AcceptStates { get; init; }

    /// <summary>
    /// Mapeia tool name -> actionKey (ex.: "search" -> "searched").
    /// Se não existir, usa o próprio tool name como actionKey.
    /// </summary>
    public Dictionary<string, string>? ToolToAction { get; init; }

    /// <summary>
    /// Transições permitidas: (fromState, actionKey) -> toState.
    /// </summary>
    public List<FlowTransition> Transitions { get; init; } = new();

    /// <summary>
    /// Se true, qualquer tool call sem transição válida falha.
    /// Se false, tool calls “desconhecidas” não mudam estado, mas podem ser validadas por outras policies.
    /// </summary>
    public bool Strict { get; init; } = true;
}

public sealed record FlowTransition(string From, string Action, string To);