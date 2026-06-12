#!/bin/sh
set -e
systemctl disable --now nhibernaut-dashboard >/dev/null 2>&1 || true
