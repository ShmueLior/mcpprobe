# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- `LintReport.ToHtml()` and `ToolSurfaceDiff.ToHtml()`: a self-contained HTML page (inline CSS, no scripts) with a banner, a summary and, when there are findings or changes, a table. Server text is sanitized and HTML-encoded.
- `mcpprobe lint --report <file>` and `mcpprobe diff --report <file>` also write that page. Stdout and exit codes are unchanged. The file is written on exit 0 and 1, not on a usage or connection error (exit 2).
- GitHub Action and Azure Pipelines template inputs `strict`, `transport`, and `report`. All three are off by default, so existing workflows keep the same CLI invocation. `report` is a directory (`lint.html` and `diff.html`); the job summary is unchanged, and artifact upload is left to the workflow (`actions/upload-artifact` / `PublishPipelineArtifact@1`). `report` requires a CLI that has `--report`; the wrapper fails fast if the installed tool does not.
- Options-first overloads for `McpProbeHost.VerifyToolSnapshotAsync`, `ToolSnapshot.Verify` and `ToolSnapshot.VerifyAsync`, so `VerifyToolSnapshotAsync(new SnapshotOptions { ... })` compiles. The existing path-first signatures are unchanged. The new overloads name their options parameter `snapshotOptions`, so a call that names only `options:` still binds to the path-first overload.

### Fixed

- The README Quickstart now includes a short `WeatherTools` type (with `UseStructuredContent`) and the missing usings, so it compiles and runs as written. The snapshot example shows a real `VerifyToolSnapshotAsync` call with `Log`. Notes were added for `*.received.json` in `.gitignore` and for the CLI's net8.0/net10.0 runtime and `DOTNET_ROOT`.

### Changed

- `lint --strict`: MCPP010 no longer reports the nullable spelling `[T, "null"]` (for example `["integer","null"]`), which the C# and Go SDKs emit for every nullable parameter. Unions of two or more non-null types, and longer unions that include `null`, are still reported, with a shorter message. This differs from Inspector's type-union check.
- MCPP002 also needs fewer than `MinToolDescriptionWords` (default 4) words, so a clear description like "Delete a file share" is not flagged for being one character short. Set `LintOptions.MinToolDescriptionWords = 0` for the old character-only check.
- MCPP007 skips a top-level `value`, `data`, `payload` and similar parameter on tools whose name has a segment like `create`, `set` or `publish`. Configure it with `LintOptions.PayloadContextTokens`.
- MCPP005 and MCPP006 say what is mixed in a name, for example "mixed ('_' and '-')", instead of just "mixed".

### Fixed

- MCPP005 and MCPP006 no longer report "most names are X" when two styles tie. With no majority they report nothing.
- `--timeout N` now stops after about N seconds for a stdio server that does not exit (the SDK waited 5 more seconds before killing it; the grace period is now 500 ms), and the error no longer ends with the runtime's "A task was canceled.". A stdio server that needs more than 500 ms to exit after its stdin closes is now killed instead of waited for.
- HTTP connection errors are short and say what to try. A pinned `--transport sse` or `streamable-http` that gets a 400, 404 or 405 says the URL did not answer as that transport and points at the other one. A 401 shows the server's `WWW-Authenticate` challenge and says to pass `--header 'Authorization: Bearer <token>'`, or that the credentials in `--header` were rejected. Other response bodies are shown as one line of at most 200 characters with HTML tags removed, instead of a whole page. Exit codes are unchanged.

## [0.3.0-preview] - 2026-11-28

### Added

- GitHub Action (`uses: ShmueLior/mcpprobe@v0.3.0-preview`) and an Azure Pipelines step template that install a pinned `McpProbe.Cli` and run `diff` and `lint`. Reports go to the job summary.
- `mcpprobe --transport auto|streamable-http|sse` pins the HTTP transport when using `--http`. The default (`auto`) is unchanged: try Streamable HTTP, then fall back to legacy SSE.
- `LintOptions.Strict` and `mcpprobe lint --strict`: schema-portability rules MCPP009-MCPP012 (bare boolean schemas, type unions, untyped schemas and remote `$ref`s), ported from MCP Inspector. Off by default. These findings fail the report only when their effective severity is Error. MCPP008 is reserved for a community issue and is not included.
- Snapshot diffs classify `format`, `uniqueItems`, `const`, `additionalProperties`, `unevaluatedProperties`, `additionalItems` and `unevaluatedItems` as `ConstraintChanged`, with the same rule as other constraints. When both sides give `additionalProperties` (or a sibling) as a schema, the two schemas are compared like nested ones.
- Quality lint rules, on by default as warnings: MCPP013 (destructive hint contradicts `readOnlyHint` or a `delete_*`-style name), MCPP014 (description promises structured results but there is no `outputSchema`), MCPP015 (description only restates the name or title) and MCPP016 (empty `enum`, or `enum` without `type`). New `LintOptions.DestructiveNameTokens` and `LintOptions.StructuredOutputCues`.

### Changed

- Default `lint` can now fail on MCPP013-MCPP016 findings, in the CLI and in the GitHub Action and Azure Pipelines template (which run `lint` with `fail-on: warning` by default). To get exactly the 0.2 behavior back, turn the four rules off with `--rule MCPP013=none --rule MCPP014=none --rule MCPP015=none --rule MCPP016=none`. `--fail-on error` (Action: `fail-on: error`, Azure Pipelines: `failOn: error`) is a looser option: MCPP001-MCPP007 warnings stop failing too. A description that only restates the tool name reports MCPP015 instead of MCPP002.
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
