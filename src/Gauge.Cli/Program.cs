using Gauge.Cli;
using Gauge.Core.Baselines;
using Gauge.Core.Tooling;
using Gauge.Core.Tracing;
using Gauge.Tools.ToolValidation;
using System.CommandLine;
using System.Text.Json;
using System.CommandLine.Invocation;

var root = new RootCommand("Gauge.NET - tool-call validation");

// -------------------------
// Shared options/args
// -------------------------

// Agora aceita zero ou mais traces (para validate/report)
var traceFilesArg = new Argument<FileInfo[]>(
    name: "traces",
    description: "Trace JSON files. Can be ToolCall[] or AgentRunTrace."
)
{
    Arity = ArgumentArity.ZeroOrMore
};

var tracePathOpt = new Option<DirectoryInfo?>(
    name: "--path",
    description: "Directory containing trace JSON files.");

var globOpt = new Option<string>(
    name: "--glob",
    description: "Glob pattern (supports **, *, ?) used under --path. Default: **/*.json",
    getDefaultValue: () => "**/*.json");

var contractsOpt = new Option<FileInfo>(
    name: "--contracts",
    description: "Path to contracts JSON file (ToolContract[]).")
{ IsRequired = true };

var policyPathOpt = new Option<FileInfo?>(
    name: "--policy",
    description: "Optional policy JSON file (ToolValidationPolicy).");

var outOpt = new Option<FileInfo>(
    name: "--out",
    description: "Output file path.")
{ IsRequired = true };

var formatOpt = new Option<string>(
    name: "--format",
    description: "Output format for validate: console (default), junit, json.",
    getDefaultValue: () => "console");

// validate controls
var failFastOpt = new Option<bool>(
    name: "--fail-fast",
    description: "Stop on first failure (validation failure or read error).");

var maxFailuresOpt = new Option<int?>(
    name: "--max-failures",
    description: "Stop after N failures (validation failure or read error).");

var suiteReportOpt = new Option<string>(
    name: "--report",
    description: "Optional report output for the current run: none (default), junit, json.",
    getDefaultValue: () => "none");

var suiteReportOutOpt = new Option<FileInfo?>(
    name: "--out",
    description: "Output file path for --report junit|json.");

var suiteDeltaOutOpt = new Option<FileInfo?>(
    name: "--delta-out",
    description: "Optional output JSON file containing regression deltas.");

// -------------------------
// validate (suite no console)
// -------------------------
var validateCmd = new Command("validate", "Validate trace(s) against tool contracts and policy")
{
    traceFilesArg,
    contractsOpt,
    policyPathOpt,
    tracePathOpt,
    globOpt,
    failFastOpt,
    maxFailuresOpt
};

