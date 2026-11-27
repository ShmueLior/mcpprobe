# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Release workflow that publishes packages to nuget.org with Trusted Publishing.

## [0.1.0-preview] - 2026-11-27

### Added

- `McpProbeHost`: runs an MCP server in memory and returns a connected `McpClient`.
- `McpAssert.That(...)` assertions for tool lists, tools, tool results (text, image, content blocks, structured output, errors), prompts, prompt results, resources and resource contents.
- Tool surface snapshots (`mcp-tools.json`) with breaking-change detection: removed tools, removed or renamed parameters, newly required parameters, type and enum changes, and output schema changes.
- `MCPPROBE_UPDATE_SNAPSHOTS` and `SnapshotOptions` to accept changes; missing snapshots fail on CI.
- Lint rules MCPP001-MCPP007 with configurable severities.
- `mcpprobe` .NET tool with `snapshot`, `diff` and `lint` commands over stdio or HTTP.
- Sample weather server and a sample test project.
