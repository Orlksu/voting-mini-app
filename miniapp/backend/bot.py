"""aiogram 3.x bot for the council voting Mini App.

Registration flow
-----------------
1. A participant follows an invite deep link: ``/start invite_pass``.
2. The bot asks for a real name and switches the FSM to ``waiting_for_name``.
3. The next text message is validated and stored in SQLite.
4. The bot confirms registration and sends a Mini App button.

``real_name`` from the users table is the only display name used by the
backend and the frontend.  Telegram profile names are never used for the
participant profile or the admin participants list.
"""

from __future__ import annotations

import asyncio
import html
import logging
import os
import re
from dataclasses import dataclass
from pathlib import Path

from aiogram import Bot, Dispatcher, F, Router
from aiogram.client.default import DefaultBotProperties
from aiogram.enums import ParseMode
from aiogram.exceptions import TelegramAPIError
from aiogram.filters import Command, CommandObject, CommandStart
from aiogram.fsm.context import FSMContext
from aiogram.fsm.state import State, StatesGroup
from aiogram.fsm.storage.memory import MemoryStorage
from aiogram.types import (
    BotCommand,
    InlineKeyboardButton,
    InlineKeyboardMarkup,
    Message,
    WebAppInfo,
)
from dotenv import load_dotenv

from database import (
    User,
    get_or_create_user,
    get_user_by_telegram_id,
    promote_bootstrap_admin,
    seed_default_data,
    session_scope,
)

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

BACKEND_DIR = Path(__file__).resolve().parent
PROJECT_DIR = BACKEND_DIR.parent

load_dotenv(PROJECT_DIR / ".env")
load_dotenv(BACKEND_DIR / ".env", override=True)

BOT_TOKEN = os.getenv("BOT_TOKEN", "").strip()
MINIAPP_URL = os.getenv("MINIAPP_URL", "http://127.0.0.1:4173/").strip()
DEFAULT_INVITE_CODE = "invite_pass"


def _parse_admin_id(raw: str | None) -> int | None:
    if not raw or not raw.strip():
        return None
    try:
        return int(raw.strip())
    except ValueError:
        logging.warning("ADMIN_TG_ID is not a valid integer: %r", raw)
        return None


ADMIN_TG_ID = _parse_admin_id(os.getenv("ADMIN_TG_ID"))


def _invite_codes() -> set[str]:
    raw = os.getenv("INVITE_CODE", DEFAULT_INVITE_CODE)
    return {item.strip() for item in raw.split(",") if item.strip()}


INVITE_CODES = _invite_codes()

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s | %(levelname)s | %(name)s | %(message)s",
)
logger = logging.getLogger(__name__)

router = Router()
dp = Dispatcher(storage=MemoryStorage())
dp.include_router(router)


# ---------------------------------------------------------------------------
# FSM
# ---------------------------------------------------------------------------


class Registration(StatesGroup):
    waiting_for_name = State()


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class UserInfo:
    telegram_id: int
    real_name: str
    username: str | None
    is_allowed: bool
    role: str


def user_to_info(user: User) -> UserInfo:
    return UserInfo(
        telegram_id=user.telegram_id,
        real_name=user.real_name,
        username=user.username,
        is_allowed=user.is_allowed,
        role=user.role,
    )


def fetch_user(telegram_id: int) -> UserInfo | None:
    """Read a user in a short-lived session (safe to call from a thread)."""
    with session_scope() as session:
        user = get_user_by_telegram_id(session, telegram_id)
        return user_to_info(user) if user else None


def register_user(
    telegram_id: int,
    username: str | None,
    real_name: str,
) -> UserInfo:
    """Create or update the user and mark them as allowed."""
    with session_scope() as session:
        user = get_or_create_user(
            session,
            telegram_id=telegram_id,
            username=username,
            real_name=real_name,
            is_allowed=True,
        )
        # The bootstrap admin from .env always gets the admin role during
        # registration.  Other admins are managed through the admin panel.
        if is_bootstrap_admin(telegram_id):
            user.role = "admin"
        return user_to_info(user)


def is_bootstrap_admin(telegram_id: int) -> bool:
    return ADMIN_TG_ID is not None and telegram_id == ADMIN_TG_ID


def user_is_admin(user: UserInfo) -> bool:
    return user.role == "admin"