validateCmd.SetHandler(async (InvocationContext ctx) =>
{
    var traceFiles = ctx.ParseResult.GetValueForArgument(traceFilesArg) ?? Array.Empty<FileInfo>();
    var contractsFile = ctx.ParseResult.GetValueForOption(contractsOpt)!;
    var policyFile = ctx.ParseResult.GetValueForOption(policyPathOpt);
    var traceDir = ctx.ParseResult.GetValueForOption(tracePathOpt);
    var glob = ctx.ParseResult.GetValueForOption(globOpt) ?? "**/*.json";
    var failFast = ctx.ParseResult.GetValueForOption(failFastOpt);
    var maxFailures = ctx.ParseResult.GetValueForOption(maxFailuresOpt);
    var outFile = ctx.ParseResult.GetValueForOption(outOpt)!;

    var contracts = await ReadJsonAsync<List<ToolContract>>(contractsFile.FullName);
    var policy = policyFile is null ? null : await ReadJsonAsync<ToolValidationPolicy>(policyFile.FullName);

    var allTraces = ResolveTraceFiles(traceFiles, traceDir, glob);
    if (allTraces.Count == 0)
        throw new ArgumentException("No trace files provided. Provide trace files or use --path with --glob.");

    var validator = new ToolCallValidator(contracts);

    Console.WriteLine($"Gauge.NET validate - traces={allTraces.Count}");
    if (traceDir is not null) Console.WriteLine($"  path={traceDir.FullName} glob={glob}");
    Console.WriteLine($"  contracts={contractsFile.FullName}");
    if (policyFile is not null) Console.WriteLine($"  policy={policyFile.FullName}");
    if (failFast) Console.WriteLine("  fail-fast=ON");
    if (maxFailures is not null) Console.WriteLine($"  max-failures={maxFailures}");
    Console.WriteLine();

    var format = (ctx.ParseResult.GetValueForOption(formatOpt) ?? "console").Trim().ToLowerInvariant();

    var run = await RunSuiteAsync(validator, allTraces, policy, failFast, maxFailures);

    switch (format)
    {
        case "console":
            // comportamento atual: imprime linha a linha
            foreach (var c in run.Cases)
            {
                var ms = c.Duration.TotalMilliseconds;

                if (c.Report.IsValid)
                {
                    Console.WriteLine($"✅ {c.File.Name} ({ms:0}ms)");
                }
                else
                {
                    Console.WriteLine($"❌ {c.File.Name} ({ms:0}ms) issues={c.Report.Issues.Count}");
                    foreach (var issue in c.Report.Issues.Take(10))
                    {
                        var at = issue.CallIndex is null ? "" : $" [call#{issue.CallIndex}]";
                        var tool = issue.ToolName is null ? "" : $" tool={issue.ToolName}";
                        var path = issue.JsonPath is null ? "" : $" path={issue.JsonPath}";
                        Console.WriteLine($"   - {issue.Code}{at}{tool}{path}: {issue.Message}");
                    }
                    if (c.Report.Issues.Count > 10)
                        Console.WriteLine($"   ... ({c.Report.Issues.Count - 10} more)");
                }
            }

            if (run.Cases.Count < allTraces.Count && (failFast || maxFailures is not null))
            {
                Console.WriteLine();
                Console.WriteLine($"⏹ Stopped early (processed={run.Cases.Count}/{allTraces.Count}, failures={run.Failures})");
            }

            Console.WriteLine();
            Console.WriteLine($"Summary: traces={allTraces.Count}, processed={run.Cases.Count}, failures={run.Failures}, readErrors={run.ReadErrors}, totalIssues={run.TotalIssues}");
            break;

        case "junit":
            if (outFile is null)
                throw new ArgumentException("--out is required when --format is junit.");

            var junitCases = run.Cases.Select(c =>
                new JUnitWriter.TestcaseResult(
                    Name: c.Name,
                    ClassName: "Gauge.NET",
                    Duration: c.Duration,
                    Report: c.Report
                )).ToList();

            var suiteXml = JUnitWriter.WriteSuite("Gauge.NET", junitCases);
            await File.WriteAllTextAsync(outFile.FullName, suiteXml);

            Console.WriteLine($"JUnit written to: {outFile.FullName}");
            Console.WriteLine($"Tests: {allTraces.Count}, Processed: {run.Cases.Count}, Failures: {run.Failures}, ReadErrors: {run.ReadErrors}");
            break;

        case "json":
            if (outFile is null)
                throw new ArgumentException("--out is required when --format is json.");

            var jsonCases = run.Cases.Select(c => new GaugeJsonReportCase(
                File: c.File.FullName,
                RunId: c.RunId,
                TestId: c.TestId,
                IsValid: c.Report.IsValid,
                DurationMs: (int)Math.Round(c.Duration.TotalMilliseconds),
                IssueCount: c.Report.Issues.Count,
                IssueCodes: c.Report.Issues.Select(i => i.Code).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Issues: c.Report.Issues.ToList()
            )).ToList();

            var asm = typeof(Program).Assembly.GetName();
            var envelope = new GaugeJsonReport(
                Framework: "Gauge.NET",
                Version: asm.Version?.ToString() ?? "unknown",
                GeneratedAt: DateTimeOffset.Now,
                Summary: new GaugeJsonReportSummary(
                    Total: allTraces.Count,
                    Passed: allTraces.Count - run.Failures,
                    Failed: run.Failures,
                    ReadErrors: run.ReadErrors,
                    TotalIssues: run.TotalIssues
                ),
                Cases: jsonCases
            );

            var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(outFile.FullName, json);

            Console.WriteLine($"JSON report written to: {outFile.FullName}");
            Console.WriteLine($"Tests: {allTraces.Count}, Processed: {run.Cases.Count}, Failures: {run.Failures}, ReadErrors: {run.ReadErrors}");
            break;

        default:
            throw new ArgumentException($"Invalid --format '{format}'. Supported: console, junit, json.");
    }

    // exit code do validate: 0 ok, 2 se qualquer falhar (inclui read error)
    Environment.ExitCode = run.Failures == 0 ? 0 : 2;
    Console.WriteLine();
    Console.WriteLine($"Summary: traces={allTraces.Count}, processed={run.Cases.Count}, failures={run.Failures}, readErrors={run.ReadErrors}, totalIssues={run.TotalIssues}");

    Environment.ExitCode = run.Failures == 0 ? 0 : 2;
});

validateCmd.AddOption(formatOpt);

root.AddCommand(validateCmd);

// -------------------------
// baseline (mantém semântica, mas usa leitura resiliente)
// -------------------------
var baselinePathArg = new Argument<FileInfo>("baseline", "Path to baseline snapshot JSON.");

var baselineCmd = new Command("baseline", "Baseline operations");

var createCmd = new Command("create", "Create a baseline snapshot from a run")
{
    // single-file baseline ainda faz sentido
    new Argument<FileInfo>("trace", "Path to a trace JSON file. Can be ToolCall[] or AgentRunTrace."),
    contractsOpt,
    policyPathOpt,
    outOpt
};

