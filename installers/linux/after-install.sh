#!/bin/sh
set -e
systemctl daemon-reload >/dev/null 2>&1 || true
# On upgrade the service is already running — restart it onto the new binary.
# (try-restart is a no-op on a fresh install where the service isn't active yet.)
if systemctl is-active --quiet nhibernaut-dashboard 2>/dev/null; then
    systemctl try-restart nhibernaut-dashboard >/dev/null 2>&1 || true
fi
echo "NHibernaut Dashboard installed."
echo "Set NHIBERNAUT_AUTH_TOKEN in /etc/nhibernaut-dashboard/dashboard.env, then:"
echo "  sudo systemctl enable --now nhibernaut-dashboard"
