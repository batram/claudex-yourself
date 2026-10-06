#!/bin/sh
set -eu

root=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
runtime=${1:-}
explicit_runtime=$runtime
if [ -z "$runtime" ]; then
  case "$(uname -s)" in
    Darwin) platform=osx ;;
    Linux) platform=linux ;;
    *) printf '%s\n' 'Unsupported host OS; specify a runtime explicitly.' >&2; exit 1 ;;
  esac
  case "$(uname -m)" in
    arm64|aarch64) runtime=$platform-arm64 ;;
    x86_64|amd64) runtime=$platform-x64 ;;
    *) printf '%s\n' 'Unsupported host architecture; specify a runtime explicitly.' >&2; exit 1 ;;
  esac
fi

if [ -n "$explicit_runtime" ]; then output="$root/bin/$runtime"; else output="$root/bin"; fi
if ! command -v dotnet >/dev/null 2>&1; then
  printf '%s\n' 'The .NET 10 SDK is required. Install it and make sure dotnet is on PATH.' >&2
  exit 1
fi
dotnet publish "$root/ClaudexYourself.csproj" -c Release -r "$runtime" --self-contained false -o "$output"
printf 'Built %s\n' "$output"
