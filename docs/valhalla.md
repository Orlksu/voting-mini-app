# Valhalla: граф Москвы и Московской области

Маршрутизация строится по графу Valhalla, собранному из двух выгрузок OpenStreetMap
с download.openstreetmap.fr:

- `moscow-latest.osm.pbf` — Москва (~105 МБ);
- `moscow_oblast-latest.osm.pbf` — Московская область (~228 МБ).

Раньше использовалась выгрузка BBBike `Moscow.osm.pbf`, которая покрывает только
Москву в пределах МКАД (lon 37.32–37.88, lat 55.57–55.92): маршруты в область
не строились.

## Как образ решает, собирать ли граф

Образ `gis-ops/docker-valhalla` при старте смотрит в `valhalla-data/`:

1. Если тайлов нет — собирает граф.
2. Если тайлы есть и хеши `.pbf` не изменились — просто отдаёт готовый граф.
3. `tile_urls` скачиваются **только** когда в `valhalla-data/` нет ни одного `.pbf`
   и нет тайлов.

Поэтому смена `tile_urls` в compose на уже работающем сервере ничего не меняет:
старые файлы нужно убрать вручную (см. ниже).

## Ресурсы

Оценка, не замер: сборка графа Москвы с областью требует нескольких ГБ RAM
и ~2 ГБ диска под PBF и тайлы. На staging VM 2 ГБ RAM, поэтому перед сборкой
нужен swap и один поток сборки. Отдача готового графа памяти почти не требует.

## Переход на staging VM (вручную)

Команды выполняются на VM из каталога проекта. Ничего не удаляем — старые файлы
откладываем в резервную папку для отката.

1. Расширить диск в облаке (рекомендуется +10 ГБ) и файловую систему:

   ```bash
   lsblk && df -hT /
   sudo growpart /dev/vda 1
   sudo resize2fs /dev/vda1   # для ext4; для xfs: sudo xfs_growfs /
   df -h /
   ```

2. Добавить swap 4 ГБ:

   ```bash
   sudo fallocate -l 4G /swapfile
   sudo chmod 600 /swapfile
   sudo mkswap /swapfile
   sudo swapon /swapfile
   echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
   echo 'vm.swappiness=10' | sudo tee /etc/sysctl.d/99-swappiness.conf
   sudo sysctl --system
   free -h
   ```

3. Дождаться деплоя этой ветки в `develop` (compose с новыми `tile_urls`).
   Во время сборки не мержить в `develop`: деплой пересоздаёт контейнер
   и прерывает сборку.

4. Отложить старый граф и запустить сборку в один поток:

   ```bash
   backup="$HOME/valhalla-backup-$(date +%Y%m%d)"
   mkdir -p "$backup"
   for f in Moscow.osm.pbf valhalla_tiles valhalla_tiles.tar file_hashes.txt; do
     if [ -e "valhalla-data/$f" ]; then mv "valhalla-data/$f" "$backup"/; else echo "нет $f — пропускаю"; fi
   done
   ls valhalla-data   # должен остаться только valhalla.json
   echo 'VALHALLA_SERVER_THREADS=1' >> .env.staging
   docker compose --env-file .env.staging -f docker-compose.yml \
     -f docker-compose.staging.yml up -d --force-recreate valhalla
   docker logs -f lct-valhalla
   ```

   Сборка на 2 ядрах со swap может идти долго (порядка часа и больше).
   Готовность — в логах появляется запуск `valhalla_service`, а
   `curl -s http://127.0.0.1:8002/status` внутри сети отвечает JSON.

5. Проверить маршрут в область (Москва → Подольск):

   ```bash
   docker exec lct-valhalla curl -s -X POST http://localhost:8002/route \
     -H 'Content-Type: application/json' \
     -d '{"locations":[{"lat":55.7558,"lon":37.6176},{"lat":55.4312,"lon":37.5447}],"costing":"auto"}' \
     | head -c 300
   ```

6. Вернуть 2 потока для отдачи маршрутов: убрать строку
   `VALHALLA_SERVER_THREADS=1` из `.env.staging` и перезапустить `valhalla`
   той же командой `up -d --force-recreate valhalla`. Граф при этом
   не пересобирается.

7. Когда всё работает, резервную папку можно удалить вручную.

## Откат

1. Остановить сборку и вернуть старый граф:

   ```bash
   docker compose --env-file .env.staging -f docker-compose.yml \
     -f docker-compose.staging.yml stop valhalla
   failed="$HOME/valhalla-failed-$(date +%Y%m%d)"
   mkdir -p "$failed"
   for f in valhalla-data/*.osm.pbf valhalla-data/valhalla_tiles valhalla-data/valhalla_tiles.tar valhalla-data/file_hashes.txt; do
     if [ -e "$f" ]; then mv "$f" "$failed"/; fi
   done
   mv "$HOME"/valhalla-backup-*/* valhalla-data/
   docker compose --env-file .env.staging -f docker-compose.yml \
     -f docker-compose.staging.yml up -d --force-recreate valhalla
   ```

2. Revert PR с новыми `tile_urls` — иначе при следующей очистке `valhalla-data`
   образ снова скачает выгрузки области.

## Если сборка упала по памяти

Healthcheck контейнер не перезапускает: `restart: unless-stopped` срабатывает,
только когда процесс сборки уже завершился (например, его убил OOM killer).

Опасность в другом: после падения в `valhalla-data/valhalla_tiles` остаются
недособранные тайлы. При перезапуске образ видит, что тайлы есть, и **молча
начинает отдавать неполный граф** без пересборки
(в логах `INFO: Routing tiles exist and no need to rebuild.`).

Признаки:

```bash
docker inspect -f '{{.RestartCount}}' lct-valhalla   # > 0 во время сборки — была авария
docker logs lct-valhalla 2>&1 | grep -E 'Killed|Routing tiles exist'
dmesg | grep -i -E 'out of memory|killed process' | tail
free -h
```

Что делать: остановить `valhalla`, отложить частично собранные
`valhalla_tiles*` и `file_hashes.txt` в отдельную папку (выгрузки `.osm.pbf`
можно оставить — повторно скачивать не придётся), увеличить swap, убедиться,
что `VALHALLA_SERVER_THREADS=1`, и снова запустить шаг 4.