def normalize_real_name(raw: str) -> str | None:
    """Return a cleaned ``Имя Фамилия`` or ``None`` for invalid input."""
    name = " ".join((raw or "").split())
    if not 3 <= len(name) <= 100:
        return None

    parts = name.split(" ")
    if len(parts) < 2:
        return None

    # Letters, hyphens and apostrophes; at least two words.
    pattern = re.compile(r"[A-Za-zА-Яа-яЁё][A-Za-zА-Яа-яЁё\-']*")
    if any(not pattern.fullmatch(part) for part in parts):
        return None
    return name


def mini_app_url(mode: str | None = None) -> str:
    url = MINIAPP_URL
    if mode:
        separator = "&" if "?" in url else "?"
        url = f"{url}{separator}mode={mode}"
    return url


def _mini_app_button(text: str, url: str) -> InlineKeyboardButton:
    # Telegram requires HTTPS for Web App buttons.  For local development we
    # fall back to a regular URL button so the flow can still be tested.
    if url.startswith("https://"):
        return InlineKeyboardButton(text=text, web_app=WebAppInfo(url=url))
    return InlineKeyboardButton(text=text, url=url)


def mini_app_keyboard(*, admin: bool = False) -> InlineKeyboardMarkup:
    rows = [[_mini_app_button("Открыть Mini App", mini_app_url())]]
    if admin:
        rows.append([_mini_app_button("Панель администратора", mini_app_url("admin"))])
    return InlineKeyboardMarkup(inline_keyboard=rows)


async def send_main_menu(message: Message, user: UserInfo) -> None:
    await message.answer(
        f"С возвращением, <b>{html.escape(user.real_name)}</b>!\n\n"
        "Открой Mini App, чтобы посмотреть повестку и проголосовать.",
        reply_markup=mini_app_keyboard(admin=user_is_admin(user)),
    )


async def notify_admin_about_registration(bot: Bot, user: UserInfo) -> None:
    if ADMIN_TG_ID is None or user.telegram_id == ADMIN_TG_ID:
        return

    text = f"🆕 Новый участник: <b>{html.escape(user.real_name)}</b>"
    if user.username:
        text += f" (@{html.escape(user.username)})"

    try:
        await bot.send_message(ADMIN_TG_ID, text)
    except TelegramAPIError:
        logger.exception("Could not notify admin about a new registration")


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------


@router.message(CommandStart())
async def cmd_start(
    message: Message,
    command: CommandObject,
    state: FSMContext,
) -> None:
    if message.from_user is None:
        return

    telegram_id = message.from_user.id
    payload = (command.args or "").strip()

    user = await asyncio.to_thread(fetch_user, telegram_id)
    admin = user_is_admin(user) if user else is_bootstrap_admin(telegram_id)

    # Already registered participants go straight to the Mini App.
    if user and user.is_allowed and user.real_name:
        await state.clear()
        await send_main_menu(message, user)
        return

    # A non-admin can only register with a valid invite code.
    if payload and payload not in INVITE_CODES:
        await message.answer(
            "Ссылка-приглашение недействительна или устарела.\n"
            "Попроси новую ссылку у администратора Совета."
        )
        return

    if not payload and not admin:
        await message.answer(
            "Регистрация доступна только по пригласительной ссылке.\n"
            "Попроси ссылку у администратора Совета старост."
        )
        return

    # New participant, reactivated participant, or an unregistered admin.
    await state.set_state(Registration.waiting_for_name)
    await state.update_data(
        invite_code=payload or ("admin" if admin else ""),
    )
    await message.answer(
        "Введи свои Имя и Фамилию для списка участников.\n\n"
        "Например: <b>Иван Иванов</b>"
    )


@router.message(Registration.waiting_for_name, F.text)
async def process_real_name(message: Message, state: FSMContext) -> None:
    if message.from_user is None:
        return

    real_name = normalize_real_name(message.text or "")
    if real_name is None:
        await message.answer(
            "Похоже, это не имя и фамилия.\n"
            "Введи текстом, например: <b>Иван Иванов</b>"
        )
        return

    user = await asyncio.to_thread(
        register_user,
        message.from_user.id,
        message.from_user.username,
        real_name,
    )
    await state.clear()

    await message.answer(
        f"✅ Регистрация завершена, <b>{html.escape(user.real_name)}</b>!\n\n"
        "Ты добавлен в список участников. Открой Mini App, чтобы посмотреть "
        "повестку и проголосовать.",
        reply_markup=mini_app_keyboard(admin=user_is_admin(user)),
    )
    await notify_admin_about_registration(message.bot, user)


