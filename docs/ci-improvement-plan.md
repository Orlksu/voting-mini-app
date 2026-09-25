# Аудит CI-процесса и план улучшений

> Итог аудита SourceCraft Pipelines по Issue #4 «CI в ПулРеквестах».
> Дата: 25.09.2026. Ветка анализа: `develop`.

## 1. Текущее состояние CI (as-is)

Единственный конфигурационный файл — `.sourcecraft/ci.yaml`. Других CI-систем
(GitHub Actions, GitLab CI и т.п.) в репозитории нет.

```yaml
on:
  push:
    - workflows: [staging]
      filter:
        branches: ["develop"]
```

Триггер ровно один: **push в `develop`**. Событие запускает workflow `staging`,
состоящий из трёх задач:

| Задача | Образ | Действия |
|---|---|---|
| `frontend-checks` | `node:20-alpine` | `npm ci` → `npm run lint` → `npm run build` |
| `backend-checks` | `mcr.microsoft.com/dotnet/sdk:10.0` | `dotnet restore` (retry x3) → `dotnet build -c Release -r linux-x64 --no-restore` |
| `staging-deploy` | `alpine:3.20` | SSH на staging VM → `git checkout -B develop origin/develop` → `docker compose up -d --build --force-recreate --remove-orphans` → curl-проверки `/healthz` (backend) и `/` (frontend) |

### Фактические данные прогонов

- Все событийные прогоны имеют тип `push`; **ни одного события типа
  `pull_request` не зарегистрировано** — CI в Pull Request сейчас не работает.
- Ручные запуски (`event_type=manual`, runs 27–36) исполняют пустой workflow
  `default` и не делают ничего полезного.
- Зафиксированные падения (runs 10, 11, 12, 14, 15): `frontend-checks` и
  `backend-checks` проходят успешно, падает только `staging-deploy` →
  `deploy-over-ssh`. То есть «зелёный» CI не гарантирует успешный деплой.
- Runs 18–25 отменены (canceled) — параллельные push/ручные запуски пересекались.

### Что уже сделано ранее

Аудит Issue #3 был частично реализован в PR !24 (ветка `fix/high-priority-docker-ci`):

- `ports: !reset []` для `db`/`valhalla` в staging-оверрайде (не публиковать БД/Valhalla наружу);
- пин Valhalla `latest` → `3.5.1`;
- `frontend/.dockerignore`: `.env*` вместо `.env*.local`;
- порты health-проверок читаются из `.env.staging` на VM;
- `ssh -o BatchMode=yes`.

Нереализованные пункты из PR !24 (зафиксированы там же как низкоприоритетные):
Node 20 в EOL, backend healthcheck в compose, проверки PostgreSQL/Valhalla после
деплоя, `docker build` в CI до деплоя, правка `X-Forwarded-Proto`.

## 2. Анализ проблем

### 2.1. Критические

1. **Нет CI в Pull Request.** Пайплайн запускается только после merge в
   `develop`. Некорректный код попадает в ветку разработки без единой
   автоматической проверки, а первый сигнал приходит уже с staging VM.
   Это прямое противоречие заголовку Issue #4.

2. **Docker-образы в CI не собираются.** Проверки выполняют `npm run build` и
   `dotnet build`, но реальные образы строит только `docker compose up -d --build`
   на VM уже после merge. Поломанный `Dockerfile`, `nginx.conf` или `publish`
   проходит CI и роняет staging. Подтверждено прогонами 10–15: проверки зелёные,
   деплой красный.

3. **Деплой не имеет защиты от параллельных запусков.** Несколько push подряд
   запускают параллельные прогоны, каждый из которых делает
   `git checkout -B develop` и `up -d --build` на одной VM. Итог — гонки и
   отменённые runs (18–25). Нет блокировки/сериализации.

4. **Ручной запуск — no-op.** Workflow `default` пуст. Нельзя перезапустить
   проверки или передеплоить без нового коммита в `develop`.

### 2.2. Высокие

5. **Нет автоматических тестов.** Во frontend нет скрипта `test`
   (`package.json`), в backend нет тестовых проектов (`backend/src` — только
   Api/Contracts/RoutingEngine). CI проверяет лишь сборку.

6. **Post-deploy проверки неполные.** Проверяются только `/healthz` backend и
   `/` frontend. Доступность Valhalla и фактическая связность backend → PostgreSQL
   не проверяются. README обещает проверку «sample API», которой в `ci.yaml` нет
   (дрейф документации).

7. **Нет отката и учёта версий деплоя.** Образы не тегируются и не хранятся в
   registry; VM всегда пересобирает из `origin/develop`. SHA развёрнутого коммита
   нигде не фиксируется, откат = ручной `git revert` + новый push.

### 2.3. Средние

8. **Node 20 — EOL.** Образ `node:20-alpine` используется и в CI, и во
   `frontend/Dockerfile`.
9. **Нет кэширования зависимостей.** Каждый прогон заново выполняет `npm ci` и
   `dotnet restore`; ретраи restore (x3) маскируют флаки сети, но не ускоряют сборку.
10. **Плавающие теги образов.** `node:20-alpine`, `mcr.microsoft.com/dotnet/sdk:10.0`,
    `postgres:17` — без фиксации patch-версий.
11. **Backend публикуется на хост в staging** (`STAGING_BACKEND_PORT`) — осознанное
    текущее решение, но стоит зафиксировать как риск и пересмотреть.
12. **Один монолитный SSH-скрипт деплоя.** Нет пошагового логирования внутри run,
    логи упавшего куба недоступны через артефакты (проверено: artifacts пусты),
    что затрудняет диагностику.

