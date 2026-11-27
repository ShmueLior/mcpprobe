#!/usr/bin/env bash
# Shared runner for the GitHub Action and the Azure Pipelines template.
# Reads MCPPROBE_* environment variables, installs a pinned McpProbe.Cli,
# runs diff and/or lint, and writes a job summary.
set -euo pipefail

# GitHub summaries are capped at 1024 KiB per step. Keep well under that.
SUMMARY_REPORT_LIMIT=204800

conclusion=error
diff_exit=
lint_exit=
ado_summary_file=

# shellcheck disable=SC2317 # invoked from the EXIT trap
write_github_outputs() {
  if [[ -z "${GITHUB_OUTPUT:-}" ]]; then
    return 0
  fi
  {
    printf 'conclusion=%s\n' "$conclusion"
    printf 'diff-exit=%s\n' "$diff_exit"
    printf 'lint-exit=%s\n' "$lint_exit"
  } >> "$GITHUB_OUTPUT" || true
}

# shellcheck disable=SC2317 # invoked from the EXIT trap
upload_ado_summary() {
  if [[ -n "${TF_BUILD:-}" && -n "${ado_summary_file:-}" && -s "$ado_summary_file" ]]; then
    # The logging command needs an absolute path and its own line.
    printf '##vso[task.uploadsummary]%s\n' "$(cd "$(dirname "$ado_summary_file")" && pwd)/$(basename "$ado_summary_file")"
  fi
}

# shellcheck disable=SC2317 # invoked from the EXIT trap
on_exit() {
  local status=$?
  trap - EXIT
  write_github_outputs
  upload_ado_summary
  exit "$status"
}

trap 'on_exit' EXIT

die() {
  printf 'error: %s\n' "$*" >&2
  append_summary "McpProbe" "error: $*"
  conclusion=error
  exit 2
}

is_true() {
  case "${1:-}" in
    true|True|TRUE|yes|YES|y|Y|1) return 0 ;;
    *) return 1 ;;
  esac
}

