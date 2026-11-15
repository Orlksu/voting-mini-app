"""SQLAlchemy models and SQLite setup for the voting Mini App.

The module is shared by the FastAPI backend (``main.py``) and the
aiogram bot (``bot.py``).  It intentionally uses synchronous SQLAlchemy:
the project is small, SQLite is local, and this keeps both processes
simple and easy to run on a single VM.
"""

from __future__ import annotations

import os
from contextlib import contextmanager
from datetime import datetime, timezone
from enum import Enum
from pathlib import Path
from typing import Iterator

from dotenv import load_dotenv
from sqlalchemy import (
    BigInteger,
    Boolean,
    CheckConstraint,
    DateTime,
    ForeignKey,
    String,
    Text,
    UniqueConstraint,
    create_engine,
    event,
    func,
    inspect,
    select,
    text,
)
from sqlalchemy.engine import Engine
from sqlalchemy.orm import (
    DeclarativeBase,
    Mapped,
    Session,
    mapped_column,
    relationship,
    sessionmaker,
)

# ---------------------------------------------------------------------------
# Paths and environment
# ---------------------------------------------------------------------------

BACKEND_DIR = Path(__file__).resolve().parent
PROJECT_DIR = BACKEND_DIR.parent

# .env is expected next to the frontend (miniapp/.env), while a backend-local
# .env is also supported for running the API/bot from this directory.
load_dotenv(PROJECT_DIR / ".env")
load_dotenv(BACKEND_DIR / ".env", override=True)

DATABASE_URL = os.getenv(
    "DATABASE_URL",
    f"sqlite:///{(BACKEND_DIR / 'voting.db').as_posix()}",
)

# SQLite needs check_same_thread=False because FastAPI/uvicorn may use worker
# threads and aiogram runs handlers in its own event loop.
connect_args = {"check_same_thread": False} if DATABASE_URL.startswith("sqlite") else {}

engine: Engine = create_engine(
    DATABASE_URL,
    connect_args=connect_args,
    pool_pre_ping=True,
    future=True,
)

SessionLocal = sessionmaker(
    bind=engine,
    autoflush=False,
    autocommit=False,
    expire_on_commit=False,
    future=True,
)


@event.listens_for(engine, "connect")
def _enable_sqlite_foreign_keys(dbapi_connection, _connection_record) -> None:
    """SQLite disables foreign keys by default; enable them per connection."""
    if DATABASE_URL.startswith("sqlite"):
        cursor = dbapi_connection.cursor()
        cursor.execute("PRAGMA foreign_keys=ON")
        cursor.close()


class Base(DeclarativeBase):
    pass


def utcnow() -> datetime:
    return datetime.now(timezone.utc)


# ---------------------------------------------------------------------------
# Enums / constants
# ---------------------------------------------------------------------------


class VotingStatus(str, Enum):
    WAITING = "waiting"
    OPEN = "open"
    CLOSED = "closed"


# ---------------------------------------------------------------------------
# Models
# ---------------------------------------------------------------------------


class User(Base):
    """A registered council member.

    ``real_name`` is the name entered by the user during the invite flow and
    is the single source of truth for the participant profile and the admin
    participants list.  Telegram profile names are not used for display.
    """

    __tablename__ = "users"
    __table_args__ = (
        CheckConstraint("role IN ('user', 'admin')", name="ck_users_role"),
    )

    id: Mapped[int] = mapped_column(primary_key=True, autoincrement=True)
    telegram_id: Mapped[int] = mapped_column(
        BigInteger, unique=True, nullable=False, index=True
    )
    username: Mapped[str | None] = mapped_column(String(64), nullable=True)
    real_name: Mapped[str] = mapped_column(String(255), nullable=False)
    role: Mapped[str] = mapped_column(
        String(16),
        nullable=False,
        default="user",
        server_default="user",
        index=True,
    )
    is_allowed: Mapped[bool] = mapped_column(
        Boolean, nullable=False, default=True, server_default="1"
    )
    will_attend: Mapped[bool] = mapped_column(
        Boolean, nullable=False, default=True, server_default="1"
    )
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow, onupdate=utcnow
    )

    votes: Mapped[list["Vote"]] = relationship(
        back_populates="user", cascade="all, delete-orphan"
    )

    @property
    def is_admin(self) -> bool:
        return self.role == "admin"

    def __repr__(self) -> str:  # pragma: no cover - debugging helper
        return f"<User id={self.id} telegram_id={self.telegram_id} name={self.real_name!r}>"


