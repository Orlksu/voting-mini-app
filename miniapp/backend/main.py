"""FastAPI backend for the council voting Mini App.

Responsibilities
----------------
* validate Telegram WebApp ``initData`` (HMAC-SHA256);
* return the current participant using ``users.real_name``;
* expose voting questions, options and results;
* accept one vote per participant per question;
* provide aggregate live data for the projector screen;
* provide admin endpoints for poll lifecycle and participants;
* serve the static Mini App frontend from the parent directory.

Run locally:
    uvicorn main:app --reload --port 8000
"""

from __future__ import annotations

import asyncio
import hashlib
import hmac
import json
import logging
import os
import time
from collections import Counter
from contextlib import asynccontextmanager
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Annotated, Literal
from urllib.parse import parse_qsl

from dotenv import load_dotenv
from fastapi import Depends, FastAPI, Header, HTTPException, Query, status
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import FileResponse
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel, Field, field_validator
from sqlalchemy import or_, select, text
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import Session, selectinload

from database import (
    EventSettings,
    Voting,
    VotingOption,
    VotingStatus,
    Vote,
    User,
    count_admins,
    count_registered_users,
    count_present_participants,
    get_db,
    get_or_create_user,
    get_user_by_id,
    get_user_by_telegram_id,
    promote_bootstrap_admin,
    seed_default_data,
    set_user_role,
    utcnow,
)

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

BACKEND_DIR = Path(__file__).resolve().parent
MINIAPP_DIR = BACKEND_DIR.parent
ASSETS_DIR = MINIAPP_DIR / "assets"

load_dotenv(MINIAPP_DIR / ".env")
load_dotenv(BACKEND_DIR / ".env", override=True)


def env_bool(name: str, default: bool = False) -> bool:
    raw = os.getenv(name)
    if raw is None:
        return default
    return raw.strip().lower() in {"1", "true", "yes", "on"}


BOT_TOKEN = os.getenv("BOT_TOKEN", "").strip()
ADMIN_TG_ID_RAW = os.getenv("ADMIN_TG_ID", "").strip()
try:
    ADMIN_TG_ID = int(ADMIN_TG_ID_RAW) if ADMIN_TG_ID_RAW else None
except ValueError:
    ADMIN_TG_ID = None

INIT_DATA_TTL_SECONDS = int(os.getenv("INIT_DATA_TTL_SECONDS", "86400"))
DEV_MODE = env_bool("DEV_MODE", False)
DEV_USER_NAME = os.getenv("DEV_USER_NAME", "Тестовый Участник").strip() or "Тестовый Участник"

# Для деплоя в интернет backend принимает запросы с любых доменов.
CORS_ORIGINS = ["*"]

EVENT = {
    "title": os.getenv("EVENT_TITLE", "Заседание Совета старост"),
    "date": os.getenv("EVENT_DATE", "2026-11-16"),
    "time": os.getenv("EVENT_TIME", "17:00"),
    "place": os.getenv("EVENT_PLACE", "Зал Учёного совета"),
}

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s | %(levelname)s | %(name)s | %(message)s",
)
logger = logging.getLogger(__name__)

# ---------------------------------------------------------------------------
# Pydantic schemas
# ---------------------------------------------------------------------------


class TelegramUser(BaseModel):
    id: int
    first_name: str | None = None
    last_name: str | None = None
    username: str | None = None
    language_code: str | None = None


class InitDataRequest(BaseModel):
    init_data: str = Field(min_length=1)


class EventOut(BaseModel):
    title: str
    date: str
    time: str
    place: str


class EventUpdate(BaseModel):
    title: str = Field(min_length=1, max_length=255)
    date: str = Field(min_length=1, max_length=32)
    time: str = Field(min_length=1, max_length=16)
    place: str = Field(min_length=1, max_length=255)

    @field_validator("title", "date", "time", "place")
    @classmethod
    def clean_text(cls, value: str) -> str:
        value = value.strip()
        if not value:
            raise ValueError("field cannot be empty")
        return value


class MeOut(BaseModel):
    id: int
    telegram_id: int
    real_name: str
    username: str | None
    role: str
    is_admin: bool
    is_allowed: bool
    will_attend: bool


class OptionCreate(BaseModel):
    label: str = Field(min_length=1, max_length=255)
    role: str | None = Field(default=None, max_length=255)

    @field_validator("label")
    @classmethod
    def clean_label(cls, value: str) -> str:
        value = value.strip()
        if not value:
            raise ValueError("option label cannot be empty")
        return value

    @field_validator("role")
    @classmethod
    def clean_role(cls, value: str | None) -> str | None:
        if value is None:
            return None
        value = value.strip()
        return value or None


