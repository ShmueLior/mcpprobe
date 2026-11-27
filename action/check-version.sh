#!/usr/bin/env bash
# Fail if the Action's default version input does not match the project Version.
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
action_yml="$root/action.yml"

action_version=$(awk '
  $0 == "  version:" { in_ver = 1; next }
  in_ver && $1 == "default:" {
    v = $2
    gsub(/['\''"]/, "", v)
    print v
    exit
  }
  in_ver && /^  [a-zA-Z0-9_-]+:/ { exit }
' "$action_yml")

if [[ -z "$action_version" ]]; then
  echo "Could not read inputs.version.default from $action_yml."
  exit 1
fi

project_version=$(dotnet msbuild "$root/src/McpProbe/McpProbe.csproj" -getProperty:Version -nologo | tr -d '[:space:]')
if [[ -z "$project_version" ]]; then
  echo "Could not read the project Version from McpProbe.csproj."
  exit 1
fi

if [[ "$action_version" != "$project_version" ]]; then
  echo "Action default version '$action_version' does not match project version '$project_version'."
  echo "Keep action.yml inputs.version.default in sync when you bump the version. See CONTRIBUTING.md."
  exit 1
fi

echo "Action default version $action_version matches the project version."
