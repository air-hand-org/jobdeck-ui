#!/usr/bin/env bash
set -euo pipefail
root=${1:?Usage: check-browser-assets.sh PUBLISH_DIRECTORY}
framework="$root/wwwroot/_framework"
test -d "$framework"
client_found=false
for file in "$framework"/*; do
  name=${file##*/}
  case "$name" in
    ui.Client.*.wasm) client_found=true ;;
    Jobdeck.Job.*) ;;
    ui.*.wasm|Jobdeck.*|Grpc.AspNetCore.*|Npgsql.*|OpenIddict.*|Microsoft.EntityFrameworkCore.*)
      printf 'Server-only assembly in browser assets: %s\n' "$name" >&2
      exit 1 ;;
  esac
done
"$client_found"
for file in "$root/wwwroot"/appsettings*.json "$root/wwwroot"/*.pfx "$root/wwwroot"/*.pem; do
  if test -f "$file"; then
    printf 'Server configuration or certificate in browser assets\n' >&2
    exit 1
  fi
done
printf 'Browser assets contain the WASM client and no server-only assemblies/configuration.\n'
