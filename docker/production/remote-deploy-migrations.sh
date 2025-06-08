#!/usr/bin/env sh

set -euxo pipefail

cd ~/docker/christofel
docker compose cp $MIGPATH database:/tmp/$BUNDLE
docker compose exec database /bin/sh -c "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 /tmp/$BUNDLE"
docker compose exec database /bin/rm /tmp/$BUNDLE
rm /tmp/$BUNDLE
