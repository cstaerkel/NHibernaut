#!/bin/sh
set -e
systemctl daemon-reload >/dev/null 2>&1 || true
echo "NHibernaut Dashboard installed."
echo "Set NHIBERNAUT_AUTH_TOKEN in /etc/nhibernaut-dashboard/dashboard.env, then:"
echo "  sudo systemctl enable --now nhibernaut-dashboard"