class OptionUpdate(BaseModel):
    label: str | None = Field(default=None, min_length=1, max_length=255)
    role: str | None = Field(default=None, max_length=255)

    @field_validator("label")
    @classmethod
    def clean_label(cls, value: str | None) -> str | None:
        if value is None:
            return None
        value = value.strip()
        if not value:
            raise ValueError("option label cannot be empty")
        return value


class VotingCreate(BaseModel):
    title: str = Field(min_length=1, max_length=255)
    description: str | None = Field(default=None, max_length=2000)
    is_anonymous: bool = True
    options: list[OptionCreate] = Field(min_length=2)

    @field_validator("title")
    @classmethod
    def clean_title(cls, value: str) -> str:
        value = value.strip()
        if not value:
            raise ValueError("title cannot be empty")
        return value


class VotingUpdate(BaseModel):
    title: str | None = Field(default=None, min_length=1, max_length=255)
    description: str | None = Field(default=None, max_length=2000)
    is_anonymous: bool | None = None

    @field_validator("title")
    @classmethod
    def clean_title(cls, value: str | None) -> str | None:
        if value is None:
            return None
        value = value.strip()
        if not value:
            raise ValueError("title cannot be empty")
        return value


class VoteRequest(BaseModel):
    option_id: int = Field(gt=0)


class RoleUpdate(BaseModel):
    role: Literal["admin", "user"]


class RsvpUpdate(BaseModel):
    will_attend: bool


class OptionResult(BaseModel):
    id: int
    label: str
    role: str | None
    count: int
    share: float
    is_my_choice: bool = False


class VotingOut(BaseModel):
    id: int
    title: str
    description: str | None
    status: str
    is_anonymous: bool
    total_votes: int
    options: list[OptionResult]
    my_vote_option_id: int | None
    created_at: datetime
    started_at: datetime | None
    closed_at: datetime | None


class VoteResponse(BaseModel):
    ok: bool
    message: str
    option_id: int
    total_votes: int
    results: list[OptionResult]


class LiveOut(BaseModel):
    event: EventOut
    poll: VotingOut | None
    votings: list[VotingOut] = []
    registered: int
    present: int
    total_votes: int
    turnout: float
    leader: OptionResult | None
    winner: OptionResult | None
    is_tie: bool
    timeline: list[int]
    updated_at: datetime


class ParticipantOut(BaseModel):
    id: int
    telegram_id: int
    real_name: str
    username: str | None
    role: str
    is_allowed: bool
    will_attend: bool
    has_voted: bool
    voted_at: datetime | None
    created_at: datetime


class UserRoleOut(BaseModel):
    id: int
    telegram_id: int
    real_name: str
    username: str | None
    role: str
    is_allowed: bool


# ---------------------------------------------------------------------------
# Auth / initData validation
# ---------------------------------------------------------------------------


@dataclass
class CurrentUser:
    user: User
    telegram_id: int
    is_admin: bool


def validate_init_data(init_data: str) -> TelegramUser:
    """Validate Telegram WebApp initData and return the Telegram user.

    The algorithm is the one documented by Telegram:
    secret_key = HMAC_SHA256("WebAppData", bot_token)
    hash = HMAC_SHA256(secret_key, data_check_string)
    """
    if not init_data:
        logger.warning("Telegram initData is empty")
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Telegram initData is required",
        )

    if not BOT_TOKEN:
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail="BOT_TOKEN is not configured",
        )

    pairs = parse_qsl(init_data, keep_blank_values=True)
    data = dict(pairs)
    received_hash = data.pop("hash", None)
    if not received_hash:
        logger.warning("Telegram initData has no hash")
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Telegram initData has no hash",
        )

    data_check_string = "\n".join(
        f"{key}={value}" for key, value in sorted(data.items())
    )
    secret_key = hmac.new(
        b"WebAppData", BOT_TOKEN.encode("utf-8"), hashlib.sha256
    ).digest()
    calculated_hash = hmac.new(
        secret_key, data_check_string.encode("utf-8"), hashlib.sha256
    ).hexdigest()

    if not hmac.compare_digest(calculated_hash, received_hash):
        logger.warning("Telegram initData signature is invalid")
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Telegram initData signature is invalid",
        )

    try:
        auth_date = int(data.get("auth_date", "0"))
    except (TypeError, ValueError) as exc:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Telegram initData has an invalid auth_date",
        ) from exc

    now = int(time.time())
    if auth_date <= 0:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Telegram initData has no auth_date",
        )
    if INIT_DATA_TTL_SECONDS > 0 and now - auth_date > INIT_DATA_TTL_SECONDS:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Telegram initData has expired",
        )
    if auth_date - now > 60:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Telegram initData auth_date is in the future",
        )

    raw_user = data.get("user")
    if not raw_user:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Telegram initData has no user",
        )

    try:
        payload = json.loads(raw_user)
        tg_user = TelegramUser.model_validate(payload)
        logger.info("Telegram initData validated for user %s", tg_user.id)
    except (json.JSONDecodeError, ValueError) as exc:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Telegram initData user is invalid",
        ) from exc

    if tg_user.id <= 0:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Telegram initData user id is invalid",
        )
    return tg_user


