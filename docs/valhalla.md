# Valhalla: граф Москвы и Московской области

Маршрутизация строится по графу Valhalla из данных OpenStreetMap.

- По умолчанию (`tile_urls` в `docker-compose.yml`) образ скачивает выгрузку
  BBBike `Moscow.osm.pbf`. Она покрывает только Москву примерно в пределах МКАД
  (lon 37.32–37.88, lat 55.57–55.92): маршруты в область не строятся.
- Граф Москвы с областью готовится скриптом `scripts/prepare-valhalla-region.sh`.

## Почему нужен скрипт

Выгрузки openstreetmap.fr `moscow-latest` и `moscow_oblast-latest` перекрываются
на границе: ~2,7 млн узлов, ~0,46 млн дорог и ~30 тыс. отношений (в том числе
запретов поворотов) есть в обоих файлах. Если отдать Valhalla два файла сразу,
сборка зависает на этапе `Adding complex turn restrictions at level 2`
(локально — больше 40 минут на одном ядре без результата).

Скрипт скачивает обе выгрузки и склеивает их `osmium merge` в один
`moscow_region.osm.pbf` без дублей. `osmium` запускается во временном контейнере
`debian:bookworm-20260918-slim`, на машине нужен только Docker.

Замеры локальной сборки из склеенного файла (arm64, 4 потока): 3,6 минуты,
пик памяти 2,9 ГБ, тайлы 424 МБ плюс `valhalla_tiles.tar` 388 МБ.
Проверенные маршруты от центра Москвы: Подольск 41 км, Балашиха 25 км,
Зеленоград 47 км, Серпухов 108 км, Коломна 117 км, Можайск 112 км.

## Как образ решает, собирать ли граф

Образ `gis-ops/docker-valhalla` при старте смотрит в `valhalla-data/`:

1. Если тайлов нет — собирает граф из всех `.pbf` в каталоге.
2. Если тайлы есть и хеши `.pbf` не изменились — отдаёт готовый граф.
3. `tile_urls` скачиваются, **только** когда в каталоге нет ни `.pbf`, ни тайлов.

Поэтому скрипт отказывается работать, если в `valhalla-data/` уже лежат
выгрузки или тайлы: их нужно сначала отложить (шаг 1 ниже).

## Переход на staging VM

Команды выполняются на VM из каталога проекта. Ничего не удаляем — старые
файлы откладываем в резервную папку для отката. Файлы в `valhalla-data`
принадлежат пользователю контейнера (UID 59999), поэтому `mv` через `sudo`.

Для краткости:

```bash
cd ~/case-13-field-engineer-routing-team-97
dc() { docker compose --env-file .env.staging -f docker-compose.yml -f docker-compose.staging.yml "$@"; }
```

Пока идёт сборка, не мержить в `develop`: деплой пересоздаёт контейнер
и прерывает сборку.

1. Отложить старый граф:

   ```bash
   backup="$HOME/valhalla-backup-$(date +%Y%m%d)"
   mkdir -p "$backup"
   for f in valhalla-data/*.osm.pbf valhalla-data/valhalla_tiles valhalla-data/valhalla_tiles.tar \
            valhalla-data/file_hashes.txt valhalla-data/duplicateways.txt; do
     if [ -e "$f" ]; then sudo mv "$f" "$backup"/; fi
   done
   ls valhalla-data   # должен остаться только valhalla.json
   ```

2. Подготовить выгрузку Москвы с областью:

   ```bash
   scripts/prepare-valhalla-region.sh
   ls -la valhalla-data   # moscow_region.osm.pbf ~300 МБ и valhalla.json
   ```