### 2.4. Что сделано хорошо (не менять)

- Секреты передаются только через SourceCraft secrets, не попадают в логи/репозиторий;
- `set -Eeuo pipefail` в удалённом скрипте;
- `BatchMode=yes` и `IdentitiesOnly` защищают SSH от зависаний;
- обязательные переменные окружения валидируются через `:?` в Compose;
- `docker compose ... config --quiet` выполняется до `up`;
- health-проверки читают порты из `.env.staging`, а не хардкодят.

## 3. План улучшений

Приоритеты: **P0** — блокеры (Issue #4), **P1** — надёжность, **P2** — скорость и
гигиена. Каждый пункт — отдельный PR с проверками из AGENTS.md.

### Фаза 1 (P0) — CI в Pull Request

- **1.1. Проверки на PR.** Добавить в `.sourcecraft/ci.yaml` событие
  `pull_request` (если схема платформы его поддерживает) с workflow `pr-checks`:
  `frontend-checks` + `backend-checks` без `staging-deploy`.
  Fallback, если событие не поддерживается: триггер `push` с фильтром по всем
  веткам, кроме `develop`/`main` (проверки на ветке PR).
- **1.2. Merge requirements.** Настроить SourceCraft merge checks
  (`src pr checks`) так, чтобы PR нельзя было слить без успешного прогона
  `pr-checks`. Не отключать существующие проверки.
- **1.3. Деплой только из `develop`.** `staging-deploy` остаётся исключительно на
  `push` в `develop`; из PR-веток деплой не запускается.

### Фаза 2 (P1) — Раннее выявление проблем

- **2.1. Docker build в CI.** Перед деплоем (или в `pr-checks`) выполнять
  `docker compose build frontend backend` и `nginx -t` внутри собранного образа
  frontend. Сломавшийся `Dockerfile`/`nginx.conf` будет ловиться до merge.
- **2.2. Автотесты.** Backend: добавить xunit-проект `backend/tests/*.Tests`,
  подключить `dotnet test` в CI. Frontend: добавить `vitest` и скрипт `test`,
  включить в CI. Не отключать lint/build.
- **2.3. Полные post-deploy проверки.** После деплоя проверять: backend `/healthz`,
  frontend `/`, Valhalla `/status` (через сеть Compose), связность backend → БД
  (например, GET `/api/v1/engineers`, как обещает README). Привести README в
  соответствие с фактическим пайплайном.

### Фаза 3 (P1) — Надёжность деплоя

- **3.1. Сериализация деплоев.** Блокировка на VM (например, `flock` на
  деплойном каталоге) или concurrency-control уровня платформы, чтобы
  параллельные push не выполняли `git checkout -B`/`up -d` одновременно.
- **3.2. Учёт и откат.** Сохранять SHA развёрнутого коммита в файл на VM
  (например, `staging-deployed.sha`), добавить ручной workflow «redeploy» с
  параметром коммита (см. 3.3). Среднесрочно — тегированные образы в registry
  (не добавлять новые сервисы без отдельного обсуждения).
- **3.3. Полезный ручной запуск.** Заменить пустой `default` на workflow
  `redeploy`: повторный прогон проверок + деплой последнего `origin/develop`.
- **3.4. Логирование деплоя.** Пошаговые `echo`-метки в удалённом скрипте и
  сохранение вывода в артефакты run, чтобы упавший шаг было видно без SSH.

### Фаза 4 (P2) — Скорость и гигиена

- **4.1. Node 22/24 LTS** в CI и `frontend/Dockerfile` (Node 20 — EOL).
- **4.2. Фиксация версий образов**: patch-пины для `node`, `dotnet/sdk`,
  `aspnet`, `postgres` в Dockerfile/Compose.
- **4.3. Кэширование** `npm ci` и NuGet restore между прогонами (если
  поддерживается платформой); иначе — оставить ретраи, но зафиксировать версии.
- **4.4. README**: синхронизировать описание пайплайна с `ci.yaml`.
- **4.5. Решить судьбу публикации backend на хост в staging** — закрыть доступ
  или явно задокументировать как временное решение.

## 4. Критерии готовности

- PR-ветка проходит `frontend-checks` и `backend-checks` до merge в `develop`;
- merge в `develop` невозможен без успешных проверок (merge checks);
- Docker-образы frontend/backend собираются в CI до деплоя;
- post-deploy проверки покрывают frontend, backend, Valhalla и БД;
- деплой сериализован, SHA развёрнутого коммита фиксируется;
- ручной запуск выполняет осмысленные действия.

## 5. Риски

- **Событие `pull_request` может не поддерживаться схемой платформы** — перед
  реализацией 1.1 проверить допустимые события; fallback — `push` по PR-веткам.
- Изменение merge requirements может временно заблокировать merge PR, пока не
  настроены проверки; последовательность: сначала CI на PR-ветках, затем
  requirements.
- Дополнительные задачи в пайплайне (docker build, тесты) удлиняют прогон.
- Затрагиваются только `.sourcecraft/ci.yaml`, Dockerfile, `package.json`/тесты и
  README; бизнес-логика не меняется.

## 6. План отката

- Для каждого пункта — `git revert` конкретного коммита/PR.
- Merge requirements и триггеры — обратимое изменение `.sourcecraft/ci.yaml`.
- Деплойные нововведения (блокировка, манифест SHA) не меняют структуру
  containers/volumes; откат = удаление lock-файла и возврат к прежнему workflow.