def _resolve_authenticated_user(
    session: Session,
    telegram_id: int,
    *,
    allow_dev_create: bool = False,
) -> User:
    user = get_user_by_telegram_id(session, telegram_id)

    if user is None and allow_dev_create:
        user = get_or_create_user(
            session,
            telegram_id=telegram_id,
            username="dev",
            real_name=DEV_USER_NAME,
            is_allowed=True,
        )
        if ADMIN_TG_ID is not None and telegram_id == ADMIN_TG_ID:
            user.role = "admin"
        session.commit()

    if user is None:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="User is not registered. Open the bot and use the invite link.",
        )

    if not user.is_allowed:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="User access is disabled",
        )
    return user


def get_current_user(
    x_telegram_init_data: Annotated[
        str | None, Header(alias="X-Telegram-Init-Data")
    ] = None,
    dev_user_id: Annotated[int | None, Query(alias="dev_user_id")] = None,
    session: Session = Depends(get_db),
) -> CurrentUser:
    logger.info(
        "Auth request: initData present=%s, length=%s, dev_user_id=%s, dev_mode=%s",
        bool(x_telegram_init_data),
        len(x_telegram_init_data or ""),
        dev_user_id,
        DEV_MODE,
    )
    if x_telegram_init_data:
        telegram_user = validate_init_data(x_telegram_init_data)
        telegram_id = telegram_user.id
        allow_dev_create = False
    elif DEV_MODE and dev_user_id is not None:
        telegram_id = dev_user_id
        allow_dev_create = True
    else:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Telegram authorization is required",
        )

    user = _resolve_authenticated_user(
        session,
        telegram_id,
        allow_dev_create=allow_dev_create,
    )
    return CurrentUser(
        user=user,
        telegram_id=telegram_id,
        is_admin=user.role == "admin",
    )


def require_admin(current: CurrentUser = Depends(get_current_user)) -> CurrentUser:
    if not current.is_admin:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Administrator access is required",
        )
    return current


# ---------------------------------------------------------------------------
# Serialization helpers
# ---------------------------------------------------------------------------


def as_utc(value: datetime | None) -> datetime | None:
    if value is None:
        return None
    if value.tzinfo is None:
        return value.replace(tzinfo=timezone.utc)
    return value.astimezone(timezone.utc)


def me_out(user: User, is_admin: bool | None = None) -> MeOut:
    return MeOut(
        id=user.id,
        telegram_id=user.telegram_id,
        real_name=user.real_name,
        username=user.username,
        role=user.role,
        is_admin=user.role == "admin" if is_admin is None else is_admin,
        is_allowed=user.is_allowed,
        will_attend=user.will_attend,
    )


def ensure_event_settings(session: Session) -> EventSettings:
    settings = session.get(EventSettings, 1)
    if settings is None:
        settings = EventSettings(id=1, **EVENT)
        session.add(settings)
        session.commit()
    return settings


def event_out(session: Session) -> EventOut:
    settings = ensure_event_settings(session)
    return EventOut(
        title=settings.title,
        date=settings.date,
        time=settings.time,
        place=settings.place,
    )


def voting_query():
    return (
        select(Voting)
        .options(
            selectinload(Voting.options),
            selectinload(Voting.votes),
        )
        .execution_options(populate_existing=True)
    )


def get_voting_or_404(session: Session, voting_id: int) -> Voting:
    voting = session.scalar(
        voting_query().where(Voting.id == voting_id).limit(1)
    )
    if voting is None:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Voting not found",
        )
    return voting