createCmd.SetHandler(async (InvocationContext ctx) =>
{
    var traceFile = ctx.ParseResult.GetValueForArgument((Argument<FileInfo>)createCmd.Arguments[0])!;
    var contractsFile = ctx.ParseResult.GetValueForOption(contractsOpt)!;
    var policyFile = ctx.ParseResult.GetValueForOption(policyPathOpt);
    var outFile = ctx.ParseResult.GetValueForOption(outOpt)!;

    var contracts = await ReadJsonAsync<List<ToolContract>>(contractsFile.FullName);
    var policy = policyFile is null ? null : await ReadJsonAsync<ToolValidationPolicy>(policyFile.FullName);

    var validator = new ToolCallValidator(contracts);
    var report = await ValidateTraceFileSafeAsync(validator, traceFile.FullName, policy);

    var snap = new BaselineSnapshot(
        CreatedAt: DateTimeOffset.Now,
        IsValid: report.IsValid,
        IssueCount: report.Issues.Count,
        IssueCodes: report.Issues.Select(i => i.Code).ToList(),
        Issues: report.Issues.ToList()
    );

    var json = JsonSerializer.Serialize(snap, new JsonSerializerOptions { WriteIndented = true });
    await File.WriteAllTextAsync(outFile.FullName, json);

    Console.WriteLine($"Baseline written to: {outFile.FullName}");
    Console.WriteLine($"IsValid={snap.IsValid}, IssueCount={snap.IssueCount}");

    Environment.ExitCode = 0;
}); 
baselineCmd.AddCommand(createCmd);

var compareCmd = new Command("compare", "Compare a run against a baseline snapshot")
{
    baselinePathArg,
    new Argument<FileInfo>("trace", "Path to a trace JSON file. Can be ToolCall[] or AgentRunTrace."),
    contractsOpt,
    policyPathOpt
};

