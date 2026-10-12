#!/usr/bin/env bash
# Sets up an Ubuntu 24.04 machine to host TSIC: packages, service account, folders,
# Statics permissions, secrets file, certificate, service definition, nginx sites
# and firewall rules.
#
#   sudo install-server.sh <machine.env>
#   sudo scripts/Ubuntu-Config/setup/install-server.sh ~/.config/tsic/machine.env
#
# Ubuntu equivalent of scripts/IIS-Config-{Dev,Prod}/Setup/Run-Full-Setup.ps1.
# Safe to run again: every step checks what is already there, and it never
# overwrites a secrets file, a certificate or the login key ring.
#
# It does not build or publish the app (publish.sh does), and it does not install
# a database.
set -euo pipefail

[[ $# -eq 1 ]] || { echo "usage: sudo $(basename "$0") <machine.env>" >&2; exit 2; }
[[ $EUID -eq 0 ]] || { echo "run with sudo" >&2; exit 1; }

env_file=$(readlink -f "$1")
[[ -r $env_file ]] || { echo "settings file not readable: $env_file" >&2; exit 1; }
# shellcheck disable=SC1090
source "$env_file"

cfg=$(dirname "$(readlink -f "$0")")/..
cfg=$(readlink -f "$cfg")

: "${SERVICE_USER:?SERVICE_USER is not set}" "${DEPLOY_USER:?DEPLOY_USER is not set}"
: "${APP_ENVIRONMENTS:?APP_ENVIRONMENTS is not set}" "${STATICS_ROOT:?STATICS_ROOT is not set}"
id "$DEPLOY_USER" >/dev/null 2>&1 || { echo "deploy account '$DEPLOY_USER' does not exist" >&2; exit 1; }

step() { printf '\n== %s\n' "$*"; }
setting() { local name="$1_${2^^}"; printf '%s' "${!name:?$name is not set in $env_file}"; }

# --- 1. Packages ---------------------------------------------------------------
step "1. Packages"
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq nginx acl rsync openssl curl gettext-base aspnetcore-runtime-10.0
echo "nginx $(nginx -v 2>&1 | sed 's/.*\///'), ASP.NET runtime $(dotnet --list-runtimes | awk '/AspNetCore.App 10/ {print $2}' | tail -1)"

# --- 2. Service account --------------------------------------------------------
step "2. Service account '$SERVICE_USER'"
if id "$SERVICE_USER" >/dev/null 2>&1; then
    echo "exists"
else
    # A system account: no password, no login shell, cannot be signed in to.
    useradd --system --user-group --home-dir /var/lib/tsic --no-create-home \
            --shell /usr/sbin/nologin "$SERVICE_USER"
    echo "created"
fi

# --- 3. Folders ----------------------------------------------------------------
step "3. Folders"
install -d -o root -g root -m 755 /etc/tsic /etc/tsic/tls /opt/tsic
install -d -o "$SERVICE_USER" -g "$SERVICE_USER" -m 750 /var/lib/tsic
for e in $APP_ENVIRONMENTS; do
    # Published builds: written by the deploy account, read by the service and nginx.
    install -d -o "$DEPLOY_USER" -g "$DEPLOY_USER" -m 755 "/opt/tsic/$e" "/opt/tsic/$e/api" "/opt/tsic/$e/web"
    # Data the service writes, kept outside the published folder so a publish
    # can never remove it.
    install -d -o "$SERVICE_USER" -g "$SERVICE_USER" -m 700 \
        "/var/lib/tsic/$e" "/var/lib/tsic/$e/keys" "/var/lib/tsic/$e/AdnMonthEnd"
    # The app keeps its login key ring in <app folder>/keys (Program.cs). Point
    # that at the durable folder.
    if [[ ! -L /opt/tsic/$e/api/keys ]]; then
        [[ ! -e /opt/tsic/$e/api/keys ]] || { echo "/opt/tsic/$e/api/keys exists and is not a link; move its contents to /var/lib/tsic/$e/keys first" >&2; exit 1; }
        ln -s "/var/lib/tsic/$e/keys" "/opt/tsic/$e/api/keys"
    fi
    echo "/opt/tsic/$e  /var/lib/tsic/$e"
done

# --- 4. Statics folder ---------------------------------------------------------
step "4. Statics folder $STATICS_ROOT"
install -d -o root -g root -m 755 "$(dirname "$STATICS_ROOT")"
mkdir -p "$STATICS_ROOT"
# Service account owns and writes; nginx (group www-data) reads; nobody else.
chown -R "$SERVICE_USER":www-data "$STATICS_ROOT"
chmod -R u=rwX,g=rX,o= "$STATICS_ROOT"
# New files inherit the www-data group, so nginx can read fresh uploads.
find "$STATICS_ROOT" -type d -exec chmod g+s {} +
if [[ -n ${STATICS_EXTRA_WRITER:-} ]]; then
    id "$STATICS_EXTRA_WRITER" >/dev/null 2>&1 || { echo "STATICS_EXTRA_WRITER '$STATICS_EXTRA_WRITER' does not exist" >&2; exit 1; }
    # Development machine: a person also runs the API. Both accounts may write,
    # including to files the other one created.
    for who in "$STATICS_EXTRA_WRITER" "$SERVICE_USER"; do
        setfacl -R -m "u:$who:rwX" "$STATICS_ROOT"
        find "$STATICS_ROOT" -type d -exec setfacl -d -m "u:$who:rwX" {} +
    done
    echo "owner $SERVICE_USER, group www-data, extra writer $STATICS_EXTRA_WRITER"
else
    echo "owner $SERVICE_USER, group www-data"
fi

# --- 5. Secrets files ----------------------------------------------------------
step "5. Secrets files"
needs_secrets=()
for e in $APP_ENVIRONMENTS; do
    dest="/etc/tsic/$e.env"
    staged="$(dirname "$env_file")/$e.secrets.env"
    if [[ -f $dest ]]; then
        echo "$dest exists, left alone"
    elif [[ -f $staged ]]; then
        install -o root -g "$SERVICE_USER" -m 640 "$staged" "$dest"
        echo "$dest installed from $staged"
    else
        install -o root -g "$SERVICE_USER" -m 640 "$cfg/secrets.env.example" "$dest"
        needs_secrets+=("$dest")
        echo "$dest created from the example: FILL IT IN before starting the service"
    fi
    chown root:"$SERVICE_USER" "$dest"; chmod 640 "$dest"
done

# --- 6. Certificates -----------------------------------------------------------
step "6. Certificates"
for e in $APP_ENVIRONMENTS; do
    crt=$(setting TLS_CERT "$e"); key=$(setting TLS_KEY "$e")
    if [[ -f $crt && -f $key ]]; then
        echo "$crt exists"
    elif [[ $crt == /etc/tsic/tls/* ]]; then
        # Lab only: a self-signed certificate. Browsers will warn about it.
        openssl req -x509 -newkey rsa:2048 -nodes -days 825 \
            -keyout "$key" -out "$crt" -subj "/CN=localhost" \
            -addext "subjectAltName=DNS:localhost,IP:127.0.0.1" 2>/dev/null
        chmod 600 "$key"; chmod 644 "$crt"
        echo "$crt self-signed certificate generated"
    else
        echo "certificate $crt is missing; obtain it before running this script" >&2
        exit 1
    fi
done

# --- 7. Service definition -----------------------------------------------------
step "7. Service definition"
SERVICE_USER="$SERVICE_USER" envsubst '${SERVICE_USER}' \
    < "$cfg/systemd/tsic-api@.service" > /etc/systemd/system/tsic-api@.service
systemctl daemon-reload
for e in $APP_ENVIRONMENTS; do
    # "enable" = start at boot. Starting now waits for the first publish.
    systemctl enable "tsic-api@$e" >/dev/null 2>&1
    echo "tsic-api@$e enabled"
done

# --- 8. Let the deploy account restart the service -----------------------------
step "8. Restart permission for '$DEPLOY_USER'"
rule=$(mktemp)
{
    echo "# Written by scripts/Ubuntu-Config/setup/install-server.sh"
    for e in $APP_ENVIRONMENTS; do
        for verb in restart start stop; do
            echo "$DEPLOY_USER ALL=(root) NOPASSWD: /usr/bin/systemctl $verb tsic-api@$e"
        done
    done
} > "$rule"
# A broken sudoers file locks everyone out of sudo, so check it before installing.
visudo -cf "$rule" >/dev/null
install -o root -g root -m 440 "$rule" /etc/sudoers.d/tsic-deploy
rm -f "$rule"
echo "/etc/sudoers.d/tsic-deploy"

# --- 9. nginx sites ------------------------------------------------------------
step "9. nginx sites (Statics, and an app site and an API site per environment)"
install -o root -g root -m 644 "$cfg/nginx/tsic-security-headers.conf" /etc/nginx/snippets/tsic-security-headers.conf
"$cfg/render-nginx.sh" "$env_file" tsic-statics > /etc/nginx/sites-available/tsic-statics
ln -sfn /etc/nginx/sites-available/tsic-statics /etc/nginx/sites-enabled/tsic-statics
for e in $APP_ENVIRONMENTS; do
    for site in tsic-app tsic-api; do
        "$cfg/render-nginx.sh" "$env_file" "$site" "$e" > "/etc/nginx/sites-available/$site-$e"
        ln -sfn "/etc/nginx/sites-available/$site-$e" "/etc/nginx/sites-enabled/$site-$e"
        echo "$site-$e"
    done
done
# The "Welcome to nginx" placeholder.
rm -f /etc/nginx/sites-enabled/default
nginx -t
systemctl reload nginx

# --- 10. Firewall ----------------------------------------------------------------
# Ubuntu equivalent of IIS-Config-*/Setup/06-Configure-Firewall.ps1. Opens the
# ports the sites listen on, unless they listen only on this machine (loopback),
# and only if the firewall (ufw) is switched on. It never switches it on or off.
step "10. Firewall"
if ufw status 2>/dev/null | grep -q '^Status: active'; then
    listens=("$STATICS_LISTEN")
    for e in $APP_ENVIRONMENTS; do
        listens+=("$(setting APP_LISTEN "$e")" "$(setting API_LISTEN "$e")")
    done
    opened=0
    for l in "${listens[@]}"; do
        port=${l##*:}
        addr=${l%:*}; [[ $addr == "$l" ]] && addr=""
        if [[ $addr == 127.* || $addr == localhost || $addr == "[::1]" ]]; then
            continue
        fi
        if [[ -n ${FIREWALL_ALLOW_FROM:-} ]]; then
            ufw allow from "$FIREWALL_ALLOW_FROM" to any port "$port" proto tcp >/dev/null
            echo "port $port open to $FIREWALL_ALLOW_FROM"
        else
            ufw allow "$port/tcp" >/dev/null
            echo "port $port open to anyone"
        fi
        opened=$((opened + 1))
    done
    [[ $opened -gt 0 ]] || echo "sites listen on this machine only; nothing to open"
else
    echo "ufw is not active; nothing changed"
fi

# --- Done ------------------------------------------------------------------------
step "Done"
if [[ ${#needs_secrets[@]} -gt 0 ]]; then
    echo "Fill in before publishing: ${needs_secrets[*]}"
fi
echo "Next, as $DEPLOY_USER:  scripts/Ubuntu-Config/publish.sh $env_file <environment>"