class EventSettings(Base):
    """Single meeting card shown to participants and on the projector."""

    __tablename__ = "event_settings"

    id: Mapped[int] = mapped_column(primary_key=True, default=1)
    title: Mapped[str] = mapped_column(
        String(255), nullable=False, default="Заседание Совета старост"
    )
    date: Mapped[str] = mapped_column(String(32), nullable=False, default="")
    time: Mapped[str] = mapped_column(String(16), nullable=False, default="")
    place: Mapped[str] = mapped_column(String(255), nullable=False, default="")
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow, onupdate=utcnow
    )

    def __repr__(self) -> str:  # pragma: no cover - debugging helper
        return f"<EventSettings title={self.title!r} date={self.date!r}>"


class Voting(Base):
    """A poll / voting question shown in the Mini App."""

    __tablename__ = "votings"
    __table_args__ = (
        CheckConstraint(
            "status IN ('waiting', 'open', 'closed')",
            name="ck_votings_status",
        ),
    )

    id: Mapped[int] = mapped_column(primary_key=True, autoincrement=True)
    title: Mapped[str] = mapped_column(String(255), nullable=False)
    description: Mapped[str | None] = mapped_column(Text, nullable=True)
    status: Mapped[str] = mapped_column(
        String(16),
        nullable=False,
        default=VotingStatus.WAITING.value,
        server_default=VotingStatus.WAITING.value,
        index=True,
    )
    is_anonymous: Mapped[bool] = mapped_column(
        Boolean, nullable=False, default=True, server_default="1"
    )
    created_by: Mapped[int | None] = mapped_column(BigInteger, nullable=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow
    )
    started_at: Mapped[datetime | None] = mapped_column(
        DateTime(timezone=True), nullable=True
    )
    closed_at: Mapped[datetime | None] = mapped_column(
        DateTime(timezone=True), nullable=True
    )

    options: Mapped[list["VotingOption"]] = relationship(
        back_populates="voting",
        cascade="all, delete-orphan",
        order_by="VotingOption.sort_order",
    )
    votes: Mapped[list["Vote"]] = relationship(
        back_populates="voting", cascade="all, delete-orphan"
    )

    @property
    def total_votes(self) -> int:
        return len(self.votes)

    def __repr__(self) -> str:  # pragma: no cover - debugging helper
        return f"<Voting id={self.id} title={self.title!r} status={self.status!r}>"


class VotingOption(Base):
    """An answer option belonging to a voting."""

    __tablename__ = "voting_options"

    id: Mapped[int] = mapped_column(primary_key=True, autoincrement=True)
    voting_id: Mapped[int] = mapped_column(
        ForeignKey("votings.id", ondelete="CASCADE"), nullable=False, index=True
    )
    label: Mapped[str] = mapped_column(String(255), nullable=False)
    role: Mapped[str | None] = mapped_column(String(255), nullable=True)
    sort_order: Mapped[int] = mapped_column(nullable=False, default=0)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow
    )

    voting: Mapped["Voting"] = relationship(back_populates="options")
    votes: Mapped[list["Vote"]] = relationship(back_populates="option")

    def __repr__(self) -> str:  # pragma: no cover - debugging helper
        return f"<VotingOption id={self.id} label={self.label!r}>"