compareCmd.SetHandler(async (InvocationContext ctx) =>
{
    var baselineFile = ctx.ParseResult.GetValueForArgument(baselinePathArg)!;
    var traceFile = ctx.ParseResult.GetValueForArgument((Argument<FileInfo>)compareCmd.Arguments[1])!;
    var contractsFile = ctx.ParseResult.GetValueForOption(contractsOpt)!;
    var policyFile = ctx.ParseResult.GetValueForOption(policyPathOpt);

    var baseline = await ReadJsonAsync<BaselineSnapshot>(baselineFile.FullName);
    var contracts = await ReadJsonAsync<List<ToolContract>>(contractsFile.FullName);
    var policy = policyFile is null ? null : await ReadJsonAsync<ToolValidationPolicy>(policyFile.FullName);

    var validator = new ToolCallValidator(contracts);
    var report = await ValidateTraceFileSafeAsync(validator, traceFile.FullName, policy);

    var baselineCodes = new HashSet<string>(baseline.IssueCodes ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
    var currentCodes = new HashSet<string>(report.Issues.Select(i => i.Code), StringComparer.OrdinalIgnoreCase);

    var newCodes = currentCodes.Except(baselineCodes).ToList();
    var worseCount = report.Issues.Count > baseline.IssueCount;
    var regressedValidity = baseline.IsValid && !report.IsValid;

    if (regressedValidity || worseCount || newCodes.Count > 0)
    {
        Console.WriteLine("❌ REGRESSION");
        Console.WriteLine($"Baseline: IsValid={baseline.IsValid}, IssueCount={baseline.IssueCount}");
        Console.WriteLine($"Current : IsValid={report.IsValid}, IssueCount={report.Issues.Count}");

        if (newCodes.Count > 0)
            Console.WriteLine($"New issue codes: {string.Join(", ", newCodes)}");

        Environment.ExitCode = 3;
        return;
    }

    Console.WriteLine("✅ OK (no regression)");
    Console.WriteLine($"Baseline: IsValid={baseline.IsValid}, IssueCount={baseline.IssueCount}");
    Console.WriteLine($"Current : IsValid={report.IsValid}, IssueCount={report.Issues.Count}");
    Environment.ExitCode = 0;
});
baselineCmd.AddCommand(compareCmd);


root.AddCommand(baselineCmd);


var suiteCmd = new Command("suite", "Suite baseline operations");
baselineCmd.AddCommand(suiteCmd);

var suiteCreateCmd = new Command("create", "Create a suite baseline snapshot from multiple traces")
{
    traceFilesArg,
    contractsOpt,
    policyPathOpt,
    tracePathOpt,
    globOpt,
    outOpt
};
// opcionalmente:
suiteCreateCmd.AddOption(failFastOpt);
suiteCreateCmd.AddOption(maxFailuresOpt);

suiteCreateCmd.SetHandler(async (InvocationContext ctx) =>
{
    var traceFiles = ctx.ParseResult.GetValueForArgument(traceFilesArg) ?? Array.Empty<FileInfo>();
    var contractsFile = ctx.ParseResult.GetValueForOption(contractsOpt)!;
    var policyFile = ctx.ParseResult.GetValueForOption(policyPathOpt);
    var traceDir = ctx.ParseResult.GetValueForOption(tracePathOpt);
    var glob = ctx.ParseResult.GetValueForOption(globOpt) ?? "**/*.json";
    var outFile = ctx.ParseResult.GetValueForOption(outOpt)!;
    var failFast = ctx.ParseResult.GetValueForOption(failFastOpt);
    var maxFailures = ctx.ParseResult.GetValueForOption(maxFailuresOpt);

    var contracts = await ReadJsonAsync<List<ToolContract>>(contractsFile.FullName);
    var policy = policyFile is null ? null : await ReadJsonAsync<ToolValidationPolicy>(policyFile.FullName);

    var allTraces = ResolveTraceFiles(traceFiles, traceDir, glob);
    if (allTraces.Count == 0)
        throw new ArgumentException("No trace files provided. Provide trace files or use --path with --glob.");

    var validator = new ToolCallValidator(contracts);

    // Reusa o seu runner único
    var run = await RunSuiteAsync(validator, allTraces, policy, failFast, maxFailures);

    var cases = run.Cases.Select(c =>
    {
        var source = ComputeSourceLabel(c.File, traceDir);
        var key = ComputeCaseKey(c.TestId, source);

        return new BaselineCaseSnapshot(
            Key: key,
            Source: source,
            RunId: c.RunId,
            TestId: c.TestId,
            IsValid: c.Report.IsValid,
            IssueCount: c.Report.Issues.Count,
            IssueCodes: c.Report.Issues.Select(i => i.Code)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Issues: null // deixa leve por default (você pode colocar c.Report.Issues.ToList() se quiser)
        );
    }).ToList();

    var snap = new BaselineSuiteSnapshot(
        CreatedAt: DateTimeOffset.Now,
        CaseCount: cases.Count,
        FailedCount: cases.Count(x => !x.IsValid),
        Cases: cases
    );

    var json = JsonSerializer.Serialize(snap, new JsonSerializerOptions { WriteIndented = true });
    await File.WriteAllTextAsync(outFile.FullName, json);

    Console.WriteLine($"Suite baseline written to: {outFile.FullName}");
    Console.WriteLine($"Cases: {snap.CaseCount}, Failed: {snap.FailedCount}");

    Environment.ExitCode = 0;
});

suiteCmd.AddCommand(suiteCreateCmd);

var suiteBaselineArg = new Argument<FileInfo>(
    name: "baseline",
    description: "Path to suite baseline snapshot JSON (BaselineSuiteSnapshot).");

var suiteCompareCmd = new Command("compare", "Compare a run suite against a suite baseline snapshot")
{
    suiteBaselineArg,
    traceFilesArg,
    contractsOpt,
    policyPathOpt,
    tracePathOpt,
    globOpt
};
suiteCompareCmd.AddOption(failFastOpt);
suiteCompareCmd.AddOption(maxFailuresOpt);
suiteCompareCmd.AddOption(suiteReportOpt);
suiteCompareCmd.AddOption(suiteReportOutOpt);
suiteCompareCmd.AddOption(suiteDeltaOutOpt);

suiteCompareCmd.SetHandler(async (InvocationContext ctx) =>
{
    var baselineFile = ctx.ParseResult.GetValueForArgument(suiteBaselineArg)!;

    var traceFiles = ctx.ParseResult.GetValueForArgument(traceFilesArg) ?? Array.Empty<FileInfo>();
    var contractsFile = ctx.ParseResult.GetValueForOption(contractsOpt)!;
    var policyFile = ctx.ParseResult.GetValueForOption(policyPathOpt);
    var traceDir = ctx.ParseResult.GetValueForOption(tracePathOpt);
    var glob = ctx.ParseResult.GetValueForOption(globOpt) ?? "**/*.json";
    var failFast = ctx.ParseResult.GetValueForOption(failFastOpt);
    var maxFailures = ctx.ParseResult.GetValueForOption(maxFailuresOpt);

    var reportKind = (ctx.ParseResult.GetValueForOption(suiteReportOpt) ?? "none").Trim().ToLowerInvariant();
    var reportOut = ctx.ParseResult.GetValueForOption(suiteReportOutOpt);
    var deltaOut = ctx.ParseResult.GetValueForOption(suiteDeltaOutOpt);

    // validate report options
    if (reportKind is not ("none" or "junit" or "json"))
        throw new ArgumentException($"Invalid --report '{reportKind}'. Supported: none, junit, json.");

    if (reportKind != "none" && reportOut is null)
        throw new ArgumentException("--out is required when --report is junit or json.");

    var baseline = await ReadJsonAsync<BaselineSuiteSnapshot>(baselineFile.FullName);

    var contracts = await ReadJsonAsync<List<ToolContract>>(contractsFile.FullName);
    var policy = policyFile is null ? null : await ReadJsonAsync<ToolValidationPolicy>(policyFile.FullName);

    var allTraces = ResolveTraceFiles(traceFiles, traceDir, glob);
    if (allTraces.Count == 0)
        throw new ArgumentException("No trace files provided. Provide trace files or use --path with --glob.");

    var validator = new ToolCallValidator(contracts);

    // Run current suite
    var run = await RunSuiteAsync(validator, allTraces, policy, failFast, maxFailures);

    // If requested, write report for the CURRENT run (always, regardless of regression result)
    if (reportKind != "none")
    {
        if (reportKind == "junit")
        {
            var junitCases = run.Cases.Select(c =>
                new JUnitWriter.TestcaseResult(
                    Name: c.Name,
                    ClassName: "Gauge.NET",
                    Duration: c.Duration,
                    Report: c.Report
                )).ToList();

            var xml = JUnitWriter.WriteSuite("Gauge.NET", junitCases);
            await File.WriteAllTextAsync(reportOut!.FullName, xml);
            Console.WriteLine($"JUnit written to: {reportOut!.FullName}");
        }
        else if (reportKind == "json")
        {
            var jsonCases = run.Cases.Select(c => new GaugeJsonReportCase(
                File: c.File.FullName,
                RunId: c.RunId,
                TestId: c.TestId,
                IsValid: c.Report.IsValid,
                DurationMs: (int)Math.Round(c.Duration.TotalMilliseconds),
                IssueCount: c.Report.Issues.Count,
                IssueCodes: c.Report.Issues.Select(i => i.Code).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Issues: c.Report.Issues.ToList()
            )).ToList();

            var asm = typeof(Program).Assembly.GetName();
            var envelope = new GaugeJsonReport(
                Framework: "Gauge.NET",
                Version: asm.Version?.ToString() ?? "unknown",
                GeneratedAt: DateTimeOffset.Now,
                Summary: new GaugeJsonReportSummary(
                    Total: allTraces.Count,
                    Passed: allTraces.Count - run.Failures,
                    Failed: run.Failures,
                    ReadErrors: run.ReadErrors,
                    TotalIssues: run.TotalIssues
                ),
                Cases: jsonCases
            );

            var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(reportOut!.FullName, json);
            Console.WriteLine($"JSON report written to: {reportOut!.FullName}");
        }

        Console.WriteLine($"Report: tests={allTraces.Count}, processed={run.Cases.Count}, failures={run.Failures}, readErrors={run.ReadErrors}");
        Console.WriteLine();
    }

    // Build baseline maps
    var baselineByKey = baseline.Cases
        .GroupBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    // Build current maps
    var currentCases = run.Cases.Select(c =>
    {
        var source = ComputeSourceLabel(c.File, traceDir);
        var key = ComputeCaseKey(c.TestId, source);

        var codes = new HashSet<string>(
            c.Report.Issues.Select(i => i.Code),
            StringComparer.OrdinalIgnoreCase);

        return new
        {
            Key = key,
            Source = source,
            Case = c,
            IsValid = c.Report.IsValid,
            IssueCount = c.Report.Issues.Count,
            Codes = codes
        };
    }).ToList();

    var currentByKey = currentCases
        .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    // Collect deltas
    var deltas = new List<CaseRegressionDelta>();

    // 1) Compare current vs baseline
    foreach (var cur in currentCases)
    {
        if (baselineByKey.TryGetValue(cur.Key, out var b))
        {
            var baseCodes = new HashSet<string>(
                b.IssueCodes ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            var newCodes = cur.Codes.Except(baseCodes).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            var worseCount = cur.IssueCount > b.IssueCount;
            var regressedValidity = b.IsValid && !cur.IsValid;

            if (regressedValidity)
            {
                deltas.Add(new CaseRegressionDelta(
                    Key: cur.Key,
                    Source: cur.Source,
                    Kind: "validity_regressed",
                    BaselineIssueCount: b.IssueCount,
                    CurrentIssueCount: cur.IssueCount,
                    NewCodes: null
                ));
            }

            if (worseCount)
            {
                deltas.Add(new CaseRegressionDelta(
                    Key: cur.Key,
                    Source: cur.Source,
                    Kind: "worse_count",
                    BaselineIssueCount: b.IssueCount,
                    CurrentIssueCount: cur.IssueCount,
                    NewCodes: null
                ));
            }

            if (newCodes.Count > 0)
            {
                deltas.Add(new CaseRegressionDelta(
                    Key: cur.Key,
                    Source: cur.Source,
                    Kind: "new_codes",
                    BaselineIssueCount: b.IssueCount,
                    CurrentIssueCount: cur.IssueCount,
                    NewCodes: newCodes
                ));
            }
        }
        else
        {
            // New case not in baseline: only regress if it FAILS
            if (!cur.IsValid)
            {
                deltas.Add(new CaseRegressionDelta(
                    Key: cur.Key,
                    Source: cur.Source,
                    Kind: "new_failure",
                    BaselineIssueCount: null,
                    CurrentIssueCount: cur.IssueCount,
                    NewCodes: cur.Codes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
                ));
            }
        }
    }

    // 2) Baseline cases missing in current
    foreach (var b in baseline.Cases)
    {
        if (!currentByKey.ContainsKey(b.Key))
        {
            deltas.Add(new CaseRegressionDelta(
                Key: b.Key,
                Source: b.Source,
                Kind: "missing",
                BaselineIssueCount: b.IssueCount,
                CurrentIssueCount: null,
                NewCodes: null
            ));
        }
    }

    // Write delta JSON if requested (always write, even if empty; helps CI)
    if (deltaOut is not null)
    {
        var deltaEnvelope = new SuiteRegressionDelta(
            ComparedAt: DateTimeOffset.Now,
            BaselinePath: baselineFile.FullName,
            BaselineCases: baseline.CaseCount,
            CurrentProcessed: run.Cases.Count,
            CurrentFailures: run.Failures,
            CurrentReadErrors: run.ReadErrors,
            Regressions: deltas
        );

        var deltaJson = JsonSerializer.Serialize(deltaEnvelope, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(deltaOut.FullName, deltaJson);
        Console.WriteLine($"Delta written to: {deltaOut.FullName}");
        Console.WriteLine();
    }

    // Console output + exit code
    if (deltas.Count > 0)
    {
        Console.WriteLine("❌ REGRESSION (suite)");
        Console.WriteLine($"Baseline: cases={baseline.CaseCount}, failed={baseline.FailedCount}");
        Console.WriteLine($"Current : processed={run.Cases.Count}, failed={run.Failures}, readErrors={run.ReadErrors}");
        Console.WriteLine();

        foreach (var g in deltas.GroupBy(d => d.Key, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var first = g.First();
            Console.WriteLine($"❌ {g.Key} ({first.Source})");

            foreach (var d in g)
            {
                if (d.Kind == "new_codes")
                {
                    Console.WriteLine($"   - new_codes: {string.Join(", ", d.NewCodes ?? Array.Empty<string>())}");
                }
                else if (d.Kind == "worse_count")
                {
                    Console.WriteLine($"   - worse_count: {d.BaselineIssueCount} -> {d.CurrentIssueCount}");
                }
                else if (d.Kind == "validity_regressed")
                {
                    Console.WriteLine($"   - validity_regressed");
                }
                else if (d.Kind == "missing")
                {
                    Console.WriteLine($"   - missing (present in baseline, missing now)");
                }
                else if (d.Kind == "new_failure")
                {
                    Console.WriteLine($"   - new_failure (new failing case)");
                }
                else
                {
                    Console.WriteLine($"   - {d.Kind}");
                }
            }
        }

        Environment.ExitCode = 3;
        return;
    }

    Console.WriteLine("✅ OK (no regression in suite)");
    Console.WriteLine($"Baseline: cases={baseline.CaseCount}, failed={baseline.FailedCount}");
    Console.WriteLine($"Current : processed={run.Cases.Count}, failed={run.Failures}, readErrors={run.ReadErrors}");

    Environment.ExitCode = 0;
});
suiteCmd.AddCommand(suiteCompareCmd);

// -------------------------
// report (junit existente + json novo)
// -------------------------
var reportCmd = new Command("report", "Reporting commands");

// ---- junit (mesmo, mas com leitura resiliente)
var junitCmd = new Command("junit", "Generate a JUnit XML report from validation run(s)")
{
    traceFilesArg,
    contractsOpt,
    policyPathOpt,
    tracePathOpt,
    globOpt,
    outOpt
};

junitCmd.SetHandler(async (InvocationContext ctx) =>
{
    var traceFiles = ctx.ParseResult.GetValueForArgument(traceFilesArg) ?? Array.Empty<FileInfo>();
    var contractsFile = ctx.ParseResult.GetValueForOption(contractsOpt)!;
    var policyFile = ctx.ParseResult.GetValueForOption(policyPathOpt);
    var traceDir = ctx.ParseResult.GetValueForOption(tracePathOpt);
    var glob = ctx.ParseResult.GetValueForOption(globOpt) ?? "**/*.json";
    var outFile = ctx.ParseResult.GetValueForOption(outOpt)!;
    var failFast = ctx.ParseResult.GetValueForOption(failFastOpt);
    var maxFailures = ctx.ParseResult.GetValueForOption(maxFailuresOpt);

    var contracts = await ReadJsonAsync<List<ToolContract>>(contractsFile.FullName);
    var policy = policyFile is null ? null : await ReadJsonAsync<ToolValidationPolicy>(policyFile.FullName);

    var allTraces = ResolveTraceFiles(traceFiles, traceDir, glob);
    if (allTraces.Count == 0)
        throw new ArgumentException("No trace files provided. Provide trace files or use --path with --glob.");

    var validator = new ToolCallValidator(contracts);

    var run = await RunSuiteAsync(validator, allTraces, policy, failFast, maxFailures);

    var results = run.Cases.Select(c =>
        new JUnitWriter.TestcaseResult(
            Name: c.Name,
            ClassName: "Gauge.NET",
            Duration: c.Duration,
            Report: c.Report
        )).ToList();

    var suiteXml = JUnitWriter.WriteSuite("Gauge.NET", results);

    await File.WriteAllTextAsync(outFile.FullName, suiteXml);

    Console.WriteLine($"JUnit written to: {outFile.FullName}");
    Console.WriteLine($"Tests: {allTraces.Count}, Processed: {run.Cases.Count}, Failures: {run.Failures}, ReadErrors: {run.ReadErrors}");

    // mantém seu contrato: exit 2 se qualquer testcase falhar (inclui read error)
    Environment.ExitCode = run.Failures > 0 ? 2 : 0;
});
junitCmd.AddOption(failFastOpt);
junitCmd.AddOption(maxFailuresOpt);

reportCmd.AddCommand(junitCmd);

// ---- json (novo)
var jsonCmd = new Command("json", "Generate a JSON report from validation run(s)")
{
    traceFilesArg,
    contractsOpt,
    policyPathOpt,
    tracePathOpt,
    globOpt,
    outOpt
};

jsonCmd.SetHandler(async (InvocationContext ctx) =>
{
    var traceFiles = ctx.ParseResult.GetValueForArgument(traceFilesArg) ?? Array.Empty<FileInfo>();
    var contractsFile = ctx.ParseResult.GetValueForOption(contractsOpt)!;
    var policyFile = ctx.ParseResult.GetValueForOption(policyPathOpt);
    var traceDir = ctx.ParseResult.GetValueForOption(tracePathOpt);
    var glob = ctx.ParseResult.GetValueForOption(globOpt) ?? "**/*.json";
    var outFile = ctx.ParseResult.GetValueForOption(outOpt)!;
    var failFast = ctx.ParseResult.GetValueForOption(failFastOpt);
    var maxFailures = ctx.ParseResult.GetValueForOption(maxFailuresOpt);

    var contracts = await ReadJsonAsync<List<ToolContract>>(contractsFile.FullName);
    var policy = policyFile is null ? null : await ReadJsonAsync<ToolValidationPolicy>(policyFile.FullName);

    var allTraces = ResolveTraceFiles(traceFiles, traceDir, glob);
    if (allTraces.Count == 0)
        throw new ArgumentException("No trace files provided. Provide trace files or use --path with --glob.");

    var validator = new ToolCallValidator(contracts);

    var run = await RunSuiteAsync(validator, allTraces, policy, failFast, maxFailures);

    var cases = run.Cases.Select(c => new GaugeJsonReportCase(
        File: c.File.FullName,
        RunId: c.RunId,
        TestId: c.TestId,
        IsValid: c.Report.IsValid,
        DurationMs: (int)Math.Round(c.Duration.TotalMilliseconds),
        IssueCount: c.Report.Issues.Count,
        IssueCodes: c.Report.Issues.Select(i => i.Code).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        Issues: c.Report.Issues.ToList()
    )).ToList();

    var asm = typeof(Program).Assembly.GetName();
    var envelope = new GaugeJsonReport(
        Framework: "Gauge.NET",
        Version: asm.Version?.ToString() ?? "unknown",
        GeneratedAt: DateTimeOffset.Now,
        Summary: new GaugeJsonReportSummary(
            Total: allTraces.Count,
            Passed: allTraces.Count - run.Failures,
            Failed: run.Failures,
            ReadErrors: run.ReadErrors,
            TotalIssues: run.TotalIssues
        ),
        Cases: cases
    );

    var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true });
    await File.WriteAllTextAsync(outFile.FullName, json);

    Console.WriteLine($"JSON report written to: {outFile.FullName}");
    Console.WriteLine($"Tests: {allTraces.Count}, Processed: {run.Cases.Count}, Failures: {run.Failures}, ReadErrors: {run.ReadErrors}");

    Environment.ExitCode = run.Failures > 0 ? 2 : 0;
});
reportCmd.AddCommand(jsonCmd);


jsonCmd.AddOption(failFastOpt);
jsonCmd.AddOption(maxFailuresOpt);


root.AddCommand(reportCmd);

return await root.InvokeAsync(args);

// -------------------------
// Helpers
// -------------------------

static List<FileInfo> ResolveTraceFiles(FileInfo[] traceFiles, DirectoryInfo? traceDir, string glob)
{
    var allTraces = new List<FileInfo>();

    if (traceFiles is { Length: > 0 })
        allTraces.AddRange(traceFiles);

    if (traceDir is not null)
    {
        if (!traceDir.Exists)
            throw new DirectoryNotFoundException($"Trace directory not found: {traceDir.FullName}");

        var discovered = Globber.Enumerate(traceDir.FullName, glob)
            .Select(p => new FileInfo(p));

        allTraces.AddRange(discovered);
    }

    // Dedup + sort
    return allTraces
        .GroupBy(f => f.FullName, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.First())
        .OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase)
        .ToList();
}

static ValidationReport CreateReadErrorReport(string filePath, string code, string message)
{
    return ValidationReport.Invalid(new[]
    {
        new ValidationIssue(
            Code: code,
            Message: $"Failed to read/parse trace '{Path.GetFileName(filePath)}': {message}",
            ToolName: null,
            CallIndex: null,
            JsonPath: "$"
        )
    });
}

static async Task<ValidationReport> ValidateTraceFileSafeAsync(
    ToolCallValidator validator,
    string tracePath,
    ToolValidationPolicy? policy)
{
    var read = await TryReadTraceAsync(tracePath);

    if (!read.Ok)
        return CreateReadErrorReport(tracePath, read.ErrorCode!, read.ErrorMessage!);

    if (read.Trace is not null)
        return validator.Validate(read.Trace, policy);

    return validator.Validate(read.Calls!, policy);
}

static async Task<(bool Ok, IReadOnlyList<ToolCall>? Calls, AgentRunTrace? Trace, string? ErrorCode, string? ErrorMessage)> TryReadTraceAsync(string path)
{
    try
    {
        var json = await File.ReadAllTextAsync(path);
        using var doc = JsonDocument.Parse(json);

        var opts = JsonOpts();

        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            var calls = JsonSerializer.Deserialize<List<ToolCall>>(json, opts);
            if (calls is null)
                return (false, null, null, "trace.calls.deserialize_failed", "Could not deserialize ToolCall[].");

            return (true, calls, null, null, null);
        }

        var trace = JsonSerializer.Deserialize<AgentRunTrace>(json, opts);
        if (trace is null)
            return (false, null, null, "trace.envelope.deserialize_failed", "Could not deserialize AgentRunTrace.");

        return (true, null, trace, null, null);
    }
    catch (FileNotFoundException ex)
    {
        return (false, null, null, "trace.io.not_found", ex.Message);
    }
    catch (UnauthorizedAccessException ex)
    {
        return (false, null, null, "trace.io.unauthorized", ex.Message);
    }
    catch (JsonException ex)
    {
        return (false, null, null, "trace.json.invalid", ex.Message);
    }
    catch (Exception ex)
    {
        return (false, null, null, "trace.read.error", ex.Message);
    }
}

