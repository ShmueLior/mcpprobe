# Contributing

Thanks for helping. Issues and pull requests are welcome.

## Build and test

You need the .NET 10 SDK. The .NET 8 runtime is also needed to run the net8.0 tests.

```bash
dotnet build -c Release
dotnet test --solution McpProbe.slnx -c Release
dotnet pack -c Release -o artifacts
```

Warnings are errors. Run the tests before you open a pull request.

## Guidelines

- Keep the core framework-agnostic: failures throw `McpProbeAssertionException`, with no test framework references in `src/`.
- Failure messages should say what was expected, what was found, and how to fix it.
- New public API must be added to `src/McpProbe/PublicAPI.Unshipped.txt` (the build tells you what's missing).
- Changes to breaking-change rules need a test in `ToolSurfaceComparerTests` for each direction (input and output).
- Add a line to `CHANGELOG.md` under "Unreleased".
- If you change the sample server's tools, update `samples/WeatherServer.Tests/mcp-tools.json` by running its tests with `MCPPROBE_UPDATE_SNAPSHOTS=1`.
- When you bump the package version, also update the default `version` input in `action.yml` and the pinned tags in the README CI examples. `action/check-version.sh` (run in CI and release) checks the Action default against the project version.

## Bugs

Include the McpProbe and ModelContextProtocol versions, the tool schema involved, and the full failure message.
