using System.Text.Json;
using Gauge.Core.Tooling;
using Gauge.Core.Tracing;
using Gauge.Core.Tracing.Payloads;

var now = DateTimeOffset.Now;

var userInput = "Find ids for Gauge.NET and then fetch details for A1.";

var trace = new AgentRunTrace(
    RunId: "run-0002",
    TestId: "tc-ctx-llm-001",
    StartedAt: now,
    CompletedAt: now.AddSeconds(1),
    Metadata: new TraceMetadata(Model: "gpt-x", Temperature: 0.2, PromptHash: "prompt_v1"),
    Steps: new List<TraceStepEnvelope>
    {
        new(
            Kind: "llm_call",
            At: now,
            Payload: JsonSerializer.SerializeToElement(new LlmCallPayload(
                Prompt: "You are an agent. Decide which tool to call.",
                Response: "Calling search with query=Gauge.NET",
                Model: "gpt-x",
                Temperature: 0.2,
                PromptTokens: 20,
                CompletionTokens: 10,
                TotalTokens: 30,
                LatencyMs: 120
            ))
        ),
        new(
            Kind: "tool_call",
            At: now.AddMilliseconds(200),
            Payload: JsonSerializer.SerializeToElement(new ToolCallPayload(new ToolCall(
                Name: "search",
                ArgsJson: """{ "query":"Gauge.NET", "topK": 5 }""",
                OutputJson: """{ "ids": ["A1","B2"] }""",
                StartedAt: now.AddMilliseconds(200),
                CompletedAt: now.AddMilliseconds(350),
                Status: "ok"
            )))
        ),
        new(
            Kind: "tool_call",
            At: now.AddMilliseconds(400),
            Payload: JsonSerializer.SerializeToElement(new ToolCallPayload(new ToolCall(
                Name: "get_details",
                ArgsJson: """{ "id":"A1" }""",
                OutputJson: """{ "id":"A1", "name":"Gauge.NET" }""",
                StartedAt: now.AddMilliseconds(400),
                CompletedAt: now.AddMilliseconds(520),
                Status: "ok"
            )))
        )
    },
    Context: new TraceContext(
        UserInput: userInput,
        Variables: new Dictionary<string, string>
        {
            ["keyword"] = "Gauge.NET"
        }
    )
);

var json = JsonSerializer.Serialize(trace, new JsonSerializerOptions { WriteIndented = true });
await File.WriteAllTextAsync("run_trace.json", json);
Console.WriteLine("Wrote run_trace.json");