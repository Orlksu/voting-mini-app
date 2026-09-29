#!/usr/bin/env bash
# Собирает и запускает весь проект в Docker одной командой.
#
# Использование (из корня репозитория):  ./start.sh
#
# Что делает:
#   1. проверяет Docker, Docker Compose v2, curl и свободные порты;
#   2. создаёт .env из .env.example, если его нет;
#   3. docker compose up -d --build;
#   4. ждёт готовности backend и frontend, затем графа Valhalla;
#   5. печатает адреса.
#
# Скрипт ничего не удаляет: база и граф Valhalla сохраняются между запусками.
# Остановить проект: docker compose down
set -Eeuo pipefail

cd "$(dirname "$0")"

FRONTEND_URL="http://localhost:8080"
BACKEND_HEALTH_URL="http://localhost:5001/healthz"
VALHALLA_STATUS_URL="http://localhost:8002/status"
PORTS=(8080 5001 5432 8002)
# Сколько ждать сборки графа Valhalla. Приложение работает и без него,
# поэтому по таймауту скрипт не падает, а только предупреждает.
VALHALLA_WAIT_SECONDS="${VALHALLA_WAIT_SECONDS:-900}"

if [ -t 1 ]; then
  bold=$'\033[1m'; green=$'\033[32m'; yellow=$'\033[33m'; red=$'\033[31m'; reset=$'\033[0m'
else
  bold=""; green=""; yellow=""; red=""; reset=""
fi

step() { printf '\n%s==> %s%s\n' "$bold" "$1" "$reset"; }
ok()   { printf '%s✔ %s%s\n' "$green" "$1" "$reset"; }
warn() { printf '%s! %s%s\n' "$yellow" "$1" "$reset"; }
fail() { printf '%s✘ %s%s\n' "$red" "$1" "$reset" >&2; exit 1; }

on_error() {
  printf '\n%s✘ Запуск прервался на строке %s.%s\n' "$red" "$1" "$reset" >&2
  printf 'Логи сервисов: docker compose logs --tail=50\n' >&2
  printf 'Типичные проблемы и решения — раздел «Если что-то пошло не так» в README.md\n' >&2
}
trap 'on_error $LINENO' ERR

# Порт занят, если на него удаётся открыть TCP-соединение.
port_busy() { (exec 3<>"/dev/tcp/127.0.0.1/$1") 2>/dev/null; }

# Ждёт, пока URL ответит 2xx/3xx. $1 — URL, $2 — таймаут в секундах.
wait_http() {
  local url="$1" timeout="$2" waited=0
  until curl --fail --silent --output /dev/null --max-time 5 "$url"; do
    if [ "$waited" -ge "$timeout" ]; then
      return 1
    fi
    sleep 5
    waited=$((waited + 5))
  done
}

step "Проверяю окружение"

command -v docker >/dev/null 2>&1 \
  || fail "Docker не найден. Установите Docker Desktop: https://docs.docker.com/get-docker/"
docker compose version >/dev/null 2>&1 \
  || fail "Нет Docker Compose v2 (команда 'docker compose'). Обновите Docker Desktop или установите docker-compose-plugin."
docker info >/dev/null 2>&1 \
  || fail "Docker daemon не отвечает. Запустите Docker Desktop и повторите."
command -v curl >/dev/null 2>&1 \
  || fail "Не найден curl. Установите его (apt install curl / brew install curl)."
ok "Docker $(docker version --format '{{.Server.Version}}'), $(docker compose version --short 2>/dev/null || echo 'Compose v2')"

# Если проект уже запущен, порты заняты его же контейнерами — это нормально.
if [ -z "$(docker compose ps --quiet 2>/dev/null)" ]; then
  busy=()
  for port in "${PORTS[@]}"; do
    if port_busy "$port"; then
      busy+=("$port")
    fi
  done
  if [ "${#busy[@]}" -gt 0 ]; then
    fail "Заняты порты: ${busy[*]}. Освободите их (часто 5432 занят локальной PostgreSQL) или поменяйте маппинг портов в docker-compose.yml."
  fi
  ok "Порты ${PORTS[*]} свободны"
else
  ok "Проект уже запущен — пересоберу и обновлю контейнеры"
fi

step "Готовлю .env"

if [ -f .env ]; then
  ok ".env уже есть — оставляю как есть"
else
  cp .env.example .env
  ok "Создан .env из .env.example"
fi
if ! grep -Eq '^VITE_MAPTILER_API_KEY=.+' .env; then
  warn "В .env пустой VITE_MAPTILER_API_KEY — интерфейс откроется без карты."
fi

step "Собираю и запускаю контейнеры (первый раз — несколько минут)"

docker compose up -d --build

step "Жду backend и frontend"

wait_http "$BACKEND_HEALTH_URL" 180 \
  || fail "Backend не ответил на $BACKEND_HEALTH_URL за 3 минуты. Логи: docker compose logs backend"
ok "Backend готов"
wait_http "$FRONTEND_URL/" 60 \
  || fail "Frontend не ответил на $FRONTEND_URL за минуту. Логи: docker compose logs frontend"
ok "Frontend готов"

step "Жду граф дорог Valhalla (первый раз: скачивание карты Москвы и сборка, обычно 2–5 минут)"

printf 'Интерфейс уже работает: %s\n' "$FRONTEND_URL"
printf 'Пока графа нет, расстояния считаются по прямой.\n'
if wait_http "$VALHALLA_STATUS_URL" "$VALHALLA_WAIT_SECONDS"; then
  ok "Valhalla готова — маршруты строятся по дорогам"
else
  warn "Valhalla ещё собирает граф (ждал ${VALHALLA_WAIT_SECONDS} с). Приложение работает; прогресс: docker compose logs -f valhalla"
fi

trap - ERR

cat <<EOF

${green}${bold}Готово!${reset}

  Веб-интерфейс        ${bold}${FRONTEND_URL}${reset}
  Документация API     ${FRONTEND_URL}/scalar/
  Health check         ${BACKEND_HEALTH_URL}

  Статус сервисов      docker compose ps
  Логи                 docker compose logs -f <frontend|backend|db|valhalla>
  Остановить           docker compose down   (данные сохраняются)
EOF