static async Task<T> ReadJsonAsync<T>(string path)
{
    var json = await File.ReadAllTextAsync(path);
    var obj = JsonSerializer.Deserialize<T>(json, JsonOpts());

    if (obj is null)
        throw new InvalidOperationException($"Could not deserialize '{path}' into {typeof(T).Name}.");

    return obj;
}

static JsonSerializerOptions JsonOpts() => new()
{
    PropertyNameCaseInsensitive = true
};

// Centralized execution for a list of trace files.
static async Task<SuiteRun> RunSuiteAsync(
    ToolCallValidator validator,
    IReadOnlyList<FileInfo> traceFiles,
    ToolValidationPolicy? policy,
    bool failFast,
    int? maxFailures)
{
    var maxFail = failFast ? 1 : (maxFailures ?? int.MaxValue);

    var cases = new List<SuiteCase>(traceFiles.Count);
    var failures = 0;

    foreach (var file in traceFiles)
    {
        var started = DateTimeOffset.Now;

        var read = await TryReadTraceAsync(file.FullName);

        ValidationReport report;
        string name;
        string? runId = null;
        string? testId = null;
        bool readError = false;

        if (!read.Ok)
        {
            readError = true;

            // Standard: include both stable + specific code (helps filtering/regression).
            report = ValidationReport.Invalid(new[]
            {
                new ValidationIssue(
                    Code: "trace.read.error",
                    Message: $"Failed to read/parse trace '{file.Name}': {read.ErrorMessage}",
                    JsonPath: "$"),
                new ValidationIssue(
                    Code: read.ErrorCode ?? "trace.read.error",
                    Message: read.ErrorMessage ?? "Unknown read error.",
                    JsonPath: "$")
            });

            name = file.Name;
        }
        else if (read.Trace is not null)
        {
            runId = read.Trace.RunId;
            testId = read.Trace.TestId;
            name = string.IsNullOrWhiteSpace(testId) ? file.Name : testId;

            report = validator.Validate(read.Trace, policy);
        }
        else
        {
            name = file.Name;
            report = validator.Validate(read.Calls!, policy);
        }

        var duration = DateTimeOffset.Now - started;
        if (!report.IsValid) failures++;

        cases.Add(new SuiteCase(
            File: file,
            Name: name,
            RunId: runId,
            TestId: testId,
            Duration: duration,
            ReadError: readError,
            Report: report
        ));

        if (failures >= maxFail)
            break;
    }

    return new SuiteRun(cases);
}
static string ComputeSourceLabel(FileInfo file, DirectoryInfo? traceDir)
{
    if (traceDir is null) return file.Name;
    try
    {
        var rel = Path.GetRelativePath(traceDir.FullName, file.FullName);
        return rel.Replace('\\', '/');
    }
    catch
    {
        return file.Name;
    }
}

