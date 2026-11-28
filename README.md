# McpProbe

[![CI](https://github.com/ShmueLior/mcpprobe/actions/workflows/ci.yml/badge.svg)](https://github.com/ShmueLior/mcpprobe/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/vpre/McpProbe.svg)](https://www.nuget.org/packages/McpProbe)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

Test kit for MCP servers built with the [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).

- **In-memory host.** Start your server in the test process and get a connected `McpClient`. No ports, no child processes.
- **Assertions** for tools, tool results (text, images, structured output, errors), prompts and resources.
- **Tool surface snapshots.** Commit `mcp-tools.json`, and the test fails when a change would break existing clients.
- **Lint rules** for tool names, descriptions and parameters.
- **`mcpprobe` CLI** that runs the snapshot and lint checks against any MCP server over stdio or HTTP, for CI.

Works with any test framework: failures are plain `McpProbeAssertionException`s. Targets .NET 8 and .NET 10, on ModelContextProtocol 2.4.0.

> Status: 0.3.0-preview on NuGet. The API may change before 1.0.

![mcpprobe CLI: snapshot a server, lint it, then catch a breaking tool-surface change](docs/demo.gif)

## Quickstart

```bash
dotnet add package McpProbe --prerelease
```

```csharp
using McpProbe;
using McpProbe.Assertions;
using Microsoft.Extensions.DependencyInjection; // WithTools<T>()

[Fact]
public async Task Forecast_works()
{
    await using var probe = await McpProbeHost.StartAsync(mcp => mcp.WithTools<WeatherTools>());

    var tools = await probe.Client.ListToolsAsync();
    McpAssert.That(tools).ContainsTool("get_forecast").HasParameter("city", type: "string", required: true);

    var result = await probe.Client.CallToolAsync("get_forecast", new Dictionary<string, object?> { ["city"] = "Paris" });
    McpAssert.That(result).Succeeded().HasStructuredValue("city", "Paris");
}
```

`StartAsync` takes the same `IMcpServerBuilder` you configure in `Program.cs`, so put your `WithTools`/`WithPrompts`/`WithResources` calls in one extension method and share it (see [samples/WeatherServer](samples/WeatherServer)). Register your own services with `McpProbeHostOptions.ConfigureServices`.

## Assertions

Every assertion returns the same object, so you can chain them. Failures throw `McpProbeAssertionException` with a message that says what was expected and what was there.

```csharp
McpAssert.That(tools).ContainsTool("delete_file").HasDestructiveHint().DoesNotHaveParameter("force");
McpAssert.That(result).Failed("Unknown city");
McpAssert.That(result).HasTextContent("sunny", StringComparison.OrdinalIgnoreCase);
McpAssert.That(result).HasImageContent("image/png");
McpAssert.That(result).HasStructuredValue("days[0].high", 26);
McpAssert.That(prompts).ContainsPrompt("plan_trip").HasArgument("city", required: true);
McpAssert.That(resources).ContainsResource("weather://cities").HasMimeType("text/plain");
```

## Snapshot workflow

```csharp
[Fact]
public Task Tool_surface_matches_snapshot() => probe.VerifyToolSnapshotAsync();
```

1. The first run writes `mcp-tools.json` next to the test file and fails, so you look at it. Commit it.
2. Later runs compare the live tool surface with the file:
   - **Breaking changes fail the test**: removed tool, removed parameter, newly required parameter, narrowed type or enum, tighter input constraints (`minimum`, `maxLength`, `minItems`, `multipleOf`, a new or changed `pattern` or `format`, a new `const`, `uniqueItems: true` or `additionalProperties: false`, ...), and removed or narrowed output fields. A removed parameter that looks renamed gets a hint.
   - **Non-breaking changes pass**: added tools, optional parameters, descriptions, annotations, widened types, looser input constraints. They are in the returned `ToolSurfaceDiff`, and `SnapshotOptions.Log` gets a report (for example `Log = output.WriteLine` with xUnit's `ITestOutputHelper`).
   - Local `$ref`s (`#/$defs/...`, `#/definitions/...`, and the `#/properties/...` refs the C# SDK emits for recursive types) are followed, so a change inside a referenced schema is classified like an inline one.
   - Schema-valued `additionalProperties` (and `unevaluatedProperties`, `additionalItems`, `unevaluatedItems`) are compared like nested schemas, at paths such as `input.meta[additionalProperties]`.
   - Schema keywords McpProbe doesn't classify yet (such as `allOf`, `anyOf` or `oneOf`) are reported as non-breaking, marked "review by hand".
3. On a failure, the current surface is written to `mcp-tools.received.json`. To accept the change, run with `MCPPROBE_UPDATE_SNAPSHOTS=1` (or set `SnapshotOptions.Update`), review the diff in git, and commit.

On CI (`CI` or `TF_BUILD` set), a missing snapshot is not created: the test fails. Set `SnapshotOptions.FailOnNonBreakingChanges` to fail on every change.

Example failure:

```
Tool surface changed: 1 breaking, 2 non-breaking.

Breaking:
  - get_forecast: Parameter 'days' type changed from string to integer. [input.days]

Non-breaking:
  ~ get_alerts: Tool was added.
  ~ get_forecast: Parameter 'days' became optional. [input.days]
```

## Lint rules

```csharp
var report = await probe.LintToolsAsync();
report.ThrowIfFailed(); // fails on warnings and errors by default

var strict = await probe.LintToolsAsync(new LintOptions { Strict = true });
strict.ThrowIfFailed();
```

| Id | Default | Checks |
|---|---|---|
| MCPP001 | error | The tool has a description. |
| MCPP002 | warning | The description is at least `MinToolDescriptionLength` (20) characters, or at least `MinToolDescriptionWords` (4) words. |
| MCPP003 | warning | Every parameter has a description. |
| MCPP004 | warning | The tool name is 1-128 characters of `A-Z a-z 0-9 _ - .`. |
| MCPP005 | warning | All tool names use the same style (snake_case, camelCase, ...). |
| MCPP006 | warning | All parameter names use the same style. |
| MCPP007 | info | Parameter names aren't vague (`data`, `value`, `input`, ...). A top-level `value` or `data` is fine on a tool named `create_*`, `set_*`, `publish_*` and similar (`PayloadContextTokens`). |
| MCPP009 | error | No bare `true`/`false` where a schema object is expected (`--strict`). |
| MCPP010 | warning | `type` is not a union array (`--strict`). `[T, "null"]`, the nullable spelling the C# and Go SDKs emit, is not reported. |
| MCPP011 | warning | Every schema has a constraining keyword (`--strict`). |
| MCPP012 | warning | `$ref` does not point outside the document (`--strict`). |
| MCPP013 | warning | Hints don't contradict: not read-only and destructive at once, and a `delete_*`-style name isn't marked `destructiveHint: false` or `readOnlyHint: true`. |
| MCPP014 | warning | A description that promises structured results ("returns JSON", "structured content", ...) comes with an `outputSchema`. |
| MCPP015 | warning | The description says more than the tool name or title. |
| MCPP016 | warning | No empty `enum`, and an `enum` declares its `type`. |

Change severities with `LintOptions.Severities["MCPP007"] = LintSeverity.None`, and the threshold with `LintOptions.FailOn`. By default MCPP003, MCPP006 and MCPP007 check top-level parameters. Set `LintOptions.CheckNestedParameters = true` (CLI: `mcpprobe lint --nested`) to also check nested object properties and array items (reported as `home.zip` or `stops[].city`), following local `$ref`s; a schema used in several places is checked once.

Set `LintOptions.Strict = true` (CLI: `mcpprobe lint --strict`) to also run MCPP009-MCPP012, the schema-portability checks from MCP Inspector, on `inputSchema` and `outputSchema`. Those findings are reported with paths such as `inputSchema.properties.city` (the existing parameter rules still use `city`). MCPP010 does not report `[T, "null"]` (for example `["integer","null"]`): the C# and Go SDKs write that for every nullable parameter and an author can't change it, so this differs from Inspector's type-union check; use Inspector if you need its exact report. Unions of two or more non-null types, and longer unions with `null`, are still reported. Portability findings fail the report only when their effective severity is error, so a type-union warning does not trip the default `FailOn = Warning` gate; use `--rule MCPP010=error` if you want that warning to fail. A single portability rule can be turned on without `--strict` by setting its severity, or turned off with `--rule MCPP011=none`. MCPP008 is reserved for a community issue and is not implemented here.

MCPP013-MCPP016 run by default and use `FailOn` like MCPP001-MCPP007. They are kept narrow on purpose: MCPP013 only reads explicit hints (an unset `destructiveHint` is fine) and matches whole name segments from `LintOptions.DestructiveNameTokens`; MCPP014 only fires on the phrases in `LintOptions.StructuredOutputCues`; MCPP016 walks `inputSchema` and `outputSchema` with the same paths as the `--strict` rules, and does not ask for a `type` on mixed-type enums (including ones with `null`) or on `anyOf`/`oneOf`/`allOf` branches, which usually take their type from the parent.

## CLI in CI

Write a snapshot once and commit it, then check it in CI:

```bash
dotnet tool install --global McpProbe.Cli --version 0.3.0-preview

mcpprobe snapshot --stdio "dotnet run --project src/MyServer" --out mcp-tools.json
mcpprobe diff     --stdio "dotnet run --project src/MyServer" --baseline mcp-tools.json
mcpprobe lint     --http https://localhost:5001/mcp --header "Authorization: Bearer $TOKEN"
mcpprobe lint     --http http://localhost:5000/sse --transport sse --header "Authorization: Bearer $TOKEN"
```

`--http` already tries Streamable HTTP and falls back to legacy SSE. Use `--transport streamable-http` or `--transport sse` to pin a mode (`auto` is the default). Legacy SSE servers usually listen at `/sse` (and `/message`), not at the Streamable HTTP path. If the server maps MCP at a prefix such as `/mcp` and enables legacy SSE, the C# SDK places those endpoints under the prefix (`/mcp/sse` and `/mcp/message`). There is no WebSocket transport.

Exit codes: `0` passed, `1` check failed (breaking change or lint findings), `2` usage or connection error. The server's stderr is printed when it fails to start. `diff` and `lint` also take `--from-file <snapshot>` to check a file without starting a server. Run `mcpprobe <command> --help` for every option.

### HTML report

`lint` and `diff` take `--report <file>` to also write a self-contained HTML page (inline CSS, no scripts): a banner, a one-line summary and, when there are findings or changes, a table. It is not in 0.3.0-preview; it comes with the next release. The text report on stdout and the exit code do not change, the file is written whether the check passed or failed (exit `0` or `1`), and nothing is written on a usage or connection error (exit `2`). In code, use `LintReport.ToHtml()` and `ToolSurfaceDiff.ToHtml()`.

```bash
mcpprobe lint --stdio "dotnet run --project src/MyServer" --report mcp-lint.html
mcpprobe diff --stdio "dotnet run --project src/MyServer" --baseline mcp-tools.json --report mcp-diff.html
```

To keep the files from a CI run, upload them as an artifact. `if: always()` keeps the upload when the check fails:

```yaml
      - uses: actions/upload-artifact@v4
        if: always()
        with:
          name: mcpprobe-reports
          path: |
            mcp-lint.html
            mcp-diff.html
```

### GitHub Actions

The `McpProbe tool surface check` Action installs the same `McpProbe.Cli` version as the tag you pin, then runs `diff` and `lint`. It needs `actions/setup-dotnet` (a .NET 8 or .NET 10 SDK) and your server already built. Reports go to the job summary. Pin an exact release tag that includes the Action (0.3.0-preview or later):

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:

permissions:
  contents: read

jobs:
  mcp:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v5
      - uses: actions/setup-dotnet@v5
        with:
          dotnet-version: |
            8.0.x
            10.0.x
      - run: dotnet build -c Release
      - name: Check MCP tool surface
        uses: ShmueLior/mcpprobe@v0.3.0-preview
        with:
          stdio: dotnet run --no-build --project src/MyServer -c Release
          baseline: mcp-tools.json
```

Inputs follow the CLI: `stdio`, `http`, or `from-file` (exactly one), plus `baseline` (default `mcp-tools.json`), `header`, `env`, `cwd`, `timeout`, `diff`, `lint`, `fail-on-non-breaking`, `fail-on`, `nested`, `rule`, `min-description-length`, `strict`, `transport`, `report`, `version`, and `source`. Both `diff` and `lint` are on by default. The Action fails the step on a breaking change or a lint finding at or above `--fail-on` (default `warning`).

`strict`, `transport`, and `report` are off by default, so an unchanged workflow runs the same commands as before. `strict` is lint-only. `transport` is only used with `http`. `report` is a directory, not a file: the Action writes `lint.html` and/or `diff.html` there (both checks run by default, so a single file path would overwrite one report with the other). The job summary stays the plain-text report. Upload is left to your workflow.

`strict` and `transport` already work against the 0.3.0-preview CLI. `report` needs a CLI that has `--report`, which 0.3.0-preview does not. If you set `report` and the installed tool lacks the flag, the Action fails fast with a message instead of a generic "unrecognized option". Until 0.4 is published, use `uses: ./` with `source:` pointing at a just-packed folder, or pin a later tag that includes these inputs. Do not add these inputs to a workflow that pins `ShmueLior/mcpprobe@v0.3.0-preview`; that tag's wrapper does not have them.

```yaml
      - name: Check MCP tool surface
        id: mcpprobe
        uses: ShmueLior/mcpprobe@<tag-that-includes-these-inputs>
        with:
          stdio: dotnet run --no-build --project src/MyServer -c Release
          baseline: mcp-tools.json
          strict: true
          report: mcpprobe-reports
      - uses: actions/upload-artifact@v7
        if: always()
        with:
          name: mcpprobe-reports
          path: mcpprobe-reports/
```

The `lint-report` and `diff-report` outputs are the paths of the files that were written (empty if that check did not write one).

### Azure Pipelines

If your organization can add a GitHub service connection, use the step template in this repository (check out your own repo first; the template checks out McpProbe to run the same script as the Action):

```yaml
resources:
  repositories:
    - repository: mcpprobe
      type: github
      name: ShmueLior/mcpprobe
      endpoint: GitHub
      ref: refs/tags/v0.3.0-preview

steps:
  - checkout: self
  - task: UseDotNet@2
    inputs:
      version: '10.x'
  - script: dotnet build -c Release
  - template: templates/azure-pipelines/mcpprobe.yml@mcpprobe
    parameters:
      stdio: 'dotnet run --no-build --project src/MyServer -c Release'
      baseline: mcp-tools.json
```

The template needs bash (Git Bash on Windows). It also accepts `strict`, `transport`, and `report` (`report` is a directory, as in the Action). Do not add those parameters to a pipeline that pins `refs/tags/v0.3.0-preview`; that tag's template does not have them. Upload is a separate step; the template does not publish an artifact:

```yaml
  - task: PublishPipelineArtifact@1
    condition: always()
    inputs:
      targetPath: mcpprobe-reports
      artifact: mcpprobe-reports
```

If you cannot reference the GitHub repo, install the tool and run the same commands yourself:

```yaml
- task: UseDotNet@2
  inputs:
    version: '10.x'
- script: |
    set -euo pipefail
    tool_path="$(Agent.TempDirectory)/mcpprobe-cli"
    dotnet tool install McpProbe.Cli --tool-path "$tool_path" --version 0.3.0-preview
    export PATH="$tool_path:$PATH"
    mcpprobe diff --stdio "dotnet run --no-build --project src/MyServer -c Release" --baseline mcp-tools.json
    mcpprobe lint --stdio "dotnet run --no-build --project src/MyServer -c Release"
  displayName: Check MCP tool surface
```

## Snapshot file format

```json
{
  "formatVersion": 1,
  "tools": [
    { "name": "get_forecast", "description": "...", "inputSchema": { }, "outputSchema": { }, "annotations": { } }
  ]
}
```

Tools are sorted by name and JSON keys are sorted, so diffs stay small. Comparison is semantic: formatting and line endings don't matter.

## Limitations

- Only local `$ref`s are followed. Remote refs, `$anchor`/`$id` refs and `$dynamicRef` are compared as plain keywords, and so is `$defs` when such a ref is present.
- `allOf`, `anyOf` and `oneOf` aren't walked; changes inside them are reported as unclassified keyword changes.
- Two different `pattern`s or `format`s can't be compared, so any change to one counts as breaking.
- The test suite in this repo uses xUnit v3. The quickstart was also run once by hand with NUnit 4, MSTest 4 and TUnit 1.87 against the packed library; those checks aren't part of CI yet.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). License: [MIT](LICENSE).
