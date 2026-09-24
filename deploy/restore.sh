#!/bin/sh
# Restauration : ./deploy/restore.sh backups/kokora-AAAA-MM-JJ_HHMM.dump [backups/fichiers-AAAA-MM-JJ_HHMM.tar.gz]
# ATTENTION : remplace la base (et les fichiers) actuels.
set -eu
cd "$(dirname "$0")/.."
DUMP="$1"
FILES="${2:-}"
printf "Remplacer les données actuelles par %s ? (oui/non) " "$DUMP"
read -r answer
[ "$answer" = "oui" ] || { echo "Annulé."; exit 1; }

docker compose stop app
docker compose exec -T db pg_restore -U kokora -d kokora --clean --if-exists --no-owner < "$DUMP"
if [ -n "$FILES" ]; then
  docker compose run --rm --no-deps --entrypoint sh app -c "rm -rf /data/* && tar xzf - -C /data" < "$FILES"
fi
docker compose start app
echo "Restauration terminée."
