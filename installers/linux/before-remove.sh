#!/bin/sh
set -e
# Runs as the deb prerm / rpm %preun hook. During an upgrade this also fires for
# the OLD package, so only stop + disable on a genuine removal:
#   deb passes "remove"; rpm passes the remaining-version count ("0" = final removal).
if [ "$1" = "remove" ] || [ "$1" = "0" ]; then
    systemctl disable --now nhibernaut-dashboard >/dev/null 2>&1 || true
fi