def option_results(
    voting: Voting,
    *,
    my_vote_option_id: int | None = None,
) -> list[OptionResult]:
    total = len(voting.votes)
    counts = Counter(vote.option_id for vote in voting.votes)
    results: list[OptionResult] = []

    for option in sorted(voting.options, key=lambda item: (item.sort_order, item.id)):
        count = counts.get(option.id, 0)
        results.append(
            OptionResult(
                id=option.id,
                label=option.label,
                role=option.role,
                count=count,
                share=round(count * 100 / total, 1) if total else 0.0,
                is_my_choice=option.id == my_vote_option_id,
            )
        )
    return results


def voting_out(
    session: Session,
    voting: Voting,
    *,
    user_id: int | None = None,
) -> VotingOut:
    my_vote: Vote | None = None
    if user_id is not None:
        my_vote = session.scalar(
            select(Vote)
            .where(Vote.voting_id == voting.id, Vote.user_id == user_id)
            .limit(1)
        )

    my_option_id = my_vote.option_id if my_vote else None
    return VotingOut(
        id=voting.id,
        title=voting.title,
        description=voting.description,
        status=voting.status,
        is_anonymous=voting.is_anonymous,
        total_votes=len(voting.votes),
        options=option_results(voting, my_vote_option_id=my_option_id),
        my_vote_option_id=my_option_id,
        created_at=as_utc(voting.created_at) or utcnow(),
        started_at=as_utc(voting.started_at),
        closed_at=as_utc(voting.closed_at),
    )


def build_timeline(voting: Voting, points: int = 15) -> list[int]:
    """Cumulative vote counts for the activity chart."""
    votes = sorted(voting.votes, key=lambda item: as_utc(item.created_at) or utcnow())
    if not votes:
        return [0]

    start = as_utc(voting.started_at) or as_utc(votes[0].created_at) or utcnow()
    end = as_utc(voting.closed_at) or as_utc(votes[-1].created_at) or utcnow()
    if end <= start:
        end = start + timedelta(seconds=1)

    span = max((end - start).total_seconds(), 1.0)
    buckets = [0] * points
    for vote in votes:
        moment = as_utc(vote.created_at) or start
        index = int((moment - start).total_seconds() / span * (points - 1))
        buckets[min(points - 1, max(0, index))] += 1

    cumulative: list[int] = []
    total = 0
    for value in buckets:
        total += value
        cumulative.append(total)
    return cumulative


def resolve_live_voting(session: Session, poll_id: int | None) -> Voting | None:
    if poll_id is not None:
        return get_voting_or_404(session, poll_id)

    active = session.scalar(
        voting_query()
        .where(Voting.status == VotingStatus.OPEN.value)
        .order_by(Voting.started_at.desc(), Voting.id.desc())
        .limit(1)
    )
    if active is not None:
        return active

    return session.scalar(
        voting_query().order_by(Voting.id.desc()).limit(1)
    )


# ---------------------------------------------------------------------------
# Lifespan / app
# ---------------------------------------------------------------------------


@asynccontextmanager
async def lifespan(_app: FastAPI):
    await asyncio.to_thread(seed_default_data)
    if ADMIN_TG_ID is not None:
        promoted = await asyncio.to_thread(
            promote_bootstrap_admin, ADMIN_TG_ID
        )
        if promoted:
            logger.info("Bootstrap admin role assigned to %s", ADMIN_TG_ID)

    bot_task: asyncio.Task[None] | None = None
    if os.getenv("RUN_BOT_IN_API", "0") == "1":

        async def _run_bot() -> None:
            try:
                from bot import run_bot as run_telegram_bot

                await run_telegram_bot()
            except Exception:
                logger.exception("Telegram bot stopped with an error")

        try:
            bot_task = asyncio.create_task(_run_bot())
            logger.info("Telegram bot task created in the API process")
        except Exception:
            logger.exception("Failed to create Telegram bot task")

    yield

    if bot_task is not None:
        bot_task.cancel()
        try:
            await bot_task
        except asyncio.CancelledError:
            pass
        except Exception:
            logger.exception("Telegram bot stopped with an error")


app = FastAPI(
    title="Совет старост — Mini App API",
    version="1.0.0",
    lifespan=lifespan,
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=CORS_ORIGINS,
    allow_credentials=False,
    allow_methods=["*"],
    allow_headers=["*"],
)


