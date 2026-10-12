#!/usr/bin/env bash
# Renders an nginx site template with this machine's settings and prints it.
#
#   render-nginx.sh <machine.env> tsic-statics
#   render-nginx.sh <machine.env> tsic-app <environment>
#   render-nginx.sh <machine.env> tsic-api <environment>
#
#   render-nginx.sh ~/.config/tsic/machine.env tsic-app staging \
#       | sudo tee /etc/nginx/sites-available/tsic-app-staging
#
# Only the placeholders listed in $vars are substituted, so nginx variables such
# as $uri are left alone. setup/install-server.sh calls this for every site.
set -euo pipefail

if [[ $# -lt 2 || $# -gt 3 ]]; then
    echo "usage: $(basename "$0") <machine.env> <site> [environment]" >&2
    exit 2
fi

env_file=$1
site=$2
app_env=${3:-}
template="$(dirname "$(readlink -f "$0")")/nginx/${site}.conf.template"

[[ -r $env_file ]] || { echo "settings file not readable: $env_file" >&2; exit 1; }
[[ -r $template ]] || { echo "no template for site '$site': $template" >&2; exit 1; }

set -a
# shellcheck disable=SC1090
source "$env_file"
set +a

names=(STATICS_ROOT STATICS_LISTEN STATICS_SERVER_NAME)

if [[ $site == tsic-app || $site == tsic-api ]]; then
    [[ -n $app_env ]] || { echo "site '$site' needs an environment name" >&2; exit 2; }
    suffix=${app_env^^}
    if [[ $site == tsic-app ]]; then
        names=(APP_ENV APP_LISTEN APP_SERVER_NAME TLS_CERT TLS_KEY)
    else
        names=(API_LISTEN API_SERVER_NAME API_PORT TLS_CERT TLS_KEY)
    fi
    export APP_ENV=$app_env
    # Per-environment settings are stored as NAME_<ENVIRONMENT> in machine.env.
    for base in "${names[@]}"; do
        [[ $base == APP_ENV ]] && continue
        per_env="${base}_${suffix}"
        [[ -n ${!per_env:-} ]] || { echo "$per_env is not set in $env_file" >&2; exit 1; }
        export "$base=${!per_env}"
    done
fi

vars=""
for name in "${names[@]}"; do
    [[ -n ${!name:-} ]] || { echo "$name is not set in $env_file" >&2; exit 1; }
    vars+="\${$name} "
done

envsubst "$vars" < "$template"
