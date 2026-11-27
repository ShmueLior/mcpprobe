# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- GitHub Action (`uses: ShmueLior/mcpprobe@v0.3.0-preview`) and an Azure Pipelines step template that install a pinned `McpProbe.Cli` and run `diff` and `lint`. Reports go to the job summary.
- `mcpprobe --transport auto|streamable-http|sse` pins the HTTP transport when using `--http`. The default (`auto`) is unchanged: try Streamable HTTP, then fall back to legacy SSE.
- `LintOptions.Strict` and `mcpprobe lint --strict`: schema-portability rules MCPP009-MCPP012 (bare boolean schemas, type unions, untyped schemas and remote `$ref`s), ported from MCP Inspector. Off by default. These findings fail the report only when their effective severity is Error. MCPP008 is reserved for a community issue and is not included.
- Snapshot diffs classify `format`, `uniqueItems`, `const`, `additionalProperties`, `unevaluatedProperties`, `additionalItems` and `unevaluatedItems` as `ConstraintChanged`, with the same rule as other constraints. When both sides give `additionalProperties` (or a sibling) as a schema, the two schemas are compared like nested ones.
- Quality lint rules, on by default as warnings: MCPP013 (destructive hint contradicts `readOnlyHint` or a `delete_*`-style name), MCPP014 (description promises structured results but there is no `outputSchema`), MCPP015 (description only restates the name or title) and MCPP016 (empty `enum`, or `enum` without `type`). New `LintOptions.DestructiveNameTokens` and `LintOptions.StructuredOutputCues`.

### Changed

- Default `lint` can now fail on MCPP013-MCPP016 findings. Turn one off with `--rule MCPP014=none` or `LintOptions.Severities`. A description that only restates the tool name reports MCPP015 instead of MCPP002.
- Changes to `format`, `uniqueItems`, `const` or `additionalProperties` that make an input tighter or an output looser (for example a new input `additionalProperties: false`, or an output that drops it) now fail the snapshot check. A changed `format` or `const` value fails on both sides. The `additionalProperties` rule also covers `unevaluatedProperties`, `additionalItems` and `unevaluatedItems`. In 0.2 these passed as unclassified "review by hand" changes. Snapshot files don't change.

## [0.2.0-preview] - 2026-11-27

### Added

- Snapshot diffs follow local `$ref`s (`#/$defs/...`, `#/definitions/...`, `#/properties/...`), so changes inside referenced schemas are classified like inline ones. Recursive schemas are handled.
- Constraint keywords are classified: `minimum`, `maximum`, `exclusiveMinimum`, `exclusiveMaximum`, `minLength`, `maxLength`, `minItems`, `maxItems`, `minProperties`, `maxProperties`, `multipleOf` and `pattern`. Tighter input constraints are breaking, looser ones aren't; outputs are the other way round. New change kind `ToolSurfaceChangeKind.ConstraintChanged`.
- `LintOptions.CheckNestedParameters` and `mcpprobe lint --nested`: MCPP003, MCPP006 and MCPP007 can also check nested object properties and array items, following local `$ref`s. Off by default.
- Release workflow that publishes packages to nuget.org with Trusted Publishing.

### Changed

- A tighter input constraint (for example a lower `maximum` from `[Range]`) now fails the snapshot check. In 0.1 it passed as an unclassified "review by hand" change.

### Fixed

- `mcpprobe` could print only "Broken pipe" without the server's stderr when a stdio server exited right away. It now waits briefly for the stderr lines before reporting the error.

## [0.1.0-preview] - 2026-11-27

### Added

- `McpProbeHost`: runs an MCP server in memory and returns a connected `McpClient`.
- `McpAssert.That(...)` assertions for tool lists, tools, tool results (text, image, content blocks, structured output, errors), prompts, prompt results, resources and resource contents.
- Tool surface snapshots (`mcp-tools.json`) with breaking-change detection: removed tools, removed or renamed parameters, newly required parameters, type and enum changes, and output schema changes.
- `MCPPROBE_UPDATE_SNAPSHOTS` and `SnapshotOptions` to accept changes; missing snapshots fail on CI.
- Lint rules MCPP001-MCPP007 with configurable severities.
- `mcpprobe` .NET tool with `snapshot`, `diff` and `lint` commands over stdio or HTTP.
- Sample weather server and a sample test project.