# ---------------------------------------------------------------------------
# Health / auth / profile
# ---------------------------------------------------------------------------


@app.get("/healthz", include_in_schema=False)
@app.get("/api/healthz", tags=["system"])
def healthz(session: Session = Depends(get_db)) -> dict[str, str]:
    try:
        session.execute(text("SELECT 1"))
    except Exception as exc:  # pragma: no cover - defensive
        logger.exception("Database health check failed")
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail="Database is unavailable",
        ) from exc
    return {"status": "ok", "database": "ok"}


@app.post("/api/auth/telegram", response_model=MeOut, tags=["auth"])
def auth_telegram(
    payload: InitDataRequest,
    session: Session = Depends(get_db),
) -> MeOut:
    telegram_user = validate_init_data(payload.init_data)
    user = _resolve_authenticated_user(session, telegram_user.id)
    return me_out(user)


@app.get("/api/me", response_model=MeOut, tags=["auth"])
def get_me(current: CurrentUser = Depends(get_current_user)) -> MeOut:
    return me_out(current.user, current.is_admin)


@app.patch("/api/me/rsvp", response_model=MeOut, tags=["profile"])
def update_rsvp(
    payload: RsvpUpdate,
    current: CurrentUser = Depends(get_current_user),
    session: Session = Depends(get_db),
) -> MeOut:
    current.user.will_attend = payload.will_attend
    session.commit()
    session.refresh(current.user)
    return me_out(current.user, current.is_admin)


# ---------------------------------------------------------------------------
# Public event / participant voting
# ---------------------------------------------------------------------------


@app.get("/api/event", response_model=EventOut, tags=["event"])
def get_event(session: Session = Depends(get_db)) -> EventOut:
    return event_out(session)


@app.patch("/api/admin/event", response_model=EventOut, tags=["admin"])
def update_event(
    payload: EventUpdate,
    _current: CurrentUser = Depends(require_admin),
    session: Session = Depends(get_db),
) -> EventOut:
    settings = ensure_event_settings(session)
    settings.title = payload.title
    settings.date = payload.date
    settings.time = payload.time
    settings.place = payload.place
    session.commit()
    return event_out(session)


@app.get("/api/votings", response_model=list[VotingOut], tags=["voting"])
def list_votings(
    current: CurrentUser = Depends(get_current_user),
    session: Session = Depends(get_db),
) -> list[VotingOut]:
    votings = session.scalars(voting_query().order_by(Voting.id)).all()
    return [voting_out(session, voting, user_id=current.user.id) for voting in votings]


@app.get("/api/votings/{voting_id}", response_model=VotingOut, tags=["voting"])
def get_voting(
    voting_id: int,
    current: CurrentUser = Depends(get_current_user),
    session: Session = Depends(get_db),
) -> VotingOut:
    voting = get_voting_or_404(session, voting_id)
    return voting_out(session, voting, user_id=current.user.id)


@app.post(
    "/api/votings/{voting_id}/vote",
    response_model=VoteResponse,
    tags=["voting"],
)
def cast_vote(
    voting_id: int,
    payload: VoteRequest,
    current: CurrentUser = Depends(get_current_user),
    session: Session = Depends(get_db),
) -> VoteResponse:
    voting = get_voting_or_404(session, voting_id)

    if voting.status != VotingStatus.OPEN.value:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="Voting is not open",
        )

    if not current.user.will_attend:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Participant is marked as absent",
        )

    option = session.get(VotingOption, payload.option_id)
    if option is None or option.voting_id != voting.id:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Option does not belong to this voting",
        )

    existing = session.scalar(
        select(Vote)
        .where(Vote.voting_id == voting.id, Vote.user_id == current.user.id)
        .limit(1)
    )
    if existing is not None:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="You have already voted in this voting",
        )

    session.add(
        Vote(
            voting_id=voting.id,
            option_id=option.id,
            user_id=current.user.id,
        )
    )
    try:
        session.commit()
    except IntegrityError as exc:
        session.rollback()
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="You have already voted in this voting",
        ) from exc

    voting = get_voting_or_404(session, voting.id)
    results = option_results(
        voting,
        my_vote_option_id=option.id,
    )
    return VoteResponse(
        ok=True,
        message="Голос учтён",
        option_id=option.id,
        total_votes=len(voting.votes),
        results=results,
    )


# ---------------------------------------------------------------------------
# Projector screen live data
# ---------------------------------------------------------------------------


