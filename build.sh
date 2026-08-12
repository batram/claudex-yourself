#!/bin/sh
set -eu

root=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
runtime=${1:-}
explicit_runtime=$runtime
if [ -z "$runtime" ]; then
  case "$(uname -m)" in
    arm64|aarch64) runtime=osx-arm64 ;;
    *) runtime=osx-x64 ;;
  esac
fi

if [ -n "$explicit_runtime" ]; then output="$root/bin/$runtime"; else output="$root/bin"; fi
dotnet publish "$root/ClaudexYourself.csproj" -c Release -r "$runtime" --self-contained false -o "$output"
printf 'Built %s\n' "$output"
