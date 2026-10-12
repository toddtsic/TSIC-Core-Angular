#!/usr/bin/env bash
# Removes one environment from this machine: its service, nginx sites, published
# build, secrets file and lab certificate.
#
#   sudo remove-environment.sh <environment> [--purge-data]
#   sudo scripts/Ubuntu-Config/setup/remove-environment.sh staging
#
# Ubuntu equivalent of scripts/IIS-Config-{Dev,Prod}/Setup/00-Remove-Sites.ps1.
# install-server.sh only ever adds; use this to take an environment away, for
# example before giving its ports to another one.
#
# Kept unless --purge-data is given: /var/lib/tsic/<environment>, which holds the
# login key ring. Never touched: the Statics folder, the service account, other
# environments.
set -euo pipefail

[[ $# -ge 1 && $# -le 2 ]] || { echo "usage: sudo $(basename "$0") <environment> [--purge-data]" >&2; exit 2; }
[[ $EUID -eq 0 ]] || { echo "run with sudo" >&2; exit 1; }

e=$1
purge=false
[[ ${2:-} == --purge-data ]] && purge=true
[[ ${2:-} == "" || ${2:-} == --purge-data ]] || { echo "unknown option: $2" >&2; exit 2; }
# The name becomes part of paths that are removed below.
[[ $e =~ ^[a-z][a-z0-9-]*$ ]] || { echo "not a valid environment name: $e" >&2; exit 2; }

step() { printf '\n== %s\n' "$*"; }

step "1. Service tsic-api@$e"
systemctl disable --now "tsic-api@$e" >/dev/null 2>&1 && echo "stopped and disabled" || echo "was not running"

step "2. nginx sites"
for site in "tsic-app-$e" "tsic-api-$e"; do
    rm -f "/etc/nginx/sites-enabled/$site" "/etc/nginx/sites-available/$site"
    echo "$site removed"
done
if nginx -t 2>/dev/null; then
    systemctl reload nginx
else
    echo "nginx configuration does not pass its test; not reloaded" >&2
    nginx -t
fi

step "3. Published build, secrets, certificate"
rm -rf "/opt/tsic/$e"
rm -f "/etc/tsic/$e.env" "/etc/tsic/tls/$e.crt" "/etc/tsic/tls/$e.key"
echo "/opt/tsic/$e  /etc/tsic/$e.env  /etc/tsic/tls/$e.*"

step "4. Restart permission"
if [[ -f /etc/sudoers.d/tsic-deploy ]]; then
    tmp=$(mktemp)
    grep -v "tsic-api@$e\$" /etc/sudoers.d/tsic-deploy > "$tmp" || true
    visudo -cf "$tmp" >/dev/null
    install -o root -g root -m 440 "$tmp" /etc/sudoers.d/tsic-deploy
    rm -f "$tmp"
    echo "rules for tsic-api@$e removed"
fi

step "5. Service data /var/lib/tsic/$e"
if $purge; then
    rm -rf "/var/lib/tsic/$e"
    echo "removed (--purge-data)"
else
    echo "kept (login key ring); pass --purge-data to remove it"
fi

step "Done"
echo "Take '$e' out of APP_ENVIRONMENTS in this machine's settings file, or the next"
echo "install-server.sh run will create it again."