@app.get("/api/live", response_model=LiveOut, tags=["screen"])
def live_data(
    poll_id: int | None = Query(default=None, gt=0),
    session: Session = Depends(get_db),
) -> LiveOut:
    voting = resolve_live_voting(session, poll_id)
    registered = count_registered_users(session)
    present = count_present_participants(session)
    public_votings = [
        voting_out(session, item)
        for item in session.scalars(voting_query().order_by(Voting.id)).all()
    ]

    if voting is None:
        return LiveOut(
            event=event_out(session),
            poll=None,
            votings=public_votings,
            registered=registered,
            present=present,
            total_votes=0,
            turnout=0.0,
            leader=None,
            winner=None,
            is_tie=False,
            timeline=[0],
            updated_at=utcnow(),
        )

    results = option_results(voting)
    total = len(voting.votes)
    maximum = max((item.count for item in results), default=0)
    leaders = [item for item in results if maximum > 0 and item.count == maximum]
    is_tie = len(leaders) > 1

    leader = leaders[0] if len(leaders) == 1 else None
    winner = leader if voting.status == VotingStatus.CLOSED.value else None

    return LiveOut(
        event=event_out(session),
        poll=voting_out(session, voting),
        votings=public_votings,
        registered=registered,
        present=present,
        total_votes=total,
        turnout=round(total * 100 / registered, 1) if registered else 0.0,
        leader=leader,
        winner=winner,
        is_tie=is_tie,
        timeline=build_timeline(voting),
        updated_at=utcnow(),
    )


# ---------------------------------------------------------------------------
# Admin API
# ---------------------------------------------------------------------------


@app.get(
    "/api/admin/participants",
    response_model=list[ParticipantOut],
    tags=["admin"],
)
def admin_participants(
    search: str | None = Query(default=None, max_length=100),
    vote_filter: str = Query(
        default="all",
        alias="filter",
        pattern="^(all|voted|not_voted)$",
    ),
    role_filter: str = Query(
        default="all",
        alias="role",
        pattern="^(all|admin|user)$",
    ),
    _current: CurrentUser = Depends(require_admin),
    session: Session = Depends(get_db),
) -> list[ParticipantOut]:
    voting = resolve_live_voting(session, None)
    voted_at_by_user: dict[int, datetime | None] = {}

    if voting is not None:
        rows = session.execute(
            select(Vote.user_id, Vote.created_at).where(
                Vote.voting_id == voting.id
            )
        ).all()
        voted_at_by_user = {
            user_id: as_utc(created_at) for user_id, created_at in rows
        }

    query = select(User).order_by(User.real_name.asc())
    if search:
        needle = f"%{search.strip()}%"
        query = query.where(
            or_(
                User.real_name.ilike(needle),
                User.username.ilike(needle),
            )
        )

    participants: list[ParticipantOut] = []
    for user in session.scalars(query).all():
        has_voted = user.id in voted_at_by_user
        if vote_filter == "voted" and not has_voted:
            continue
        if vote_filter == "not_voted" and has_voted:
            continue
        if role_filter != "all" and user.role != role_filter:
            continue

        participants.append(
            ParticipantOut(
                id=user.id,
                telegram_id=user.telegram_id,
                real_name=user.real_name,
                username=user.username,
                role=user.role,
                is_allowed=user.is_allowed,
                will_attend=user.will_attend,
                has_voted=has_voted,
                voted_at=voted_at_by_user.get(user.id),
                created_at=as_utc(user.created_at) or utcnow(),
            )
        )
    return participants


@app.patch(
    "/api/admin/users/{user_id}/role",
    response_model=UserRoleOut,
    tags=["admin"],
)
def update_user_role(
    user_id: int,
    payload: RoleUpdate,
    _current: CurrentUser = Depends(require_admin),
    session: Session = Depends(get_db),
) -> UserRoleOut:
    user = get_user_by_id(session, user_id)
    if user is None:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="User not found",
        )

    if payload.role == "user" and user.role == "admin":
        if count_admins(session) <= 1:
            raise HTTPException(
                status_code=status.HTTP_409_CONFLICT,
                detail="Cannot demote the last administrator",
            )

    if payload.role != user.role:
        set_user_role(session, user, payload.role)
        session.commit()

    return UserRoleOut(
        id=user.id,
        telegram_id=user.telegram_id,
        real_name=user.real_name,
        username=user.username,
        role=user.role,
        is_allowed=user.is_allowed,
    )


