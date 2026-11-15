# Совет Старост — Telegram Mini App

Mini App для голосования на заседаниях Совета старост: frontend, FastAPI API,
SQLite и Telegram-бот на aiogram 3.

Проект можно запустить в двух режимах:

- **Демо** — только frontend, без backend и Telegram. Данные хранятся в `localStorage`.
- **Сервер** — FastAPI + SQLite; голоса, роли и повестка хранятся в базе, бот регистрирует участников.

## Структура

```text
miniapp/
  index.html                 — разметка Mini App
  styles.css                 — тема и экран участника
  admin.css                  — панель администратора
  screen.css                 — экран зала и адаптив
  app.js                     — состояние, рендер, API-клиент, demo fallback
  assets/
    logo.png
    fonts/Montserrat-*.ttf
  .env.example               — шаблон переменных окружения
  backend/
    main.py                  — FastAPI API и раздача frontend
    database.py              — модели, миграции, seed и работа с SQLite
    bot.py                   — aiogram 3, регистрация и команды
    requirements.txt
    .gitignore
```

## Быстрый старт: только frontend

Из корня репозитория:

```powershell
python -m http.server 4173 --bind 127.0.0.1 --directory miniapp
```

Откройте `http://127.0.0.1:4173/`.

Это демо-режим: backend не нужен, данные хранятся в браузере. Можно открыть
`miniapp/index.html` напрямую, но локальный сервер надёжнее для шрифтов и ассетов.

## Полный запуск: API, база и бот

### 1. Настройте окружение

Скопируйте `miniapp/.env.example` в `miniapp/.env`:

```powershell
Copy-Item miniapp\.env.example miniapp\.env
```

Заполните как минимум:

```dotenv
BOT_TOKEN=токен_из_BotFather
ADMIN_TG_ID=ваш_telegram_id
INVITE_CODE=invite_pass
MINIAPP_URL=http://127.0.0.1:8000/
DEV_MODE=1
```

`BOT_TOKEN` и `ADMIN_TG_ID` — не секреты репозитория, а реальные значения
должны лежать только в локальном `.env` или в secrets CI/хостинга.

Для локальной проверки API без Telegram установите `DEV_MODE=1`.
В production обязательно `DEV_MODE=0`.

### 2. Запустите backend

```powershell
cd miniapp\backend
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install -r requirements.txt
uvicorn main:app --reload --host 127.0.0.1 --port 8000
```

Проверка:

```text
http://127.0.0.1:8000/api/healthz
```

Frontend раздаётся тем же сервером:

```text
http://127.0.0.1:8000/
```

Swagger/OpenAPI:

```text
http://127.0.0.1:8000/docs
```

При первом запуске создаётся SQLite-файл `voting.db`, таблицы и три
демо-вопроса повестки. Пользователи и голоса не сидятся: при `DEV_MODE=1`
тестовый пользователь создаётся при первом запросе с `dev_user_id`.

### 3. Запустите Telegram-бота

В отдельном терминале из `miniapp/backend`, с тем же `.env`:

```powershell
python bot.py
```

Бот:

- принимает `/start invite_pass` и просит ввести **Имя и Фамилию**;
- сохраняет `telegram_id`, `real_name`, `username`, `is_allowed`;
- открывает Mini App кнопкой;
- `/admin` — панель администратора;
- `/invite` — пригласительная ссылка;
- `/cancel` — отмена регистрации.

Для настоящей кнопки Web App нужен HTTPS-адрес. Локальный `http://`-адрес
бот отправит обычной ссылкой.

### 4. Локальная авторизация

При `DEV_MODE=1` API принимает query-параметр `dev_user_id`:

```text
http://127.0.0.1:8000/?mode=admin&dev_user_id=111
http://127.0.0.1:8000/?mode=participant&dev_user_id=555
http://127.0.0.1:8000/?mode=screen
```

- `111` при `ADMIN_TG_ID=111` автоматически получает роль администратора.
- Любой другой `dev_user_id` создаёт локального тестового участника.
- Публичный экран работает без авторизации.

`dev_user_id` не работает при `DEV_MODE=0`.

В Telegram frontend отправляет `initData` в заголовке
`X-Telegram-Init-Data`; backend проверяет HMAC-SHA256 и срок `auth_date`.

## Роли

- В таблице `users` есть поле `role` со значениями `admin` и `user`.
- `ADMIN_TG_ID` автоматически получает `role=admin` при старте, если роль ещё
  не назначена в базе.
- В админ-панели можно назначить администратора и разжаловать его.
- Назначение хранится в БД; backend проверяет роль по `telegram_id`, а не по
  значению из `.env`.
- Нельзя разжаловать последнего администратора — API вернёт `409`.
- `real_name` из регистрации используется в профиле, списке участников и
  админ-панели. Имя из Telegram-профиля для этого не применяется.

## Режимы frontend

Переключатель «Админ / Участник / Экран» закреплён в углу экрана.

- **Участник** — карточка заседания, повестка, голосование, состояние
  «Вы уже проголосовали».
- **Админ** — карточка мероприятия, повестка, редактор вопросов и вариантов,
  запуск/завершение голосования, результаты, участники и роли.
- **Экран** — публичный дашборд для проектора: явка, результаты, динамика,
  лидер и победитель.

