#!/usr/bin/env bash
# Builds the API and the Angular app and publishes them to one environment on an
# Ubuntu server set up by setup/install-server.sh.
#
#   publish.sh <machine.env> <environment> [--build-only] [--no-build] [--skip-api] [--skip-web]
#   publish.sh ~/.config/tsic/machine.env staging
#
#   --build-only  build into publish/ubuntu/<environment> and stop
#   --no-build    publish what is already in that folder
#
# Ubuntu equivalent of scripts/1-Build-And-Deploy-Local.ps1. Run as the deploy
# account, never as root. The build happens here; the server only receives files.
#
# A machine.env may set DEPLOY_TARGET_<ENVIRONMENT>=user@host to publish to another
# machine over SSH. Without it the environment is on this machine.
set -euo pipefail

usage() { echo "usage: $(basename "$0") <machine.env> <environment> [--build-only] [--no-build] [--skip-api] [--skip-web]" >&2; exit 2; }
[[ $# -ge 2 ]] || usage

env_file=$1
app_env=$2
shift 2
build_only=false no_build=false do_api=true do_web=true
for arg in "$@"; do
    case $arg in
        --build-only) build_only=true ;;
        --no-build)   no_build=true ;;
        --skip-api)   do_api=false ;;
        --skip-web)   do_web=false ;;
        *) usage ;;
    esac
done

[[ $EUID -ne 0 ]] || { echo "run as the deploy account, not root" >&2; exit 1; }
[[ -r $env_file ]] || { echo "settings file not readable: $env_file" >&2; exit 1; }
# shellcheck disable=SC1090
source "$env_file"

suffix=${app_env^^}
ng_config_var="NG_CONFIG_${suffix}";      ng_config=${!ng_config_var:?$ng_config_var is not set in $env_file}
api_port_var="API_PORT_${suffix}";        api_port=${!api_port_var:?$api_port_var is not set in $env_file}
target_var="DEPLOY_TARGET_${suffix}";     target=${!target_var:-}
api_url_var="API_PUBLIC_URL_${suffix}";   api_url=${!api_url_var:?$api_url_var is not set in $env_file}
statics_url=${STATICS_PUBLIC_URL:?STATICS_PUBLIC_URL is not set in $env_file}

script_dir=$(dirname "$(readlink -f "$0")")
repo=$(readlink -f "$script_dir/../..")
api_proj="$repo/TSIC-Core-Angular/src/backend/TSIC.API/TSIC.API.csproj"
web_dir="$repo/TSIC-Core-Angular/src/frontend/tsic-app"
out="$repo/publish/ubuntu/$app_env"
live="/opt/tsic/$app_env"

step() { printf '\n== %s\n' "$*"; }
# Runs a command on the machine that hosts the environment.
on_target() { if [[ -n $target ]]; then ssh "$target" "$@"; else "$@"; fi; }
dest() { if [[ -n $target ]]; then printf '%s:%s' "$target" "$1"; else printf '%s' "$1"; fi; }

build_stamp="v$(date +%y%m%d.%H%M).$(git -C "$repo" rev-parse --short HEAD 2>/dev/null || echo unknown)"
echo "Publishing $app_env  build $build_stamp  target ${target:-this machine}"

if $no_build; then
    step "Steps 1-2 skipped: publishing the existing build in $out"
    if $do_api; then [[ -f $out/api/TSIC.API.dll ]] || { echo "no API build in $out/api" >&2; exit 1; }; fi
    if $do_web; then [[ -f $out/web/index.html ]]   || { echo "no web build in $out/web" >&2; exit 1; }; fi
fi

# --- Step 1: API -------------------------------------------------------------
if $do_api && ! $no_build; then
    step "Step 1: build the API"
    rm -rf "$out/api"
    # Framework-dependent and scoped to Linux, as the Windows script scopes to win-x64.
    dotnet publish "$api_proj" --configuration Release --output "$out/api" \
        -r linux-x64 --self-contained false --nologo -v quiet
    [[ -f $out/api/TSIC.API.dll ]] || { echo "publish produced no TSIC.API.dll" >&2; exit 1; }
    # IIS-only file; nothing on Linux reads it.
    rm -f "$out/api/web.config"
fi

# --- Step 2: Angular ---------------------------------------------------------
if $do_web && ! $no_build; then
    step "Step 2: build Angular (configuration=$ng_config)"
    # Node is installed with nvm, which only loads itself in interactive shells.
    if ! command -v npm >/dev/null 2>&1 && [[ -s $HOME/.nvm/nvm.sh ]]; then
        # shellcheck disable=SC1091
        source "$HOME/.nvm/nvm.sh"
    fi
    env_ts="$web_dir/src/environments/environment.$ng_config.ts"
    [[ -f $env_ts ]] || { echo "no environment file for '$ng_config': $env_ts" >&2; exit 1; }
    # Stamp the build version for this build only, then put the file back so git
    # stays clean (1b-Build-Angular.ps1 does the same).
    cp "$env_ts" "$env_ts.orig"
    trap 'mv -f "$env_ts.orig" "$env_ts"' EXIT
    sed -i -E "s/buildVersion:\s*'[^']*'/buildVersion: '$build_stamp'/" "$env_ts"
    (cd "$web_dir" && npx ng build --configuration "$ng_config")
    mv -f "$env_ts.orig" "$env_ts"
    trap - EXIT

    dist="$web_dir/dist/tsic-app/browser"
    [[ -f $dist/index.html ]] || dist="$web_dir/dist/tsic-app"
    [[ -f $dist/index.html ]] || { echo "Angular build produced no index.html" >&2; exit 1; }
    rm -rf "$out/web"
    mkdir -p "$out/web"
    cp -a "$dist/." "$out/web/"
    # The stamp the running app polls for, to notice a new build.
    printf '{"buildStamp":"%s"}' "$build_stamp" > "$out/web/version.json"
    # IIS-only files.
    find "$out/web" -name web.config -delete
fi

if $build_only; then
    rm -f "$out/web/app-config.json"
    step "Build only: output is in $out"
    exit 0
fi

# --- Step 3: copy to the live folders ----------------------------------------
# --delete makes live an exact copy of the build. keys/ holds the login key ring
# and must survive every publish, exactly as on IIS.
step "Step 3: copy to $(dest "$live")"
if $do_api; then
    rsync -a --delete --exclude '/keys' "$out/api/" "$(dest "$live/api/")"
fi
if $do_web; then
    # The addresses the target machine's browser clients use; main.ts reads the file
    # at start-up. Written here, not at build time, so one build can be published to
    # any machine.
    printf '{"apiUrl":"%s/api","staticsUrl":"%s"}' "${api_url%/}" "${statics_url%/}" > "$out/web/app-config.json"
    rsync -a --delete "$out/web/" "$(dest "$live/web/")"
fi

# --- Step 4: restart and check -------------------------------------------------
if $do_api; then
    step "Step 4: restart tsic-api@$app_env"
    on_target sudo -n /usr/bin/systemctl restart "tsic-api@$app_env"
    for _ in $(seq 1 60); do
        code=$(on_target curl -s -o /dev/null -m 2 -w '%{http_code}' "http://127.0.0.1:$api_port/" || true)
        [[ $code != 000 && -n $code ]] && break
        sleep 1
    done
    if [[ ${code:-000} == 000 ]]; then
        echo "API did not answer on port $api_port within 60s. Last log lines:" >&2
        on_target journalctl -u "tsic-api@$app_env" -n 40 --no-pager >&2 || true
        exit 1
    fi
    echo "API is answering on port $api_port (HTTP $code at /)."
fi

step "Published $app_env  build $build_stamp"