@app.delete(
    "/api/admin/users/{user_id}",
    status_code=status.HTTP_204_NO_CONTENT,
    tags=["admin"],
)
def delete_participant(
    user_id: int,
    current: CurrentUser = Depends(require_admin),
    session: Session = Depends(get_db),
) -> None:
    user = get_user_by_id(session, user_id)
    if user is None:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="User not found",
        )
    if user.role == "admin":
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Cannot delete an administrator",
        )
    if user.id == current.user.id:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Cannot delete yourself",
        )
    session.delete(user)
    session.commit()

@app.post(
    "/api/admin/votings",
    response_model=VotingOut,
    status_code=status.HTTP_201_CREATED,
    tags=["admin"],
)
def create_voting(
    payload: VotingCreate,
    current: CurrentUser = Depends(require_admin),
    session: Session = Depends(get_db),
) -> VotingOut:
    voting = Voting(
        title=payload.title,
        description=payload.description,
        status=VotingStatus.WAITING.value,
        is_anonymous=payload.is_anonymous,
        created_by=current.telegram_id,
    )
    voting.options = [
        VotingOption(
            label=option.label,
            role=option.role,
            sort_order=index,
        )
        for index, option in enumerate(payload.options)
    ]
    session.add(voting)
    session.commit()
    voting = get_voting_or_404(session, voting.id)
    return voting_out(session, voting, user_id=current.user.id)


@app.delete(
    "/api/admin/votings/{voting_id}",
    status_code=status.HTTP_204_NO_CONTENT,
    tags=["admin"],
)
def delete_voting(
    voting_id: int,
    _current: CurrentUser = Depends(require_admin),
    session: Session = Depends(get_db),
) -> None:
    voting = get_voting_or_404(session, voting_id)

    if voting.status != VotingStatus.WAITING.value:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="Only a waiting voting can be deleted",
        )

    if voting.votes:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="A voting with votes cannot be deleted",
        )

    session.delete(voting)
    session.commit()


@app.patch(
    "/api/admin/votings/{voting_id}",
    response_model=VotingOut,
    tags=["admin"],
)
def update_voting(
    voting_id: int,
    payload: VotingUpdate,
    current: CurrentUser = Depends(require_admin),
    session: Session = Depends(get_db),
) -> VotingOut:
    voting = get_voting_or_404(session, voting_id)
    changes = payload.model_dump(exclude_unset=True)

    for field, value in changes.items():
        setattr(voting, field, value)

    session.commit()
    voting = get_voting_or_404(session, voting.id)
    return voting_out(session, voting, user_id=current.user.id)


@app.post(
    "/api/admin/votings/{voting_id}/start",
    response_model=VotingOut,
    tags=["admin"],
)
def start_voting(
    voting_id: int,
    current: CurrentUser = Depends(require_admin),
    session: Session = Depends(get_db),
) -> VotingOut:
    voting = get_voting_or_404(session, voting_id)

    if voting.status == VotingStatus.OPEN.value:
        return voting_out(session, voting, user_id=current.user.id)

    if voting.status == VotingStatus.CLOSED.value:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="Closed voting cannot be started; reopen it instead",
        )

    if len(voting.options) < 2:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Voting needs at least two options",
        )

    other_open = session.scalar(
        select(Voting)
        .where(
            Voting.status == VotingStatus.OPEN.value,
            Voting.id != voting.id,
        )
        .limit(1)
    )
    if other_open is not None:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="Another voting is already open",
        )

    voting.status = VotingStatus.OPEN.value
    voting.started_at = utcnow()
    voting.closed_at = None
    session.commit()
    voting = get_voting_or_404(session, voting.id)
    return voting_out(session, voting, user_id=current.user.id)


@app.post(
    "/api/admin/votings/{voting_id}/close",
    response_model=VotingOut,
    tags=["admin"],
)
def close_voting(
    voting_id: int,
    current: CurrentUser = Depends(require_admin),
    session: Session = Depends(get_db),
) -> VotingOut:
    voting = get_voting_or_404(session, voting_id)

    if voting.status == VotingStatus.CLOSED.value:
        return voting_out(session, voting, user_id=current.user.id)

    if voting.status != VotingStatus.OPEN.value:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="Only an open voting can be closed",
        )

    voting.status = VotingStatus.CLOSED.value
    voting.closed_at = utcnow()
    session.commit()
    voting = get_voting_or_404(session, voting.id)
    return voting_out(session, voting, user_id=current.user.id)


