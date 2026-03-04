using System.Text.Json;

namespace Gauge.Core.Tracing;

public sealed record TraceStepEnvelope(
    string Kind,
    DateTimeOffset At,
    JsonElement Payload
);