#!/usr/bin/env bash
# Post-deploy smoke test. Usage: tools/smoke/smoke.sh <api-base-url> [web-base-url]
#   e.g. tools/smoke/smoke.sh https://api.example.com https://app.example.com
# Exits non-zero on the first failed check.
set -u
api="${1:?api base url required}"; web="${2:-}"
failures=0

check() { # name, expected status, url
  local status
  status=$(curl -s -o /tmp/smoke-body -w '%{http_code}' --max-time 20 "$3")
  if [ "$status" = "$2" ]; then echo "ok    $1 ($status)"; else
    echo "FAIL  $1: expected $2, got $status"; head -c 300 /tmp/smoke-body; echo; failures=$((failures + 1)); fi
}

check "API process is up"                         200 "$api/health"
check "database reachable and fully migrated"     200 "$api/health/ready"
check "documents need sign-in"                    401 "$api/api/documents"
if [ -n "$web" ]; then
  check "web app loads"                           200 "$web/"
  grep -qi "<app-root" /tmp/smoke-body || { echo "FAIL  web app: page has no <app-root>"; failures=$((failures + 1)); }
fi

[ "$failures" -eq 0 ] && echo "All smoke checks passed." || echo "$failures smoke check(s) failed."
exit "$failures"