static string ComputeCaseKey(string? testId, string source)
    => string.IsNullOrWhiteSpace(testId) ? source : testId!;
// -------------------------
// JSON report models (novo)
// -------------------------
public sealed record GaugeJsonReport(
    string Framework,
    string Version,
    DateTimeOffset GeneratedAt,
    GaugeJsonReportSummary Summary,
    IReadOnlyList<GaugeJsonReportCase> Cases
);

public sealed record GaugeJsonReportSummary(
    int Total,
    int Passed,
    int Failed,
    int ReadErrors,
    int TotalIssues
);

public sealed record GaugeJsonReportCase(
    string File,
    string? RunId,
    string? TestId,
    bool IsValid,
    int DurationMs,
    int IssueCount,
    IReadOnlyList<string> IssueCodes,
    IReadOnlyList<ValidationIssue> Issues
);
// -------------------------
// Suite runner (shared by validate/report)
// -------------------------

internal sealed record SuiteCase(
    FileInfo File,
    string Name,
    string? RunId,
    string? TestId,
    TimeSpan Duration,
    bool ReadError,
    ValidationReport Report
);

internal sealed record SuiteRun(IReadOnlyList<SuiteCase> Cases)
{
    public int Total => Cases.Count;
    public int Failures => Cases.Count(c => !c.Report.IsValid);
    public int ReadErrors => Cases.Count(c => c.ReadError);
    public int TotalIssues => Cases.Sum(c => c.Report.Issues.Count);
}

internal sealed record SuiteRegressionDelta(
  DateTimeOffset ComparedAt,
  string BaselinePath,
  int BaselineCases,
  int CurrentProcessed,
  int CurrentFailures,
  int CurrentReadErrors,
  IReadOnlyList<CaseRegressionDelta> Regressions
);

internal sealed record CaseRegressionDelta(
  string Key,
  string Source,
  string Kind,             
  int? BaselineIssueCount,
  int? CurrentIssueCount,
  IReadOnlyList<string>? NewCodes
);