#!/usr/bin/env bash
# Готовит один PBF Москвы и Московской области для Valhalla.
#
# Выгрузки openstreetmap.fr по Москве и области перекрываются на границе.
# Если отдать их Valhalla двумя файлами, сборка зависает на этапе
# "Adding complex turn restrictions" из-за дублей. Поэтому склеиваем их
# через `osmium merge` в один файл без дублей.
#
# Нужен только Docker: osmium запускается во временном контейнере.
# Использование: scripts/prepare-valhalla-region.sh  (из корня репозитория)
set -Eeuo pipefail

cd "$(dirname "$0")/.."

data_dir="${VALHALLA_DATA_DIR:-valhalla-data}"
base_url="https://download.openstreetmap.fr/extracts/russia/central_federal_district"
osmium_image="debian:bookworm-20260918-slim"
output="moscow_region.osm.pbf"

mkdir -p "$data_dir"

# Не смешиваем с уже существующим графом или другими выгрузками:
# Valhalla собирает граф из всех .pbf в каталоге.
existing="$(find "$data_dir" -maxdepth 1 \( -name '*.pbf' -o -name 'valhalla_tiles*' -o -name 'file_hashes.txt' \) -print)"
if [ -n "$existing" ]; then
  echo "В $data_dir уже есть граф или выгрузки:" >&2
  echo "$existing" >&2
  echo "Сначала отложите их в резервную папку (docs/valhalla.md, шаг «Отложить старый граф»)." >&2
  exit 1
fi

work_dir="$(mktemp -d "${TMPDIR:-/tmp}/valhalla-region.XXXXXX")"
trap 'rm -rf "$work_dir"' EXIT

for name in moscow moscow_oblast; do
  echo "Скачиваю ${name}-latest.osm.pbf..."
  curl --fail --location --silent --show-error \
    --retry 3 --retry-delay 5 \
    -o "$work_dir/${name}.osm.pbf" "$base_url/${name}-latest.osm.pbf"
done

echo "Склеиваю выгрузки (osmium merge)..."
docker run --rm -v "$work_dir:/work" "$osmium_image" bash -Eeuo pipefail -c '
  apt-get update -qq
  apt-get install -y -qq --no-install-recommends osmium-tool >/dev/null
  osmium merge /work/moscow.osm.pbf /work/moscow_oblast.osm.pbf \
    -o /work/region.osm.pbf --overwrite
  osmium fileinfo -e /work/region.osm.pbf | grep -E "Number of (nodes|ways|relations):"
  chmod 644 /work/region.osm.pbf
'

mv "$work_dir/region.osm.pbf" "$data_dir/$output"
echo "Готово: $data_dir/$output ($(du -h "$data_dir/$output" | cut -f1))"
echo "Теперь перезапустите valhalla, граф соберётся из этого файла."
