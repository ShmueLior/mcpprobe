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

> Status: 0.2.0-preview on NuGet. The API may change before 1.0.

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
   - **Breaking changes fail the test**: removed tool, removed parameter, newly required parameter, narrowed type or enum, tighter input constraints (`minimum`, `maxLength`, `minItems`, `multipleOf`, a new or changed `pattern`, ...), and removed or narrowed output fields. A removed parameter that looks renamed gets a hint.
   - **Non-breaking changes pass**: added tools, optional parameters, descriptions, annotations, widened types, looser input constraints. They are in the returned `ToolSurfaceDiff`, and `SnapshotOptions.Log` gets a report (for example `Log = output.WriteLine` with xUnit's `ITestOutputHelper`).
   - Local `$ref`s (`#/$defs/...`, `#/definitions/...`, and the `#/properties/...` refs the C# SDK emits for recursive types) are followed, so a change inside a referenced schema is classified like an inline one.
   - Schema keywords McpProbe doesn't classify yet (such as `format` or `uniqueItems`) are reported as non-breaking, marked "review by hand".
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
```

| Id | Default | Checks |
|---|---|---|
| MCPP001 | error | The tool has a description. |
| MCPP002 | warning | The description is at least `MinToolDescriptionLength` (20) characters. |
| MCPP003 | warning | Every parameter has a description. |
| MCPP004 | warning | The tool name is 1-128 characters of `A-Z a-z 0-9 _ - .`. |
| MCPP005 | warning | All tool names use the same style (snake_case, camelCase, ...). |
| MCPP006 | warning | All parameter names use the same style. |
| MCPP007 | info | Parameter names aren't vague (`data`, `value`, `input`, ...). |

Change severities with `LintOptions.Severities["MCPP007"] = LintSeverity.None`, and the threshold with `LintOptions.FailOn`. By default MCPP003, MCPP006 and MCPP007 check top-level parameters. Set `LintOptions.CheckNestedParameters = true` (CLI: `mcpprobe lint --nested`) to also check nested object properties and array items (reported as `home.zip` or `stops[].city`), following local `$ref`s; a schema used in several places is checked once.

## CLI in CI

Write a snapshot once and commit it, then check it in CI:

```bash
dotnet tool install --global McpProbe.Cli --version 0.2.0-preview

mcpprobe snapshot --stdio "dotnet run --project src/MyServer" --out mcp-tools.json
mcpprobe diff     --stdio "dotnet run --project src/MyServer" --baseline mcp-tools.json
mcpprobe lint     --http https://localhost:5001/mcp --header "Authorization: Bearer $TOKEN"
mcpprobe lint     --http http://localhost:5000/sse --transport sse --header "Authorization: Bearer $TOKEN"
```

`--http` already tries Streamable HTTP and falls back to legacy SSE. Use `--transport streamable-http` or `--transport sse` to pin a mode (`auto` is the default). Legacy SSE servers usually listen at `/sse` (and `/message`), not at the Streamable HTTP path. If the server maps MCP at a prefix such as `/mcp` and enables legacy SSE, the C# SDK places those endpoints under the prefix (`/mcp/sse` and `/mcp/message`). There is no WebSocket transport.

Exit codes: `0` passed, `1` check failed (breaking change or lint findings), `2` usage or connection error. The server's stderr is printed when it fails to start. `diff` and `lint` also take `--from-file <snapshot>` to check a file without starting a server. Run `mcpprobe <command> --help` for every option.

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

Inputs follow the CLI: `stdio`, `http`, or `from-file` (exactly one), plus `baseline` (default `mcp-tools.json`), `header`, `env`, `cwd`, `timeout`, `diff`, `lint`, `fail-on-non-breaking`, `fail-on`, `nested`, `rule`, `min-description-length`, `version`, and `source`. Both `diff` and `lint` are on by default. The Action fails the step on a breaking change or a lint finding at or above `--fail-on` (default `warning`).

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

The template needs bash (Git Bash on Windows). If you cannot reference the GitHub repo, install the tool and run the same commands yourself:

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
- Two different `pattern`s can't be compared, so any change to one counts as breaking. `format`, `uniqueItems`, `const` and `additionalProperties` aren't classified yet.
- The test suite in this repo uses xUnit v3. The quickstart was also run once by hand with NUnit 4, MSTest 4 and TUnit 1.87 against the packed library; those checks aren't part of CI yet.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). License: [MIT](LICENSE).