Быстрое переключение: `Alt+1` — админ, `Alt+2` — участник, `Alt+3` — экран.
Не-администратор не видит и не может открыть админ-панель.

Frontend автоматически проверяет `/api/healthz`:

- если API доступен — работает режим **«Сервер»**;
- если нет — переходит в **«Демо»** и показывает уведомление.

В API-режиме данные всегда загружаются с сервера. В `localStorage` сохраняются
только выбранный режим, RSVP и настройки интерфейса, но не серверные голоса,
пользователи и результаты.

## Query-параметры

```text
?mode=participant
?mode=admin
?mode=screen
?demo=1                 # принудительно демо-режим
?api=http://host:8000   # явный адрес API
?dev_user_id=111        # локальная авторизация при DEV_MODE=1
?voted=1                # демо: сразу показать «Вы уже проголосовали»
?closed=1               # демо: сразу показать завершённое голосование
```

Параметры можно комбинировать:

```text
http://127.0.0.1:8000/?mode=screen&demo=1
http://127.0.0.1:8000/?mode=admin&dev_user_id=111
```

## API

Основные маршруты:

| Метод | Маршрут | Доступ |
| --- | --- | --- |
| `GET` | `/api/healthz` | публично |
| `POST` | `/api/auth/telegram` | публично, тело с `init_data` |
| `GET` | `/api/me` | участник |
| `GET` | `/api/event` | публично |
| `PATCH` | `/api/admin/event` | админ |
| `GET` | `/api/votings` | участник |
| `GET` | `/api/votings/{id}` | участник |
| `POST` | `/api/votings/{id}/vote` | участник |
| `GET` | `/api/live` | публично |
| `GET` | `/api/admin/participants` | админ |
| `PATCH` | `/api/admin/users/{id}/role` | админ |
| `POST` | `/api/admin/votings` | админ |
| `PATCH` | `/api/admin/votings/{id}` | админ |
| `DELETE` | `/api/admin/votings/{id}` | админ, только вопрос без голосов |
| `POST` | `/api/admin/votings/{id}/start` | админ |
| `POST` | `/api/admin/votings/{id}/close` | админ |
| `POST` | `/api/admin/votings/{id}/reopen` | админ |
| `POST` | `/api/admin/votings/{id}/options` | админ |
| `PATCH` | `/api/admin/options/{id}` | админ |
| `DELETE` | `/api/admin/options/{id}` | админ |

Правила:

- одновременно открыт только один вопрос;
- один участник может проголосовать один раз в каждом вопросе;
- удалить можно только вопрос в статусе `waiting`, в котором нет голосов;
- название и варианты вопроса можно менять только до запуска голосования;
- результаты публичного экрана не содержат персональных данных.

## Данные

SQLite-файл по умолчанию: `miniapp/backend/voting.db`.
Он исключён из Git через `backend/.gitignore`.

Таблицы:

- `users` — участники, `real_name`, `telegram_id`, `role`, `is_allowed`;
- `event_settings` — карточка мероприятия;
- `votings` — вопросы повестки;
- `voting_options` — варианты ответа;
- `votes` — голоса, уникальные в рамках `user_id + voting_id`.

Демо-состояние frontend хранится в `localStorage` под ключом
`sovet-starost-miniapp-v1`. Кнопка сброса в демо-режиме возвращает исходные
данные, а в API-режиме запрашивает свежие данные с сервера.

## Проверки

Backend:

```powershell
cd miniapp\backend
python -m py_compile database.py bot.py main.py
```

API smoke-check:

```powershell
Invoke-RestMethod http://127.0.0.1:8000/api/healthz
Invoke-RestMethod http://127.0.0.1:8000/api/live
```

При изменении frontend проверьте оба режима:

```text
http://127.0.0.1:4173/          # демо
http://127.0.0.1:8000/          # API
```

## Деплой на Render

Команда запуска backend:

```text
uvicorn main:app --host 0.0.0.0 --port $PORT
```

Чек-лист переменных окружения:

- `BOT_TOKEN` — токен Telegram-бота из `@BotFather`.
- `ADMIN_TG_ID` — Telegram ID главного администратора.
- `DATABASE_URL` — строка подключения к БД. Для Render Postgres/SQLite укажите значение хостинга.
- `MINIAPP_URL` — публичный HTTPS-адрес Mini App.
- `DEV_MODE=0` — обязательно выключить в production.
- `INVITE_CODE` — код или коды через запятую.
- `INIT_DATA_TTL_SECONDS=86400`.
- `EVENT_TITLE`, `EVENT_DATE`, `EVENT_TIME`, `EVENT_PLACE` — значения по умолчанию.

CORS настроен на приём запросов с любых доменов.

## Ограничения

- Это учебный прототип, а не production-система с аудитом и резервным копированием.
- SQLite подходит для одного небольшого заседания; для нескольких инстансов
  backend нужна внешняя БД.
- Бот использует `MemoryStorage`; состояние регистрации теряется при перезапуске.
- Реальная отправка сообщений Telegram и HTTPS Web App требуют настоящего
  `BOT_TOKEN` и публичного HTTPS-адреса.
- В демо-режиме голоса и результаты имитируются локально.
