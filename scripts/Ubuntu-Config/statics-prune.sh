#!/usr/bin/env bash
# Removes Statics folders that nothing in this stack reads or writes.
#
#   statics-prune.sh <machine.env>            report only: what would go, and how big
#   statics-prune.sh <machine.env> --apply    delete, after asking for confirmation
#   statics-prune.sh <machine.env> --apply --yes        delete without asking
#   statics-prune.sh <machine.env> --list <file> ...    use another list
#
# What to remove comes from statics-prune.list (one folder per line, relative to
# STATICS_ROOT, with the evidence beside it). What must never be removed is fixed
# in this script: the folders the API is configured to read and write. A line in
# the list that names one of those, or anything inside or above one, is refused.
#
# Every run with --apply writes a record of each file removed (size, date, path) to
# <STATICS_ROOT>/../statics-prune-<timestamp>.log, so there is a list to restore
# from backup if a folder turns out to be needed.
#
# Run as an account that can write to the Statics folder: the service account on
# a server (sudo -u tsic ...), or the extra writer on a development machine.
set -euo pipefail

usage() { sed -n '2,8p' "$0" | sed 's/^# \{0,1\}//' >&2; exit 2; }
[[ $# -ge 1 ]] || usage

env_file=$1; shift
apply=false assume_yes=false
list="$(dirname "$(readlink -f "$0")")/statics-prune.list"
while [[ $# -gt 0 ]]; do
    case $1 in
        --apply) apply=true ;;
        --yes)   assume_yes=true ;;
        --list)  shift; list=${1:?--list needs a file} ;;
        *) usage ;;
    esac
    shift
done

[[ -r $env_file ]] || { echo "settings file not readable: $env_file" >&2; exit 1; }
[[ -r $list ]]     || { echo "list not readable: $list" >&2; exit 1; }
# shellcheck disable=SC1090
source "$env_file"
root=$(readlink -f "${STATICS_ROOT:?STATICS_ROOT is not set in $env_file}")
[[ -d $root && $root != / ]] || { echo "STATICS_ROOT is not a usable folder: $root" >&2; exit 1; }

# The folders the API reads and writes (FileStorage:* in appsettings). Fixed here on
# purpose: the list file can be edited freely, this cannot be edited by accident.
protected=(
    "BannerFiles"
    "Headshots-AllRegistrants"
    "Store-Sku-Images"
    "RegFileUploads/MedForms"
)

# True when $1 is, is inside, or contains a protected folder.
touches_protected() {
    local rel=$1 p
    for p in "${protected[@]}"; do
        [[ $rel == "$p" || $rel == "$p"/* || $p == "$rel"/* ]] && return 0
    done
    return 1
}

human() { numfmt --to=iec --suffix=B "$1"; }

targets=() total_files=0 total_bytes=0 problems=0
printf '%-34s %9s %9s  %-10s  %s\n' "folder" "files" "size" "newest" "status"
while IFS= read -r line || [[ -n $line ]]; do
    rel=${line%%#*}                                   # drop the evidence comment
    rel=$(printf '%s' "$rel" | sed -E 's/^[[:space:]]+|[[:space:]]+$//g; s:/+$::')
    [[ -n $rel ]] || continue

    if [[ $rel == /* || $rel == *..* ]]; then
        printf '%-34s %9s %9s  %-10s  %s\n' "$rel" - - - "REFUSED: must be a plain path inside Statics"
        problems=$((problems + 1)); continue
    fi
    if touches_protected "$rel"; then
        printf '%-34s %9s %9s  %-10s  %s\n' "$rel" - - - "REFUSED: protected, the API uses it"
        problems=$((problems + 1)); continue
    fi
    path="$root/$rel"
    if [[ ! -e $path ]]; then
        printf '%-34s %9s %9s  %-10s  %s\n' "$rel" 0 - - "already gone"
        continue
    fi
    # Resolve links and confirm the result is still inside Statics.
    real=$(readlink -f "$path")
    if [[ $real != "$root"/* ]]; then
        printf '%-34s %9s %9s  %-10s  %s\n' "$rel" - - - "REFUSED: resolves outside Statics"
        problems=$((problems + 1)); continue
    fi
    read -r files bytes newest < <(find "$real" -type f -printf '%s %T@\n' \
        | awk '{n++; b+=$1; if ($2>m) m=$2} END {printf "%d %d %d\n", n, b, m}')
    newest_s=$([[ $files -gt 0 ]] && date -u -d "@$newest" +%Y-%m-%d || echo -)
    printf '%-34s %9s %9s  %-10s  %s\n' "$rel" "$files" "$(human "$bytes")" "$newest_s" "$($apply && echo remove || echo "would remove")"
    targets+=("$real")
    total_files=$((total_files + files)); total_bytes=$((total_bytes + bytes))
done < "$list"

printf '%-34s %9s %9s\n' "TOTAL" "$total_files" "$(human "$total_bytes")"
printf 'Statics now: %s, disk free: %s\n' "$(du -sh "$root" | cut -f1)" "$(df -h --output=avail "$root" | tail -1 | tr -d ' ')"

if [[ $problems -gt 0 ]]; then
    echo "$problems line(s) in $list were refused. Fix the list; nothing was removed." >&2
    exit 1
fi
if ! $apply; then
    echo "Report only. Run again with --apply to remove."
    exit 0
fi
[[ ${#targets[@]} -gt 0 ]] || { echo "Nothing to remove."; exit 0; }

if ! $assume_yes; then
    read -r -p "Remove $total_files files ($(human "$total_bytes")) from $root? Type 'prune' to continue: " answer
    [[ $answer == prune ]] || { echo "Cancelled; nothing was removed."; exit 1; }
fi

log="$(dirname "$root")/statics-prune-$(date -u +%Y%m%d-%H%M%S).log"
[[ -w $(dirname "$root") ]] || log="$HOME/statics-prune-$(date -u +%Y%m%d-%H%M%S).log"
{
    echo "# statics-prune $(date -u +%FT%TZ)  root=$root  list=$list"
    echo "# bytes  modified(UTC)  path"
} > "$log"
for t in "${targets[@]}"; do
    find "$t" -type f -printf '%s  %TY-%Tm-%Td  %p\n' >> "$log"
    rm -rf -- "$t"
    echo "removed ${t#"$root"/}"
done
echo "Record of removed files: $log"
printf 'Statics now: %s, disk free: %s\n' "$(du -sh "$root" | cut -f1)" "$(df -h --output=avail "$root" | tail -1 | tr -d ' ')"
