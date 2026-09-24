#!/bin/sh
# Sauvegarde quotidienne de Kokora (installation Docker) : base PostgreSQL + fichiers (/data : photos, logos, clés).
# Planification : crontab -e  puis  30 3 * * * /chemin/vers/kokora/deploy/backup.sh >> /var/log/kokora-backup.log 2>&1
set -eu
cd "$(dirname "$0")/.."
[ -f .env ] && . ./.env

DEST="${BACKUP_DIR:-./backups}"
KEEP="${BACKUP_KEEP_DAYS:-14}"
STAMP="$(date +%Y-%m-%d_%H%M)"
mkdir -p "$DEST"

echo "[$(date)] Sauvegarde $STAMP"
docker compose exec -T db pg_dump -U kokora -d kokora --format=custom > "$DEST/kokora-$STAMP.dump"
docker compose exec -T app tar czf - -C /data . > "$DEST/fichiers-$STAMP.tar.gz"

# Vérification minimale : une sauvegarde vide est un échec
[ -s "$DEST/kokora-$STAMP.dump" ] || { echo "ERREUR : sauvegarde de la base vide"; exit 1; }

# Rotation locale
find "$DEST" -name 'kokora-*.dump' -mtime +"$KEEP" -delete
find "$DEST" -name 'fichiers-*.tar.gz' -mtime +"$KEEP" -delete

# Copie hors du serveur (un serveur peut disparaître avec ses disques)
if [ -n "${RCLONE_REMOTE:-}" ]; then
  rclone copy "$DEST" "$RCLONE_REMOTE" --include "*-$STAMP.*"
fi
echo "[$(date)] Terminé : $(du -h "$DEST/kokora-$STAMP.dump" | cut -f1) (base), $(du -h "$DEST/fichiers-$STAMP.tar.gz" | cut -f1) (fichiers)"