class Vote(Base):
    """One user's vote in one voting (unique per user per voting)."""

    __tablename__ = "votes"
    __table_args__ = (
        UniqueConstraint("voting_id", "user_id", name="uq_vote_voting_user"),
    )

    id: Mapped[int] = mapped_column(primary_key=True, autoincrement=True)
    voting_id: Mapped[int] = mapped_column(
        ForeignKey("votings.id", ondelete="CASCADE"), nullable=False, index=True
    )
    option_id: Mapped[int] = mapped_column(
        ForeignKey("voting_options.id", ondelete="CASCADE"), nullable=False, index=True
    )
    user_id: Mapped[int] = mapped_column(
        ForeignKey("users.id", ondelete="CASCADE"), nullable=False, index=True
    )
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow
    )

    voting: Mapped["Voting"] = relationship(back_populates="votes")
    option: Mapped["VotingOption"] = relationship(back_populates="votes")
    user: Mapped["User"] = relationship(back_populates="votes")

    def __repr__(self) -> str:  # pragma: no cover - debugging helper
        return f"<Vote voting_id={self.voting_id} user_id={self.user_id} option_id={self.option_id}>"


# ---------------------------------------------------------------------------
# Database helpers
# ---------------------------------------------------------------------------


def init_db() -> None:
    """Create all tables and apply the small SQLite role migration.

    ``create_all`` does not add columns to existing tables, so an older
    database gets the ``users.role`` column through a safe ALTER TABLE.
    """
    Base.metadata.create_all(bind=engine)

    inspector = inspect(engine)
    if "users" not in inspector.get_table_names():
        return

    columns = {column["name"] for column in inspector.get_columns("users")}

    if "role" not in columns:
        with engine.begin() as connection:
            connection.execute(
                text(
                    "ALTER TABLE users ADD COLUMN role VARCHAR(16) "
                    "NOT NULL DEFAULT 'user'"
                )
            )
            connection.execute(
                text("UPDATE users SET role = 'user' WHERE role IS NULL")
            )
            connection.execute(
                text("CREATE INDEX IF NOT EXISTS ix_users_role ON users (role)")
            )

    if "will_attend" not in columns:
        with engine.begin() as connection:
            connection.execute(
                text(
                    "ALTER TABLE users ADD COLUMN will_attend BOOLEAN "
                    "NOT NULL DEFAULT 1"
                )
            )
            connection.execute(
                text("UPDATE users SET will_attend = 1 WHERE will_attend IS NULL")
            )


@contextmanager
def session_scope() -> Iterator[Session]:
    """Transactional scope for bot handlers and scripts."""
    session = SessionLocal()
    try:
        yield session
        session.commit()
    except Exception:
        session.rollback()
        raise
    finally:
        session.close()


def get_db() -> Iterator[Session]:
    """FastAPI dependency."""
    session = SessionLocal()
    try:
        yield session
    finally:
        session.close()


def get_user_by_telegram_id(
    session: Session, telegram_id: int
) -> User | None:
    return session.scalar(select(User).where(User.telegram_id == telegram_id))


def get_user_by_id(session: Session, user_id: int) -> User | None:
    return session.get(User, user_id)


def count_admins(session: Session) -> int:
    return (
        session.scalar(
            select(func.count(User.id)).where(User.role == "admin")
        )
        or 0
    )


def set_user_role(session: Session, user: User, role: str) -> User:
    if role not in {"user", "admin"}:
        raise ValueError("role must be 'user' or 'admin'")
    user.role = role
    session.flush()
    return user


def promote_bootstrap_admin(telegram_id: int | None) -> bool:
    """Assign the env ``ADMIN_TG_ID`` the admin role on application start.

    Returns ``True`` when an existing user was promoted.  The user is not
    created here: a real name is still collected by the bot registration
    flow, and the role is assigned when that registration completes.
    """
    if telegram_id is None:
        return False

    init_db()
    with session_scope() as session:
        user = get_user_by_telegram_id(session, telegram_id)
        if user is None:
            return False
        if user.role == "admin":
            return False
        user.role = "admin"
        return True


