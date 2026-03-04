namespace Gauge.Core.Tooling;

public sealed record ToolContract(
    string Name,
    string Version,
    string ArgsJsonSchema,
    string? OutputJsonSchema = null
);