@router.message(Registration.waiting_for_name)
async def process_real_name_non_text(message: Message) -> None:
    await message.answer(
        "Пришли имя и фамилию обычным текстом, например: <b>Иван Иванов</b>"
    )


@router.message(Command("admin"))
async def cmd_admin(message: Message, state: FSMContext) -> None:
    if message.from_user is None:
        return

    telegram_id = message.from_user.id
    user = await asyncio.to_thread(fetch_user, telegram_id)

    if user is None:
        if is_bootstrap_admin(telegram_id):
            await state.set_state(Registration.waiting_for_name)
            await state.update_data(invite_code="admin")
            await message.answer(
                "Ты администратор, но ещё не в списке участников.\n"
                "Введи свои Имя и Фамилию для списка участников."
            )
        else:
            await message.answer("Эта команда доступна только администратору.")
        return

    if not user_is_admin(user):
        await message.answer("Эта команда доступна только администратору.")
        return

    if not user.is_allowed or not user.real_name:
        await state.set_state(Registration.waiting_for_name)
        await state.update_data(invite_code="admin")
        await message.answer(
            "Ты администратор, но ещё не в списке участников.\n"
            "Введи свои Имя и Фамилию для списка участников."
        )
        return

    await state.clear()
    await message.answer(
        f"Панель администратора, <b>{html.escape(user.real_name)}</b>.\n\n"
        "Открой Mini App в режиме администратора, чтобы управлять повесткой "
        "и голосованиями.",
        reply_markup=mini_app_keyboard(admin=True),
    )


@router.message(Command("invite"))
async def cmd_invite(message: Message) -> None:
    if message.from_user is None:
        return

    user = await asyncio.to_thread(fetch_user, message.from_user.id)
    if user is None or not user_is_admin(user):
        await message.answer("Эта команда доступна только администратору.")
        return

    bot = message.bot
    me = await bot.get_me()
    code = sorted(INVITE_CODES)[0] if INVITE_CODES else DEFAULT_INVITE_CODE
    link = f"https://t.me/{me.username}?start={code}"

    await message.answer(
        "Пригласительная ссылка для участников:\n\n"
        f"<code>{html.escape(link)}</code>\n\n"
        "По ней участник зарегистрируется и получит кнопку Mini App."
    )


@router.message(Command("cancel"))
async def cmd_cancel(message: Message, state: FSMContext) -> None:
    current = await state.get_state()
    if current is None:
        await message.answer("Нечего отменять.")
        return

    await state.clear()
    await message.answer("Регистрация отменена. Начать заново: /start invite_pass")


# ---------------------------------------------------------------------------
# Entrypoint
# ---------------------------------------------------------------------------


async def main() -> None:
    if not BOT_TOKEN:
        raise RuntimeError(
            "BOT_TOKEN is not set. Copy .env.example to .env and fill BOT_TOKEN."
        )

    # Create the SQLite schema and the demo agenda on first launch.
    await asyncio.to_thread(seed_default_data)
    if ADMIN_TG_ID is not None:
        promoted = await asyncio.to_thread(
            promote_bootstrap_admin, ADMIN_TG_ID
        )
        if promoted:
            logger.info("Bootstrap admin role assigned to %s", ADMIN_TG_ID)

    bot = Bot(
        token=BOT_TOKEN,
        default=DefaultBotProperties(parse_mode=ParseMode.HTML),
    )
    try:
        await bot.set_my_commands(
            [
                BotCommand(command="start", description="Открыть Mini App"),
                BotCommand(command="admin", description="Панель администратора"),
                BotCommand(command="invite", description="Ссылка-приглашение"),
                BotCommand(command="cancel", description="Отменить регистрацию"),
            ]
        )
        await bot.delete_webhook(drop_pending_updates=True)
        logger.info("Bot started")
        await dp.start_polling(bot)
    finally:
        await bot.session.close()


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except (KeyboardInterrupt, SystemExit):
        logger.info("Bot stopped")
