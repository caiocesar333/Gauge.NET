

# Gauge.NET
<img width="2762" height="1331" alt="Gauge NET" src="https://github.com/user-attachments/assets/7209e6d0-c2b0-4c11-ac4b-ad90be01fe25" />

Gauge.NET is a .NET AI Agent Test Harness focused on **tool-call validation**, **cross-step correctness**, and **CI-ready evaluation** for agent traces.

It validates:
- Tool call **args/output schemas** (JSON Schema)
- **Known tool contracts** enforcement
- **Flow rules** (allow/deny/must-call/order)
- **Cross-step rules** (e.g., `get_details.id ∈ search.output.ids`)
- **LLM thresholds** (tokens, latency, #calls)
- **Baseline regression** (new issue codes, higher issue counts, validity regressions)
- CI outputs: **JUnit XML** and **JSON reports**

---

## Install

### Global tool (recommended)
dotnet tool install -g GaugeNET


### Local tool (recommended for CI)

```bash
dotnet tool restore
```

> This repo uses a local tool manifest (`.config/dotnet-tools.json`). In CI, always run `dotnet tool restore`.

---

## Quickstart

### Validate a suite (folder + glob)

```bash
dotnet gauge validate --path ./traces --glob "**/*.json" --contracts ./contracts.json
```

### Generate a JUnit report

```bash
dotnet gauge report junit --path ./traces --glob "**/*.json" --contracts ./contracts.json --out ./results.junit.xml
```

### Generate a JSON report

```bash
dotnet gauge report json --path ./traces --glob "**/*.json" --contracts ./contracts.json --out ./results.json
```

---

## Baseline Regression (Suite)

### Create suite baseline

```bash
dotnet gauge baseline suite create \
  --path ./traces \
  --glob "**/*.json" \
  --contracts ./contracts.json \
  --out ./baseline.suite.json
```

Commit `baseline.suite.json` to your repo.

### Compare against baseline (CI gate)

```bash
dotnet gauge baseline suite compare ./baseline.suite.json \
  --path ./traces \
  --glob "**/*.json" \
  --contracts ./contracts.json \
  --report junit \
  --out ./results.junit.xml \
  --delta-out ./delta.json
```

Exit codes:

* `0` OK (no regression)
* `2` Validation failed (when used in validate/report flows)
* `3` Regression detected (baseline compare)

---

## Trace formats

Gauge.NET accepts **two** trace formats:

### 1) Legacy: `ToolCall[]`

An array of tool calls.

### 2) Envelope: `AgentRunTrace`

A modern trace with steps (e.g., `llm_call`, `tool_call`) plus metadata.

---

## Contracts format

Tool contracts are a JSON array of `ToolContract`:

* `Name`, `Version`
* `ArgsJsonSchema` (JSON Schema)
* `OutputJsonSchema` (JSON Schema, optional)

---

## GitHub Actions CI

This repo includes a workflow in `.github/workflows/gauge.yml` that:

* builds/tests the solution
* runs suite baseline compare
* uploads `results.junit.xml` and `delta.json` as artifacts

---

## Contributing

PRs are welcome. Please:

* keep changes focused and tested
* add or update samples/traces when changing validation behavior

See `CONTRIBUTING.md`.

---

## License

MIT. See `LICENSE`.

---

## Project links

* GitHub: [https://github.com/caiocesar333/Gauge.NET](https://github.com/caiocesar333/Gauge.NET)
* Issues: [https://github.com/caiocesar333/Gauge.NET/issues](https://github.com/caiocesar333/Gauge.NET/issues)

````