# Read the stamped default from action.yml at the repository root.
default_version_from_action_yml() {
  awk '
    $0 == "  version:" { in_ver = 1; next }
    in_ver && $1 == "default:" {
      v = $2
      gsub(/['\''"]/, "", v)
      print v
      exit
    }
    in_ver && /^  [a-zA-Z0-9_-]+:/ { exit }
  ' "$1"
}

truncate_text() {
  local text=$1
  local bytes
  bytes=$(printf '%s' "$text" | wc -c | tr -d ' ')
  if [ "$bytes" -le "$SUMMARY_REPORT_LIMIT" ]; then
    printf '%s' "$text"
    return 0
  fi
  printf '%s\n\n[truncated after %s bytes]\n' "$(printf '%s' "$text" | head -c "$SUMMARY_REPORT_LIMIT")" "$SUMMARY_REPORT_LIMIT"
}

append_summary() {
  local title=$1
  local body=$2
  local tmp
  tmp=$(mktemp "${TMPDIR:-/tmp}/mcpprobe-md.XXXXXX")
  {
    printf '## %s\n\n' "$title"
    printf '%s\n' '```'
    printf '%s\n' "$(truncate_text "$body")"
    printf '%s\n' '```'
  } >"$tmp"
  if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
    cat "$tmp" >> "$GITHUB_STEP_SUMMARY" || true
  fi
  if [[ -n "${TF_BUILD:-}" ]]; then
    if [[ -z "${ado_summary_file:-}" ]]; then
      ado_summary_file="${RUNNER_TEMP:-${AGENT_TEMPDIRECTORY:-${TMPDIR:-/tmp}}}/mcpprobe-summary-$$.md"
    fi
    cat "$tmp" >> "$ado_summary_file" || true
  fi
  rm -f "$tmp"
}

# Print non-empty lines from $1, stripping a trailing CR.
each_nonempty_line() {
  local line
  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line%$'\r'}"
    [[ -z "$line" ]] && continue
    printf '%s\n' "$line"
  done <<END_LINES
${1:-}
END_LINES
}

count_sources=0
[[ -n "${MCPPROBE_STDIO:-}" ]] && count_sources=$((count_sources + 1))
[[ -n "${MCPPROBE_HTTP:-}" ]] && count_sources=$((count_sources + 1))
[[ -n "${MCPPROBE_FROM_FILE:-}" ]] && count_sources=$((count_sources + 1))
if [ "$count_sources" -ne 1 ]; then
  die "Pass exactly one of stdio, http, or from-file."
fi

run_diff=false
run_lint=false
is_true "${MCPPROBE_DIFF:-true}" && run_diff=true
is_true "${MCPPROBE_LINT:-true}" && run_lint=true
if [[ "$run_diff" != true && "$run_lint" != true ]]; then
  die "Enable at least one of diff or lint."
fi

version="${MCPPROBE_VERSION:-}"
if [[ -z "$version" ]]; then
  script_dir=$(cd "$(dirname "$0")" && pwd)
  if [[ -f "$script_dir/../action.yml" ]]; then
    version=$(default_version_from_action_yml "$script_dir/../action.yml" || true)
  fi
fi
# Accept a leading v, matching the repo's v* tags.
version="${version#v}"
if [[ -z "$version" || "$version" == "latest" || "$version" == *"*"* ]]; then
  die "version must be an exact McpProbe.Cli version, not '${MCPPROBE_VERSION:-}'."
fi

if ! command -v dotnet >/dev/null 2>&1; then
  die "dotnet was not found on PATH. Run actions/setup-dotnet (or the Azure Pipelines UseDotNet task) first, with a .NET 8 or .NET 10 SDK."
fi

temp_root="${RUNNER_TEMP:-${AGENT_TEMPDIRECTORY:-${TMPDIR:-/tmp}}}"
tool_path="$temp_root/mcpprobe-cli-$$"
mkdir -p "$tool_path"

# An exact --version is enough for preview packages. --prerelease cannot be
# combined with --version on current SDKs.
install_cmd=(dotnet tool install McpProbe.Cli --tool-path "$tool_path" --version "$version")
if [[ -n "${MCPPROBE_SOURCE:-}" ]]; then
  source_arg="$MCPPROBE_SOURCE"
  if [[ "$source_arg" != *://* && -d "$source_arg" ]]; then
    source_arg=$(cd "$source_arg" && pwd)
  elif [[ "$source_arg" != *://* && -f "$source_arg" ]]; then
    # dotnet --source expects a folder of packages or a feed URL, not a nupkg path.
    source_arg=$(cd "$(dirname "$source_arg")" && pwd)
  elif [[ "$source_arg" != *://* ]]; then
    die "NuGet source not found: $MCPPROBE_SOURCE"
  fi
  # Exclusive --source so a just-packed nupkg is not shadowed by nuget.org.
  install_cmd+=(--source "$source_arg")
fi

printf 'Installing McpProbe.Cli %s\n' "$version"
if ! "${install_cmd[@]}"; then
  die "dotnet tool install McpProbe.Cli $version failed."
fi
export PATH="$tool_path:$PATH"
if ! command -v mcpprobe >/dev/null 2>&1; then
  die "mcpprobe was not found on PATH after installing McpProbe.Cli $version."
fi

common_args=()
if [[ -n "${MCPPROBE_STDIO:-}" ]]; then
  common_args+=(--stdio "$MCPPROBE_STDIO")
elif [[ -n "${MCPPROBE_HTTP:-}" ]]; then
  common_args+=(--http "$MCPPROBE_HTTP")
else
  common_args+=(--from-file "$MCPPROBE_FROM_FILE")
fi
[[ -n "${MCPPROBE_TIMEOUT:-}" ]] && common_args+=(--timeout "$MCPPROBE_TIMEOUT")
[[ -n "${MCPPROBE_CWD:-}" ]] && common_args+=(--cwd "$MCPPROBE_CWD")
while IFS= read -r line; do
  common_args+=(--header "$line")
done < <(each_nonempty_line "${MCPPROBE_HEADER:-}")
while IFS= read -r line; do
  common_args+=(--env "$line")
done < <(each_nonempty_line "${MCPPROBE_ENV:-}")

run_mcpprobe() {
  local title=$1
  shift
  local stdout_file stderr_file status body
  stdout_file=$(mktemp "$temp_root/mcpprobe-out.XXXXXX")
  stderr_file=$(mktemp "$temp_root/mcpprobe-err.XXXXXX")
  set +e
  mcpprobe "$@" >"$stdout_file" 2>"$stderr_file"
  status=$?
  cat "$stdout_file"
  cat "$stderr_file" >&2
  body=$(cat "$stdout_file")
  if [[ -s "$stderr_file" ]]; then
    if [[ -n "$body" ]]; then
      body="$body"$'\n\n'"$(cat "$stderr_file")"
    else
      body=$(cat "$stderr_file")
    fi
  fi
  [[ -n "$body" ]] || body="(no output, exit $status)"
  append_summary "$title" "$body"
  rm -f "$stdout_file" "$stderr_file"
  return "$status"
}

if [[ "$run_diff" == true ]]; then
  diff_args=(diff --baseline "${MCPPROBE_BASELINE:-mcp-tools.json}" "${common_args[@]}")
  is_true "${MCPPROBE_FAIL_ON_NON_BREAKING:-}" && diff_args+=(--fail-on-non-breaking)
  diff_exit=0
  run_mcpprobe "McpProbe diff" "${diff_args[@]}" || diff_exit=$?
fi

if [[ "$run_lint" == true ]]; then
  lint_args=(lint "${common_args[@]}")
  [[ -n "${MCPPROBE_FAIL_ON:-}" ]] && lint_args+=(--fail-on "$MCPPROBE_FAIL_ON")
  is_true "${MCPPROBE_NESTED:-}" && lint_args+=(--nested)
  [[ -n "${MCPPROBE_MIN_DESCRIPTION_LENGTH:-}" ]] && lint_args+=(--min-description-length "$MCPPROBE_MIN_DESCRIPTION_LENGTH")
  while IFS= read -r line; do
    lint_args+=(--rule "$line")
  done < <(each_nonempty_line "${MCPPROBE_RULE:-}")
  lint_exit=0
  run_mcpprobe "McpProbe lint" "${lint_args[@]}" || lint_exit=$?
fi

set -e

final=0
for code in "$diff_exit" "$lint_exit"; do
  [[ -z "$code" ]] && continue
  if [ "$code" -eq 2 ]; then
    final=2
  elif [ "$final" -ne 2 ] && [ "$code" -gt "$final" ]; then
    final=$code
  fi
done

if [ "$final" -eq 0 ]; then
  conclusion=passed
elif [ "$final" -eq 1 ]; then
  conclusion=failed
else
  conclusion=error
  final=2
fi

exit "$final"
