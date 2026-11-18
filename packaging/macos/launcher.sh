#!/bin/sh
set -eu

resources="$(dirname "$0")/../Resources"
if [ "$(/usr/sbin/sysctl -in hw.optional.arm64)" = 1 ]; then
    arch=arm64
else
    arch=x64
fi
exec "$resources/osx-$arch/PhoneBoxAgent" "$@"
