using Gauge.Core.Tooling;

namespace Gauge.Core.Tracing.Payloads;

public sealed record ToolCallPayload(
    ToolCall Call
);