3. Пересобрать граф и следить за логами:

   ```bash
   dc up -d --force-recreate valhalla
   docker logs -f lct-valhalla
   ```

   В логах: `Running build tiles with: ... /custom_files/moscow_region.osm.pbf`,
   этапы сборки и в конце запуск `valhalla_service`. Раз в несколько минут
   во втором окне:

   ```bash
   free -h
   docker inspect -f '{{.RestartCount}}' lct-valhalla   # должно быть 0
   ```

   Если на 2 ГБ RAM памяти не хватает даже со swap, собирать в один поток:
   добавить `VALHALLA_SERVER_THREADS=1` в `.env.staging`, отложить частичные
   тайлы (см. «Если сборка упала по памяти») и повторить шаг 3. После сборки
   строку убрать и снова выполнить `dc up -d --force-recreate valhalla`.

4. Проверить маршрут в область (Москва → Подольск, ожидается ~41 км):

   ```bash
   docker exec lct-valhalla curl -s -X POST http://localhost:8002/route \
     -H 'Content-Type: application/json' \
     -d '{"locations":[{"lat":55.7558,"lon":37.6176},{"lat":55.4312,"lon":37.5447}],"costing":"auto","units":"kilometers"}' \
     | grep -o '"length":[0-9.]*' | tail -1
   ```

   Ошибка `No path could be found` или `unconnected regions` означает, что
   область в граф не попала — переходить к откату.

5. Когда всё работает несколько дней, резервную папку можно удалить:
   `sudo rm -rf ~/valhalla-backup-*`. До этого не создавайте новых бэкапов
   поверх: откат берёт самую свежую непустую папку.

## Откат

Сначала проверяем, что есть что возвращать, и только потом трогаем текущий
граф. Иначе при отсутствии бэкапа `valhalla-data` останется пустым, и образ
при старте молча скачает и соберёт граф заново вместо отката.
Берём самую свежую **непустую** резервную папку: их может быть несколько
(например, пустая от прерванной попытки).

```bash
backup=""
for d in $(ls -dt "$HOME"/valhalla-backup-* 2>/dev/null); do
  if [ -n "$(ls -A "$d")" ]; then backup="$d"; break; fi
done

if [ -z "$backup" ]; then
  echo "Нет непустой папки ~/valhalla-backup-*: откатывать не на что, ничего не трогаю."
else
  echo "Возвращаю граф из $backup"
  ls "$backup"
  dc stop valhalla
  failed="$HOME/valhalla-failed-$(date +%Y%m%d-%H%M)"
  mkdir -p "$failed"
  for f in valhalla-data/*.osm.pbf valhalla-data/valhalla_tiles valhalla-data/valhalla_tiles.tar \
           valhalla-data/file_hashes.txt valhalla-data/duplicateways.txt; do
    if [ -e "$f" ]; then sudo mv "$f" "$failed"/; fi
  done
  sudo mv "$backup"/* valhalla-data/
  dc up -d --force-recreate valhalla
  docker inspect -f '{{.State.Health.Status}} restarts={{.RestartCount}}' lct-valhalla
fi
```

Старый граф поднимается сразу, без сборки: хеши его `.pbf` уже записаны
в `file_hashes.txt`. Проверить, что пересборки не было:
`docker logs lct-valhalla 2>&1 | grep -E 'No routing tiles|Downloading'` — пусто.

## Если сборка упала по памяти

Healthcheck контейнер не перезапускает: `restart: unless-stopped` срабатывает,
только когда процесс сборки уже завершился (например, его убил OOM killer).

Опасность в другом: после падения в `valhalla-data/valhalla_tiles` остаются
недособранные тайлы. При перезапуске образ видит, что тайлы есть, и **молча
начинает отдавать неполный граф** без пересборки.

Признаки:

```bash
docker inspect -f '{{.RestartCount}}' lct-valhalla   # > 0 во время сборки — была авария
docker logs lct-valhalla 2>&1 | grep -E 'Killed|Routing tiles exist'
dmesg | grep -i -E 'out of memory|killed process' | tail
free -h
```

Что делать: остановить `valhalla`, отложить частичные `valhalla_tiles*`
и `file_hashes.txt` в отдельную папку (`moscow_region.osm.pbf` оставить),
включить `VALHALLA_SERVER_THREADS=1` и повторить шаг 3.