def get_or_create_user(
    session: Session,
    *,
    telegram_id: int,
    username: str | None = None,
    real_name: str | None = None,
    is_allowed: bool = True,
) -> User:
    """Create a user or refresh mutable Telegram fields.

    ``real_name`` is only overwritten when a non-empty value is supplied, so
    /start without an invite does not erase a name collected during FSM
    registration.
    """
    user = get_user_by_telegram_id(session, telegram_id)
    if user is None:
        user = User(
            telegram_id=telegram_id,
            username=username,
            real_name=(real_name or "").strip() or "Участник",
            is_allowed=is_allowed,
        )
        session.add(user)
        session.flush()
        return user

    user.username = username
    if real_name and real_name.strip():
        user.real_name = real_name.strip()
    user.is_allowed = is_allowed
    session.flush()
    return user


def count_registered_users(session: Session) -> int:
    """Number of allowed participants, excluding administrators."""
    return (
        session.scalar(
            select(func.count(User.id)).where(
                User.is_allowed.is_(True),
                User.role == "user",
            )
        )
        or 0
    )


def count_present_participants(session: Session) -> int:
    """Number of allowed participants who marked themselves as present."""
    return (
        session.scalar(
            select(func.count(User.id)).where(
                User.is_allowed.is_(True),
                User.role == "user",
                User.will_attend.is_(True),
            )
        )
        or 0
    )


def get_active_voting(session: Session) -> Voting | None:
    """Return the currently open voting, if any."""
    return session.scalar(
        select(Voting)
        .where(Voting.status == VotingStatus.OPEN.value)
        .order_by(Voting.started_at.desc(), Voting.id.desc())
        .limit(1)
    )


def seed_default_data() -> None:
    """Insert the demo agenda on first run.

    Real votes are not seeded: every option starts at zero, and numbers come
    from actual Mini App submissions.
    """
    init_db()
    with session_scope() as session:
        existing = session.scalar(select(Voting.id).limit(1))
        if existing is not None:
            return

        chair = Voting(
            title="Выборы председателя",
            description="Выберите кандидата на должность председателя Совета старост.",
            status=VotingStatus.OPEN.value,
            is_anonymous=True,
            started_at=utcnow(),
        )
        chair.options = [
            VotingOption(label="Анна Соколова", role="Студент 4 курса · ИГСУ", sort_order=0),
            VotingOption(label="Дмитрий Лебедев", role="Студент 3 курса · ИОН", sort_order=1),
            VotingOption(label="Мария Орлова", role="Студент 2 курса · ИГСУ", sort_order=2),
        ]

        plan = Voting(
            title="Утверждение плана работы на 2027 год",
            description="Повестка следующего вопроса.",
            status=VotingStatus.WAITING.value,
            is_anonymous=True,
        )
        plan.options = [
            VotingOption(label="Утвердить план", role="Работа секций и форумов", sort_order=0),
            VotingOption(
                label="Утвердить с правками",
                role="Доработать в рабочем порядке",
                sort_order=1,
            ),
            VotingOption(label="Отклонить", role="Вернуться к обсуждению", sort_order=2),
        ]

        forum = Voting(
            title="Формат зимнего форума",
            description="Повестка следующего вопроса.",
            status=VotingStatus.WAITING.value,
            is_anonymous=True,
        )
        forum.options = [
            VotingOption(label="Форум в декабре", role="До новогодних праздников", sort_order=0),
            VotingOption(label="Перенести на январь", role="После сессии", sort_order=1),
            VotingOption(label="Смешанный формат", role="Очно и онлайн", sort_order=2),
        ]

        session.add_all([chair, plan, forum])


if __name__ == "__main__":
    seed_default_data()
    print(f"Database is ready: {DATABASE_URL}")
