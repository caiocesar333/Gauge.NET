## 1) README.md (cole na raiz do repo)

````md
# Gauge.NET

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
```bash
dotnet tool install -g gauge
````

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

---

## 2) release workflow: `.github/workflows/release.yml`

Esse workflow publica **o tool `gauge` no NuGet** quando você criar uma tag `vX.Y.Z` (ex.: `v0.2.0-alpha.1`). Ele também faz upload do `.nupkg` como artifact.

> Pré-requisito: `NUGET_API_KEY` em **GitHub Secrets**.

```yaml
name: Release (NuGet)

on:
  push:
    tags:
      - "v*"

jobs:
  publish:
    runs-on: ubuntu-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "9.0.x"

      - name: Restore
        run: dotnet restore

      - name: Build
        run: dotnet build -c Release --no-restore

      - name: Test
        run: dotnet test -c Release --no-build

      - name: Pack (Gauge.Cli)
        run: dotnet pack ./src/Gauge.Cli -c Release -o ./artifacts

      - name: Upload nupkg artifact
        uses: actions/upload-artifact@v4
        with:
          name: nupkg
          path: ./artifacts/*.nupkg

      - name: Publish to NuGet
        run: dotnet nuget push "./artifacts/*.nupkg" --api-key "${{ secrets.NUGET_API_KEY }}" --source "https://api.nuget.org/v3/index.json" --skip-duplicate
````