@app.post(
    "/api/admin/votings/{voting_id}/reopen",
    response_model=VotingOut,
    tags=["admin"],
)
def reopen_voting(
    voting_id: int,
    current: CurrentUser = Depends(require_admin),
    session: Session = Depends(get_db),
) -> VotingOut:
    voting = get_voting_or_404(session, voting_id)

    if voting.status == VotingStatus.OPEN.value:
        return voting_out(session, voting, user_id=current.user.id)

    other_open = session.scalar(
        select(Voting)
        .where(
            Voting.status == VotingStatus.OPEN.value,
            Voting.id != voting.id,
        )
        .limit(1)
    )
    if other_open is not None:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="Another voting is already open",
        )

    voting.status = VotingStatus.OPEN.value
    voting.closed_at = None
    if voting.started_at is None:
        voting.started_at = utcnow()
    session.commit()
    voting = get_voting_or_404(session, voting.id)
    return voting_out(session, voting, user_id=current.user.id)


def _ensure_voting_editable(voting: Voting) -> None:
    if voting.status != VotingStatus.WAITING.value:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="Voting can only be edited while it is waiting",
        )


@app.post(
    "/api/admin/votings/{voting_id}/options",
    response_model=VotingOut,
    status_code=status.HTTP_201_CREATED,
    tags=["admin"],
)
def add_option(
    voting_id: int,
    payload: OptionCreate,
    current: CurrentUser = Depends(require_admin),
    session: Session = Depends(get_db),
) -> VotingOut:
    voting = get_voting_or_404(session, voting_id)
    _ensure_voting_editable(voting)

    next_order = max((item.sort_order for item in voting.options), default=-1) + 1
    voting.options.append(
        VotingOption(
            label=payload.label,
            role=payload.role,
            sort_order=next_order,
        )
    )
    session.commit()
    voting = get_voting_or_404(session, voting.id)
    return voting_out(session, voting, user_id=current.user.id)


@app.patch(
    "/api/admin/options/{option_id}",
    response_model=VotingOut,
    tags=["admin"],
)
def update_option(
    option_id: int,
    payload: OptionUpdate,
    current: CurrentUser = Depends(require_admin),
    session: Session = Depends(get_db),
) -> VotingOut:
    option = session.get(VotingOption, option_id)
    if option is None:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Option not found",
        )

    voting = get_voting_or_404(session, option.voting_id)
    _ensure_voting_editable(voting)

    changes = payload.model_dump(exclude_unset=True)
    for field, value in changes.items():
        setattr(option, field, value)

    session.commit()
    voting = get_voting_or_404(session, voting.id)
    return voting_out(session, voting, user_id=current.user.id)


@app.delete(
    "/api/admin/options/{option_id}",
    response_model=VotingOut,
    tags=["admin"],
)
def delete_option(
    option_id: int,
    current: CurrentUser = Depends(require_admin),
    session: Session = Depends(get_db),
) -> VotingOut:
    option = session.get(VotingOption, option_id)
    if option is None:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Option not found",
        )

    voting = get_voting_or_404(session, option.voting_id)
    _ensure_voting_editable(voting)

    if len(voting.options) <= 2:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="A voting must keep at least two options",
        )

    if option.votes:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="Option already has votes",
        )

    session.delete(option)
    session.commit()
    voting = get_voting_or_404(session, voting.id)
    return voting_out(session, voting, user_id=current.user.id)


# ---------------------------------------------------------------------------
# Static frontend
# ---------------------------------------------------------------------------

FRONTEND_FILES = {"styles.css", "admin.css", "screen.css", "app.js"}

if ASSETS_DIR.exists():
    app.mount("/assets", StaticFiles(directory=ASSETS_DIR), name="assets")


@app.get("/", include_in_schema=False)
@app.get("/index.html", include_in_schema=False)
def frontend_index() -> FileResponse:
    return FileResponse(MINIAPP_DIR / "index.html")


@app.get("/{filename}", include_in_schema=False)
def frontend_file(filename: str) -> FileResponse:
    if filename not in FRONTEND_FILES:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="File not found",
        )
    path = MINIAPP_DIR / filename
    if not path.exists():
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="File not found",
        )
    return FileResponse(path)


if __name__ == "__main__":
    import uvicorn

    uvicorn.run(
        app,
        host=os.getenv("HOST", "0.0.0.0"),
        port=int(os.getenv("PORT", "8000")),
    )
