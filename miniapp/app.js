'use strict';

/* ============================================================
   Runtime / backend connection
   ============================================================ */
const params = new URLSearchParams(window.location.search);
const telegramApp = window.Telegram && window.Telegram.WebApp ? window.Telegram.WebApp : null;
if (telegramApp) {
  try {
    telegramApp.ready();
    telegramApp.expand();
  } catch (error) {
    /* Telegram WebApp is optional; the static prototype still works. */
  }
}

const runtime = {
  api: false,
  me: null,
  participants: [],
  live: null,
  initData: telegramApp && typeof telegramApp.initData === 'string' ? telegramApp.initData : '',
  devUserId: params.get('dev_user_id') || null,
  forceDemo: params.get('demo') === '1',
  refreshing: false,
  saveTimers: new Map(),
  authError: null,
  lastSync: null,
};
const API_BASE = (params.get('api') || '').replace(/\/+$/, '');

/* ============================================================
   State
   ============================================================ */
const STORAGE_KEY = 'sovet-starost-miniapp-v1';
const REGISTERED = 45;
const PRESENT = 45;

const PEOPLE = [
  'Анна Смирнова', 'Дмитрий Лебедев', 'Мария Орлова', 'Александр Иванов',
  'Екатерина Морозова', 'Илья Лебедев', 'София Ковалёва', 'Максим Петров',
  'Полина Волкова', 'Артём Соколов', 'Дарья Кузнецова', 'Никита Попов',
  'Алиса Григорьева', 'Михаил Новиков', 'Виктория Зайцева', 'Роман Фёдоров',
  'Ксения Михайлова', 'Глеб Беляев', 'Елизавета Тарасова', 'Тимофей Орлов',
  'Арина Комарова', 'Сергей Киселёв', 'Валерия Макарова', 'Егор Андреев',
  'Мария Ильина', 'Владислав Гусев', 'Анастасия Титова', 'Кирилл Козлов',
  'Полина Степанова', 'Денис Николаев', 'Ангелина Сергеева', 'Иван Романов',
  'Софья Виноградова', 'Матвей Кузьмин', 'Ксения Фролова', 'Лев Борисов',
  'Ева Павлова', 'Артём Семёнов', 'Алина Голубева', 'Марк Соловьёв',
  'Таисия Ефимова', 'Ярослав Дмитриев', 'Варвара Алексеева', 'Пётр Сорокин',
  'Юлия Смирнова',
];
const ROLES = [
  'Член Совета · ИГСУ',
  'Член Совета · ИОН',
  'Член Совета · ИПиНБ',
  'Член Совета · Факультет ГМУ',
];

const ICONS = {
  calendar: '<rect x="3" y="5" width="18" height="16" rx="3"/><path d="M8 3v4M16 3v4M3 10h18"/>',
  clock: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
  pin: '<path d="M20 10c0 5-8 11-8 11S4 15 4 10a8 8 0 1 1 16 0Z"/><circle cx="12" cy="10" r="2.4"/>',
  users: '<path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75"/>',
  user: '<circle cx="12" cy="8" r="4"/><path d="M4 21a8 8 0 0 1 16 0"/>',
  check: '<path d="m5 12 4 4L19 6"/>',
  checkCircle: '<circle cx="12" cy="12" r="9"/><path d="m8.5 12 2.5 2.5 4.5-5"/>',
  ballot: '<rect x="4" y="4" width="16" height="16" rx="3"/><path d="m8 12 2.5 2.5L16 9"/>',
  monitor: '<rect x="3" y="4" width="18" height="13" rx="2"/><path d="M8 21h8M12 17v4"/>',
  play: '<path d="m8 5 11 7-11 7V5Z"/>',
  stop: '<rect x="6" y="6" width="12" height="12" rx="2"/>',
  plus: '<path d="M12 5v14M5 12h14"/>',
  trash: '<path d="M3 6h18M8 6V4h8v2M19 6l-1 15H6L5 6M10 11v6M14 11v6"/>',
  arrowRight: '<path d="M5 12h14M13 6l6 6-6 6"/>',
  arrowUpRight: '<path d="M7 17 17 7M7 7h10v10"/>',
  chevronRight: '<path d="m9 6 6 6-6 6"/>',
  chevronLeft: '<path d="m15 6-6 6 6 6"/>',
  search: '<circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/>',
  settings: '<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .34 1.87l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.7 1.7 0 0 0-1.87-.34 1.7 1.7 0 0 0-1.03 1.56V21a2 2 0 1 1-4 0v-.09A1.7 1.7 0 0 0 8.96 19.4a1.7 1.7 0 0 0-1.87.34l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.7 1.7 0 0 0 4.6 15a1.7 1.7 0 0 0-1.56-1.03H3a2 2 0 1 1 0-4h.09A1.7 1.7 0 0 0 4.6 8.96a1.7 1.7 0 0 0-.34-1.87l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.7 1.7 0 0 0 8.96 4.6 1.7 1.7 0 0 0 10 3.04V3a2 2 0 1 1 4 0v.09a1.7 1.7 0 0 0 1.03 1.56 1.7 1.7 0 0 0 1.87-.34l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06A1.7 1.7 0 0 0 19.4 8.96c.26.63.87 1.04 1.56 1.04H21a2 2 0 1 1 0 4h-.09A1.7 1.7 0 0 0 19.4 15Z"/>',
  trophy: '<path d="M8 21h8M12 17v4M7 4h10v5a5 5 0 0 1-10 0V4Z"/><path d="M7 6H4v2a4 4 0 0 0 4 4M17 6h3v2a4 4 0 0 1-4 4"/>',
  crown: '<path d="m3 7 4.5 3L12 4l4.5 6L21 7l-2 12H5L3 7Z"/><path d="M5 19h14"/>',
  chart: '<path d="M4 20V10M10 20V4M16 20v-7M22 20H2"/>',
  home: '<path d="m3 11 9-8 9 8v9a1 1 0 0 1-1 1h-5v-6H9v6H4a1 1 0 0 1-1-1v-9Z"/>',
  list: '<path d="M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01"/>',
  archive: '<rect x="3" y="4" width="18" height="4" rx="1"/><path d="M5 8v12h14V8M10 12h4"/>',
  lock: '<rect x="4" y="10" width="16" height="11" rx="2"/><path d="M8 10V7a4 4 0 0 1 8 0v3"/>',
  eye: '<path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12Z"/><circle cx="12" cy="12" r="3"/>',
  eyeOff: '<path d="m3 3 18 18M10.6 10.6a2 2 0 0 0 2.8 2.8M9.9 5.2A10.6 10.6 0 0 1 12 5c6.5 0 10 7 10 7a17.6 17.6 0 0 1-3.2 4.2M6.2 6.2A17.7 17.7 0 0 0 2 12s3.5 7 10 7a10.7 10.7 0 0 0 4.2-.85"/>',
  close: '<path d="M6 6l12 12M18 6 6 18"/>',
  refresh: '<path d="M21 12a9 9 0 1 1-2.64-6.36M21 3v6h-6"/>',
  sparkles: '<path d="m12 3 1.5 4.5L18 9l-4.5 1.5L12 15l-1.5-4.5L6 9l4.5-1.5L12 3ZM19 14l.8 2.2L22 17l-2.2.8L19 20l-.8-2.2L16 17l2.2-.8L19 14Z"/>',
  shield: '<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10Z"/>',
  logout: '<path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9"/>',
  grip: '<path d="M9 5h.01M15 5h.01M9 12h.01M15 12h.01M9 19h.01M15 19h.01"/>',
  fullscreen: '<path d="M8 3H3v5M16 3h5v5M21 16v5h-5M8 21H3v-5"/>',
  info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v5M12 8h.01"/>',
  vote: '<path d="M9 11l2 2 4-4"/><rect x="3" y="4" width="18" height="16" rx="3"/>',
};

const state = loadState();
const views = {
  participant: document.getElementById('view-participant'),
  admin: document.getElementById('view-admin'),
  screen: document.getElementById('view-screen'),
};
let toastTimer = 0;

/* ============================================================
   Utils
   ============================================================ */
function esc(value) {
  return String(value ?? '').replace(/[&<>"']/g, (ch) => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
  }[ch]));
}
function icon(name, cls = '') {
  return `<svg class="icon ${cls}" viewBox="0 0 24 24" aria-hidden="true">${ICONS[name] || ''}</svg>`;
}
function pad(value) {
  return String(value).padStart(2, '0');
}
function newId(prefix = 'id') {
  return `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2, 6)}`;
}
function plural(n, one, few, many) {
  const mod10 = n % 10;
  const mod100 = n % 100;
  if (mod10 === 1 && mod100 !== 11) return one;
  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 10 || mod100 >= 20)) return few;
  return many;
}
function initials(name) {
  return String(name).split(' ').filter(Boolean).slice(0, 2).map((part) => part[0]).join('').toUpperCase();
}
function pct(part, whole) {
  return whole ? Math.round((part / whole) * 100) : 0;
}
function dateRu(dateStr, withYear = false) {
  const date = new Date(`${dateStr}T12:00:00`);
  if (Number.isNaN(date.getTime())) return dateStr;
  return new Intl.DateTimeFormat('ru-RU', {
    day: 'numeric',
    month: 'long',
    ...(withYear ? { year: 'numeric' } : {}),
  }).format(date);
}
function weekdayRu(dateStr) {
  const date = new Date(`${dateStr}T12:00:00`);
  if (Number.isNaN(date.getTime())) return '';
  return new Intl.DateTimeFormat('ru-RU', { weekday: 'long' }).format(date);
}
function dateBadge(dateStr) {
  const date = new Date(`${dateStr}T12:00:00`);
  if (Number.isNaN(date.getTime())) return '—';
  return new Intl.DateTimeFormat('ru-RU', { day: '2-digit', month: 'short' })
    .format(date)
    .replace('.', '')
    .toUpperCase();
}
function timeShort() {
  return new Intl.DateTimeFormat('ru-RU', { hour: '2-digit', minute: '2-digit' }).format(new Date());
}
function timeFull() {
  return new Intl.DateTimeFormat('ru-RU', { hour: '2-digit', minute: '2-digit', second: '2-digit' }).format(new Date());
}
function todayPlus(days) {
  const date = new Date();
  date.setDate(date.getDate() + days);
  return date.toISOString().slice(0, 10);
}
function isEditing() {
  const el = document.activeElement;
  return Boolean(el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.tagName === 'SELECT'));
}
function isAdminUser() {
  return Boolean(runtime.api && runtime.me && runtime.me.is_admin);
}
function enforceRoleMode() {
  if (!runtime.api || !runtime.me) return;
  if (runtime.me.is_admin) {
    if (state.mode === 'participant') state.mode = 'admin';
    return;
  }
  state.mode = 'participant';
}
function toast(message) {
  const el = document.getElementById('toast');
  if (!el) return;
  el.textContent = message;
  el.classList.add('show');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => el.classList.remove('show'), 3400);
}

/* ============================================================
   API client
   ============================================================ */
function apiUrl(path) {
  const url = new URL(path, API_BASE || window.location.origin);
  if (runtime.devUserId) url.searchParams.set('dev_user_id', runtime.devUserId);
  return url.toString();
}

async function apiRequest(path, options = {}) {
  const { method = 'GET', body, timeout = 10000 } = options;
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeout);
  const headers = { Accept: 'application/json' };
  if (runtime.initData) headers['X-Telegram-Init-Data'] = runtime.initData;
  if (body !== undefined) headers['Content-Type'] = 'application/json';

  try {
    const response = await fetch(apiUrl(path), {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      signal: controller.signal,
    });
    const text = await response.text();
    let data = null;
    if (text) {
      try {
        data = JSON.parse(text);
      } catch (error) {
        data = text;
      }
    }
    if (!response.ok) {
      const detail = data && typeof data === 'object' && data.detail ? data.detail : `HTTP ${response.status}`;
      const message = Array.isArray(detail)
        ? detail.map((item) => item.msg || '').filter(Boolean).join('; ') || 'Проверьте заполнение полей.'
        : String(detail);
      const error = new Error(message);
      error.status = response.status;
      throw error;
    }
    return data;
  } catch (error) {
    if (error.name === 'AbortError') {
      const timeoutError = new Error('Сервер не ответил вовремя.');
      timeoutError.status = 0;
      throw timeoutError;
    }
    throw error;
  } finally {
    clearTimeout(timer);
  }
}

function apiErrorMessage(error) {
  const detail = String(error && error.message ? error.message : error);
  const messages = {
    'Telegram authorization is required': 'Открой мини-приложение через Telegram.',
    'User is not registered. Open the bot and use the invite link.': 'Аккаунт не зарегистрирован. Открой бота по пригласительной ссылке.',
    'User access is disabled': 'Доступ к голосованию отключён.',
    'Administrator access is required': 'Это действие доступно только администратору.',
    'Another voting is already open': 'Сначала завершите текущее голосование.',
    'Only an open voting can be closed': 'Завершить можно только открытое голосование.',
    'Closed voting cannot be started; reopen it instead': 'Завершённое голосование можно только открыть заново.',
    'Voting is not open': 'Голосование уже завершено.',
    'You have already voted in this voting': 'Вы уже проголосовали по этому вопросу.',
    'Cannot demote the last administrator': 'В системе должен остаться хотя бы один администратор.',
    'A voting must keep at least two options': 'В вопросе должно остаться минимум два варианта.',
    'Voting options cannot be changed while voting is open': 'Варианты нельзя менять, пока идёт голосование.',
    'Voting needs at least two options': 'Добавьте минимум два варианта ответа.',
    'Attendance response is already confirmed': 'Ответ уже подтверждён. Изменить его нельзя.',
    'Participant must confirm attendance before voting': 'Сначала подтвердите присутствие на заседании.',
  };
  return messages[detail] || detail;
}

async function canUseApi() {
  if (runtime.forceDemo) return false;
  // Render and other free hosts can need more than a couple of seconds to wake
  // up. A single very short check made the app fall back to demo mode even
  // when the backend was only cold-starting.
  for (let attempt = 0; attempt < 2; attempt += 1) {
    try {
      const response = await fetch(apiUrl('/api/healthz'), {
        headers: { Accept: 'application/json' },
        signal: AbortSignal.timeout ? AbortSignal.timeout(8000) : undefined,
      });
      if (response.ok) return true;
    } catch (error) {
      // Retry once after a short pause before deciding that the backend is down.
    }
    if (attempt === 0) {
      await new Promise((resolve) => setTimeout(resolve, 1500));
    }
  }
  return false;
}

function mapApiVoting(voting) {
  return {
    id: String(voting.id),
    title: voting.title,
    description: voting.description || '',
    status: voting.status,
    options: (voting.options || []).map((option) => ({
      id: String(option.id),
      label: option.label,
      role: option.role || '',
      count: Number(option.count) || 0,
    })),
    timeline: [Number(voting.total_votes) || 0],
    startsAt: voting.started_at ? Date.parse(voting.started_at) : null,
    endsAt: voting.closed_at ? Date.parse(voting.closed_at) : null,
    myVoteOptionId: voting.my_vote_option_id == null ? null : String(voting.my_vote_option_id),
  };
}

function applyEvent(event) {
  if (!event) return;
  state.event = {
    title: event.title || state.event.title,
    date: event.date || state.event.date,
    time: event.time || state.event.time,
    place: event.place || state.event.place,
  };
}

async function loadPublicScreenState() {
  const [event, live] = await Promise.all([
    apiRequest('/api/event'),
    apiRequest('/api/live'),
  ]);
  applyEvent(event);
  runtime.live = live;
  state.registered = Number(live.registered) || 0;
  state.present = Number(live.present) || 0;
  const publicVotings = Array.isArray(live.votings) && live.votings.length
    ? live.votings
    : live.poll
      ? [live.poll]
      : [];
  if (publicVotings.length) {
    state.polls = publicVotings.map(mapApiVoting);
    state.activePollId = live.poll ? String(live.poll.id) : state.polls[0].id;
    mergeLivePoll(live);
  }
}

function mergeLivePoll(live) {
  if (!live || !live.poll) return;
  const pollId = String(live.poll.id);
  const poll = state.polls.find((item) => item.id === pollId);
  if (poll && Array.isArray(live.timeline) && live.timeline.length) {
    poll.timeline = live.timeline.map((value) => Number(value) || 0);
  }
}

async function loadApiState() {
  let me;
  try {
    me = await apiRequest('/api/me');
  } catch (error) {
    if (error.status === 401 || error.status === 403) {
      runtime.me = null;
      runtime.authError = error;
    }
    throw error;
  }

  runtime.me = me;
  runtime.authError = null;
  enforceRoleMode();
  state.participant.rsvpConfirmed = Boolean(me.rsvp_confirmed);
  state.participant.rsvp = state.participant.rsvpConfirmed
    ? (me.will_attend ? 'yes' : 'no')
    : null;
  state.participant.rsvpDraft = state.participant.rsvp;

  const event = await apiRequest('/api/event');
  applyEvent(event);

  const [votings, live] = await Promise.all([
    apiRequest('/api/votings'),
    apiRequest('/api/live'),
  ]);
  runtime.live = live;

  const previousActive = state.activePollId;
  state.polls = votings.map(mapApiVoting);
  state.registered = Number(live.registered) || 0;
  state.present = Number(live.present) || 0;

  if (!state.polls.length) {
    state.activePollId = null;
  } else {
    const open = state.polls.find((poll) => poll.status === 'open');
    const previous = state.polls.find((poll) => poll.id === previousActive);
    if (!previous) {
      state.activePollId = (open || state.polls[0]).id;
    }
    if (state.mode === 'participant' && open) {
      state.activePollId = open.id;
    }
  }

  mergeLivePoll(live);

  if (me.is_admin) {
    try {
      runtime.participants = await apiRequest('/api/admin/participants');
    } catch (error) {
      runtime.participants = [];
    }
  } else {
    runtime.participants = [];
  }

  const active = activePoll();
  state.participant.name = me.real_name;
  state.participant.role = me.is_admin ? 'Администратор' : 'Член Совета';
  state.participant.votedPollId = active.myVoteOptionId != null ? active.id : null;
  state.participant.choiceId = active.myVoteOptionId != null ? String(active.myVoteOptionId) : null;
  state.participant.votedAt = null;

  if (state.participant.votedPollId === active.id) {
    state.participant.selectedId = null;
  } else if (active.options.some((option) => option.id === state.participant.selectedId)) {
    // Keep the current selection during background refreshes.
  } else {
    state.participant.selectedId = null;
  }

  runtime.lastSync = Date.now();
}

async function refreshApi(options = {}) {
  if (!runtime.api) return false;
  if (runtime.refreshing) return false;
  runtime.refreshing = true;
  try {
    await loadApiState();
    return true;
  } catch (error) {
    if (!options.silent) toast(apiErrorMessage(error));
    return false;
  } finally {
    runtime.refreshing = false;
  }
}

function renderCurrentMode() {
  if (state.mode === 'admin') {
    if (!isEditing()) renderAdmin();
  } else if (state.mode === 'screen') {
    renderScreen();
  } else {
    renderParticipant();
  }
  applyMode();
}

function applyRuntimeUi() {
  const label = document.querySelector('[data-connection-label]');
  if (label) {
    label.textContent = runtime.api ? 'Сервер' : 'Демо';
    label.title = runtime.api
      ? 'Данные синхронизируются с FastAPI'
      : 'Локальный демонстрационный режим';
  }

  const admin = isAdminUser();
  const participantAllowed = !runtime.api || !admin;
  const adminAllowed = !runtime.api || admin;

  document.querySelectorAll('.demo-seg [data-mode="participant"]').forEach((button) => {
    button.hidden = !participantAllowed;
    button.disabled = !participantAllowed;
  });
  document.querySelectorAll('.demo-seg [data-mode="admin"]').forEach((button) => {
    button.hidden = !adminAllowed;
    button.disabled = !adminAllowed;
  });
  document.querySelectorAll('.demo-seg [data-mode="screen"]').forEach((button) => {
    button.hidden = !adminAllowed;
    button.disabled = !adminAllowed;
  });

  const modeSwitch = document.querySelector('.demo-switch');
  if (modeSwitch) modeSwitch.hidden = runtime.api && Boolean(runtime.me) && !admin;

  const reset = document.querySelector('[data-action="reset-demo"]');
  if (reset) {
    const title = runtime.api ? 'Обновить данные с сервера' : 'Сбросить демо';
    reset.title = title;
    reset.setAttribute('aria-label', title);
  }
}

/* ============================================================
   State helpers
   ============================================================ */
function makeDefaultState() {
  const now = Date.now();
  return {
    mode: 'participant',
    event: {
      title: 'Заседание Совета старост',
      date: '2026-11-16',
      time: '17:00',
      place: 'Зал Учёного совета',
    },
    polls: [
      {
        id: 'poll-chair',
        title: 'Выборы председателя',
        status: 'open',
        options: [
          { id: 'opt-sokolova', label: 'Анна Соколова', role: 'Студент 4 курса · ИГСУ', count: 10 },
          { id: 'opt-lebedev', label: 'Дмитрий Лебедев', role: 'Студент 3 курса · ИОН', count: 23 },
          { id: 'opt-orlova', label: 'Мария Орлова', role: 'Студент 2 курса · ИГСУ', count: 6 },
        ],
        timeline: [6, 8, 11, 13, 16, 19, 22, 25, 28, 31, 33, 35, 37, 38, 39],
        startsAt: now - 9 * 60 * 1000,
        endsAt: now + 5 * 60 * 1000,
      },
      {
        id: 'poll-plan',
        title: 'Утверждение плана работы на 2027 год',
        status: 'waiting',
        options: [
          { id: 'opt-plan-yes', label: 'Утвердить план', role: 'Работа секций и форумов', count: 0 },
          { id: 'opt-plan-edits', label: 'Утвердить с правками', role: 'Доработать в рабочем порядке', count: 0 },
          { id: 'opt-plan-no', label: 'Отклонить', role: 'Вернуться к обсуждению', count: 0 },
        ],
        timeline: [0],
        startsAt: null,
        endsAt: null,
      },
      {
        id: 'poll-forum',
        title: 'Формат зимнего форума',
        status: 'waiting',
        options: [
          { id: 'opt-forum-dec', label: 'Форум в декабре', role: 'До новогодних праздников', count: 0 },
          { id: 'opt-forum-jan', label: 'Перенести на январь', role: 'После сессии', count: 0 },
          { id: 'opt-forum-mix', label: 'Смешанный формат', role: 'Очно и онлайн', count: 0 },
        ],
        timeline: [0],
        startsAt: null,
        endsAt: null,
      },
    ],
    activePollId: 'poll-chair',
    participant: {
      name: 'Анна Смирнова',
      role: 'Член Совета · ИГСУ',
      votedPollId: null,
      choiceId: null,
      votedAt: null,
      rsvp: null,
      rsvpDraft: null,
      rsvpConfirmed: false,
      selectedId: null,
    },
    registered: PEOPLE.length - 1,
    present: PEOPLE.length - 1,
    adminIndexes: [0],
    removedDemoIndexes: [],
    adminFilter: 'all',
    adminSearch: '',
    previewTab: 'live',
    showResults: true,
  };
}

function loadState() {
  const base = makeDefaultState();
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return base;
    const saved = JSON.parse(raw);
    if (!saved || typeof saved !== 'object') return base;
    const merged = {
      ...base,
      ...saved,
      event: { ...base.event, ...(saved.event || {}) },
      participant: { ...base.participant, ...(saved.participant || {}) },
    };
    // Older demo saves kept only ``rsvp``.  They predate the explicit
    // confirmation step, so ask the participant once instead of silently
    // treating the old value as confirmed.
    if (typeof merged.participant.rsvpConfirmed !== 'boolean') {
      merged.participant.rsvpConfirmed = false;
      merged.participant.rsvp = null;
      merged.participant.rsvpDraft = null;
    } else if (merged.participant.rsvpConfirmed) {
      if (!['yes', 'no'].includes(merged.participant.rsvp)) {
        merged.participant.rsvp = ['yes', 'no'].includes(merged.participant.rsvpDraft)
          ? merged.participant.rsvpDraft
          : null;
      }
      merged.participant.rsvpDraft = merged.participant.rsvp;
    } else {
      merged.participant.rsvp = null;
      if (!['yes', 'no'].includes(merged.participant.rsvpDraft)) {
        merged.participant.rsvpDraft = null;
      }
    }
    if (!Array.isArray(merged.polls) || merged.polls.length === 0) merged.polls = base.polls;
    merged.adminIndexes = Array.isArray(merged.adminIndexes)
      ? merged.adminIndexes.filter((index) => Number.isInteger(index) && index >= 0 && index < PEOPLE.length)
      : [0];
    if (!merged.adminIndexes.length) merged.adminIndexes = [0];
    merged.removedDemoIndexes = Array.isArray(merged.removedDemoIndexes)
      ? merged.removedDemoIndexes.filter((index) => Number.isInteger(index) && index >= 0 && index < PEOPLE.length)
      : [];
    merged.polls = merged.polls.map((poll) => ({
      ...poll,
      options: Array.isArray(poll.options) && poll.options.length ? poll.options : [],
      timeline: Array.isArray(poll.timeline) && poll.timeline.length ? poll.timeline : [0],
      startsAt: poll.startsAt || null,
      endsAt: poll.endsAt || null,
    }));
    const savedPolls = Array.isArray(saved.polls) && saved.polls.length > 0;
    if (!savedPolls && saved.activePollId != null) {
      // API-mode persistence keeps only the selected id; validate it after the
      // server responds instead of replacing it with a demo poll id.
      merged.activePollId = saved.activePollId;
    } else if (!merged.polls.some((poll) => poll.id === merged.activePollId)) {
      merged.activePollId = merged.polls[0].id;
    }
    merged.polls.forEach((poll) => {
      if (poll.status === 'open' && (!poll.endsAt || poll.endsAt < Date.now())) {
        poll.endsAt = Date.now() + 5 * 60 * 1000;
      }
    });
    if (!['admin', 'participant', 'screen'].includes(merged.mode)) merged.mode = 'participant';
    return merged;
  } catch (error) {
    return base;
  }
}

function save() {
  try {
    // Raw demo data stays in the browser, but API data must always come from
    // the server. In API mode only UI preferences and the local RSVP survive.
    const payload = runtime.api
      ? {
          mode: state.mode,
          activePollId: state.activePollId,
          participant: {
            rsvp: state.participant.rsvp,
            rsvpDraft: state.participant.rsvpDraft,
            rsvpConfirmed: state.participant.rsvpConfirmed,
          },
          adminSearch: state.adminSearch,
          adminFilter: state.adminFilter,
          previewTab: state.previewTab,
          showResults: state.showResults,
        }
      : state;
    localStorage.setItem(STORAGE_KEY, JSON.stringify(payload));
  } catch (error) {
    /* localStorage can be unavailable in private mode — the prototype still works. */
  }
}

function emptyPoll() {
  return {
    id: 'empty',
    title: 'Повестка пока пуста',
    description: '',
    status: 'waiting',
    options: [],
    timeline: [0],
    startsAt: null,
    endsAt: null,
  };
}
function activePoll() {
  return state.polls.find((poll) => poll.id === state.activePollId) || state.polls[0] || emptyPoll();
}
function participantIsAdmin(index) {
  return state.adminIndexes.includes(index);
}
function adminCount() {
  if (runtime.api) {
    return runtime.participants.filter((user) => user.role === 'admin').length;
  }
  return state.adminIndexes.length;
}
function pollById(id) {
  return state.polls.find((poll) => poll.id === id);
}
function totalVotes(poll) {
  return poll ? poll.options.reduce((sum, option) => sum + (Number(option.count) || 0), 0) : 0;
}
function maxVotes(poll) {
  return poll ? Math.max(0, ...poll.options.map((option) => Number(option.count) || 0)) : 0;
}
function leaderOption(poll) {
  if (!poll) return null;
  const maximum = maxVotes(poll);
  if (!maximum) return null;
  const leaders = poll.options.filter((option) => (Number(option.count) || 0) === maximum);
  return leaders.length === 1 ? leaders[0] : null;
}
function leadersList(poll) {
  if (!poll) return [];
  const maximum = maxVotes(poll);
  if (!maximum) return [];
  return poll.options.filter((option) => (Number(option.count) || 0) === maximum);
}
function statusMeta(poll) {
  if (!poll) return { label: 'Черновик', tone: 'wait', icon: 'clock' };
  if (poll.status === 'open') return { label: 'Идёт голосование', tone: 'live', icon: 'vote' };
  if (poll.status === 'closed') return { label: 'Голосование завершено', tone: 'done', icon: 'checkCircle' };
  return { label: 'Ожидает запуска', tone: 'wait', icon: 'clock' };
}
function statusBadge(poll) {
  const meta = statusMeta(poll);
  return `<span class="badge badge--${meta.tone}">${meta.tone === 'live' ? '<i></i>' : icon(meta.icon)}${meta.label}</span>`;
}
function pollIndex(poll) {
  return state.polls.findIndex((item) => item.id === poll.id) + 1;
}
function countdownLabel() {
  const poll = activePoll();
  if (!poll || poll.status !== 'open' || !poll.endsAt) return '—';
  const seconds = Math.max(0, Math.floor((poll.endsAt - Date.now()) / 1000));
  return `${pad(Math.floor(seconds / 60))}:${pad(seconds % 60)}`;
}
function eventMetaMarkup() {
  return `
    <span>${icon('calendar')}${esc(dateRu(state.event.date))}, ${esc(weekdayRu(state.event.date))}</span>
    <span>${icon('clock')}${esc(state.event.time)} МСК</span>
    <span>${icon('pin')}${esc(state.event.place)}</span>`;
}
function titleMarkup(title) {
  const safe = esc(title).trim();
  const lastSpace = safe.lastIndexOf(' ');
  if (lastSpace < 0) return `<em>${safe}</em>`;
  return `${safe.slice(0, lastSpace)} <em>${safe.slice(lastSpace + 1)}</em>`;
}
function heroTitleMarkup(title) {
  const words = String(title).trim().split(/\s+/);
  if (words.length < 3) return esc(title);
  return `${esc(words.slice(0, -2).join(' '))}<br><em>${esc(words.slice(-2).join(' '))}</em>`;
}
function pollVoteCountLabel(poll) {
  const total = totalVotes(poll);
  return `${total} ${plural(total, 'голос', 'голоса', 'голосов')}`;
}

/* ============================================================
   Participant screen
   ============================================================ */
function participantHero(poll) {
  const total = totalVotes(poll);
  const meta = statusMeta(poll);
  const people = state.registered;
  const answered = state.participant.rsvpConfirmed;
  const selectedRsvp = answered ? state.participant.rsvp : state.participant.rsvpDraft;
  const yesSelected = selectedRsvp === 'yes';
  const noSelected = selectedRsvp === 'no';
  return `
    <section class="mini-hero">
      <span class="mini-hero__grid" aria-hidden="true"></span>
      <span class="mini-hero__orbit" aria-hidden="true"></span>
      <span class="mini-hero__watermark" aria-hidden="true">${esc(dateBadge(state.event.date).split(' ')[0])}</span>
      <div class="mini-hero__top">
        <span class="badge badge--${meta.tone}">${meta.tone === 'live' ? '<i></i>' : icon(meta.icon)}${meta.label}</span>
        <span class="mini-hero__date">${esc(dateBadge(state.event.date))}</span>
      </div>
      <h1 class="mini-hero__title">${heroTitleMarkup(state.event.title)}</h1>
      <div class="mini-hero__meta">
        <span>${icon('calendar')}${esc(dateRu(state.event.date))}, ${esc(weekdayRu(state.event.date))}</span>
        <span>${icon('clock')}${esc(state.event.time)} МСК · ${icon('pin')}${esc(state.event.place)}</span>
      </div>
      <div class="mini-hero__foot mini-hero__foot--rsvp">
        <div class="rsvp-card">
          <div class="rsvp-card__top">
            <span class="rsvp-card__eyebrow">${icon('users')}Присутствие</span>
            ${answered ? `<span class="rsvp-card__locked">${icon('lock')}Ответ подтверждён</span>` : ''}
          </div>
          <h3>Будете ли вы присутствовать?</h3>
          <div class="rsvp-card__choices">
            <button type="button" class="rsvp-choice rsvp-choice--yes${yesSelected ? ' is-selected' : ''}" data-action="select-rsvp" data-rsvp="yes" ${answered ? 'disabled' : ''}>
              ${icon('check')}Да
            </button>
            <button type="button" class="rsvp-choice rsvp-choice--no${noSelected ? ' is-selected' : ''}" data-action="select-rsvp" data-rsvp="no" ${answered ? 'disabled' : ''}>
              ${icon('close')}Нет
            </button>
          </div>
          ${answered
            ? `<div class="rsvp-card__confirmed">${icon('lock')}Ваш ответ: <b>${state.participant.rsvp === 'yes' ? 'Да' : 'Нет'}</b>. Изменить его нельзя.</div>`
            : `<button type="button" class="btn btn--primary btn--wide rsvp-card__confirm" data-action="confirm-rsvp" ${selectedRsvp ? '' : 'disabled'}>${icon('check')}Подтвердить ответ</button>`}
        </div>
        <span class="mini-hero__people">${icon('users')}${people} ${plural(people, 'участник', 'участника', 'участников')} · ${total} ${plural(total, 'голос', 'голоса', 'голосов')}</span>
      </div>
    </section>`;
}

function participantAgenda() {
  return state.polls.map((poll, index) => {
    const isActive = poll.id === activePoll().id;
    const meta = statusMeta(poll);
    const content = `
      <span class="mini-agenda__num">${pad(index + 1)}</span>
      <span class="mini-agenda__copy">
        <b>${esc(poll.title)}</b>
        <small>${poll.options.length} ${plural(poll.options.length, 'вариант', 'варианта', 'вариантов')} · ${meta.label}</small>
      </span>
      <span class="mini-agenda__chev">${isActive ? icon('chevronRight') : ''}</span>`;
    if (isActive && state.participant.rsvp === 'yes') {
      return `<button type="button" class="mini-agenda__item active" data-action="scroll" data-target="participant-vote">${content}</button>`;
    }
    return `<div class="mini-agenda__item mini-agenda__item--inactive">${content}</div>`;
  }).join('');
}

function participantVoteBlock() {
  const poll = activePoll();
  const total = totalVotes(poll);
  const voted = state.participant.votedPollId === poll.id;
  const choice = poll.options.find((option) => option.id === state.participant.choiceId);
  const selected = state.participant.selectedId;
  const meta = statusMeta(poll);

  if (poll.status === 'waiting') {
    return `
      <div class="state-card state-card--wait">
        <div class="state-icon">${icon('clock')}</div>
        <h3>Голосование ещё не началось</h3>
        <p>Организатор откроет этот вопрос во время заседания. Следите за повесткой — экран обновится автоматически.</p>
        <div class="state-meta">${icon('info')}Вопрос ${pad(pollIndex(poll))} из ${pad(state.polls.length)} · ${esc(poll.title)}</div>
      </div>`;
  }

  if (poll.status === 'closed') {
    const winner = leaderOption(poll);
    const leaders = leadersList(poll);
    const winnerName = winner ? winner.label : leaders.length > 1 ? 'Ничья' : 'Голосов нет';
    const note = winner
      ? 'Решение принято по итогам голосования.'
      : leaders.length > 1
        ? 'Голоса разделились — требуется дополнительное обсуждение.'
        : 'По этому вопросу не было подано ни одного голоса.';
    return `
      <div class="state-card state-card--done">
        <div class="state-icon">${icon('trophy')}</div>
        <h3>Голосование завершено</h3>
        <p>${esc(note)}</p>
        <div class="winner-mini">
          <span class="winner-mini__icon">${icon(winner ? 'trophy' : 'vote')}</span>
          <span>
            <small>${winner ? 'Решение' : 'Итог'}</small>
            <b>${esc(winnerName)}</b>
            <span>${winner ? `${winner.count} ${plural(winner.count, 'голос', 'голоса', 'голосов')} · ${pct(winner.count, total)}%` : 'Итоги опубликованы'}</span>
          </span>
        </div>
        <div class="state-meta">${icon('checkCircle')}Итоги опубликованы · ${pollVoteCountLabel(poll)}</div>
      </div>`;
  }

  if (voted) {
    return `
      <div class="state-card" id="participant-voted">
        <div class="state-icon">${icon('check')}</div>
        <h3>Вы уже проголосовали</h3>
        <p>Ваш голос по вопросу «${esc(poll.title)}» принят анонимно. Итоги появятся после завершения голосования.</p>
        <div class="vote-receipt">
          <span>Ваш выбор</span>
          <b>${esc(choice ? choice.label : 'Голос учтён')}</b>
        </div>
        <div class="state-meta">${icon('lock')}Голос защищён · анонимно</div>
      </div>`;
  }

  if (!state.participant.rsvpConfirmed) {
    return `
      <div class="state-card state-card--wait">
        <div class="state-icon">${icon('users')}</div>
        <h3>Подтвердите присутствие</h3>
        <p>Ответьте на вопрос «Будете ли вы присутствовать?» в карточке заседания. После подтверждения ответ изменить нельзя.</p>
        <div class="state-meta">${icon('info')}Вопрос ${pad(pollIndex(poll))} из ${pad(state.polls.length)} · ${esc(poll.title)}</div>
      </div>`;
  }

  if (state.participant.rsvp === 'no') {
    return `
      <div class="state-card state-card--wait">
        <div class="state-icon">${icon('users')}</div>
        <h3>Вы не сможете присутствовать</h3>
        <p>Вы подтвердили, что не сможете прийти на заседание. Голосование недоступно; повестка ниже отображается для ознакомления.</p>
        <div class="state-meta">${icon('info')}Вопрос ${pad(pollIndex(poll))} из ${pad(state.polls.length)} · ${esc(poll.title)}</div>
      </div>`;
  }

  const options = poll.options.map((option) => `
    <button type="button" class="vote-option ${selected === option.id ? 'selected' : ''}" data-action="select-option" data-option="${esc(option.id)}">
      <span class="vote-option__radio" aria-hidden="true"></span>
      <span class="vote-option__avatar">${esc(initials(option.label))}</span>
      <span class="vote-option__copy">
        <b>${esc(option.label)}</b>
        <small>${esc(option.role || 'Кандидат в повестке заседания')}</small>
      </span>
      <span class="vote-option__check">${icon('check')}</span>
    </button>`).join('');

  return `
    <div class="vote-card" id="participant-vote">
      <div class="vote-card__top">
        <span class="vote-card__num">${icon('ballot')}Вопрос ${pad(pollIndex(poll))}</span>
        ${statusBadge(poll)}
      </div>
      <h3>${esc(poll.title)}</h3>
      <p class="vote-card__hint">Выберите один вариант ответа. Голос нельзя изменить после подтверждения.</p>
      <div class="vote-options">${options}</div>
      <button type="button" class="btn btn--primary btn--wide" data-action="cast-vote" ${selected ? '' : 'disabled'}>
        ${icon('lock')}Отдать голос
      </button>
      <div class="vote-card__footer">
        <span>${icon('shield')}Анонимно · 1 голос</span>
        <span>${icon('eye')}Итоги после завершения</span>
      </div>
    </div>`;
}

function participantNav() {
  return `
    <nav class="phone__nav" aria-label="Навигация участника">
      <button type="button" class="active" data-action="scroll" data-target="phone-top">${icon('home')}Заседание</button>
      <button type="button" data-action="scroll" data-target="participant-agenda">${icon('list')}Повестка</button>
      <button type="button" data-action="profile">${icon('user')}Профиль</button>
    </nav>`;
}

function renderParticipant() {
  const poll = activePoll();
  const total = totalVotes(poll);
  const meta = statusMeta(poll);
  const myInitials = initials(state.participant.name);
  views.participant.innerHTML = `
    <div class="participant-stage">
      <div class="phone">
        <div class="phone__status">
          <span>${timeShort()}</span>
          <span class="phone__status-right" aria-hidden="true">
            <svg viewBox="0 0 20 12"><path d="M1 10h2V7H1v3Zm4 0h2V5H5v5Zm4 0h2V3H9v7Zm4 0h2V1h-2v9Z"/></svg>
            <svg viewBox="0 0 22 16"><path d="M11 14.8 8.2 12a4 4 0 0 1 5.6 0L11 14.8Zm-4.9-4.9L3.9 7.7a10.1 10.1 0 0 1 14.2 0l-2.2 2.2a6.9 6.9 0 0 0-9.8 0Z"/></svg>
            <svg viewBox="0 0 24 12"><rect x="1" y="2" width="18" height="8" rx="2" fill="none" stroke="currentColor" stroke-width="1.5"/><rect x="3" y="4" width="12" height="4" rx="1"/><path d="M21 4v4" stroke="currentColor" stroke-width="1.5"/></svg>
          </span>
        </div>
        <div class="phone__topbar" id="phone-top">
          <button type="button" class="icon-btn" data-action="back" aria-label="Назад">${icon('chevronLeft')}</button>
          <span class="phone__brand"><img src="assets/logo.png" alt="Совет Старост" /></span>
          <span class="phone__avatar" title="${esc(state.participant.name)}">${myInitials}</span>
        </div>
        <div class="phone__scroll" id="phone-scroll">
          ${participantHero(poll)}
          <section class="mini-section">
            <div class="mini-section__head">
              <h2>${poll.status === 'open' ? 'Голосование' : 'Текущий вопрос'}</h2>
              <span>${meta.label}</span>
            </div>
            ${participantVoteBlock()}
          </section>
          <section class="mini-section" id="participant-agenda">
            <div class="mini-section__head">
              <h2>Повестка заседания</h2>
              <span>${state.polls.length} ${plural(state.polls.length, 'вопрос', 'вопроса', 'вопросов')}</span>
            </div>
            <div class="mini-agenda">${participantAgenda()}</div>
          </section>
          ${!runtime.api && poll.status === 'open' && state.participant.votedPollId !== poll.id ? '<p class="phone__note">Демо-подсказка: выберите вариант и подтвердите голос — откроется состояние «Вы уже проголосовали».</p>' : ''}
          <p class="phone__note">Совет Старост · ${esc(dateRu(state.event.date, true))}<br>Голосование анонимное · ${total} ${plural(total, 'голос', 'голоса', 'голосов')} уже подано</p>
        </div>
        ${participantNav()}
      </div>
    </div>`;
}

/* ============================================================
   Admin screen
   ============================================================ */
function sidebarMarkup() {
  const poll = activePoll();
  const adminName = (runtime.me && runtime.me.real_name) || state.participant.name;
  const adminInitials = initials(adminName);
  return `
    <aside class="sidebar">
      <div class="sidebar__brand"><img src="assets/logo.png" alt="Совет Старост" /></div>
      <div class="sidebar__event">
        <div class="sidebar__event-top">
          <span class="sidebar__event-date" data-live-event-date>${esc(dateBadge(state.event.date))}</span>
          ${statusBadge(poll)}
        </div>
        <h3 data-live-event-title>${esc(state.event.title)}</h3>
        <p>${icon('clock')}${esc(state.event.time)} МСК · ${icon('pin')}${esc(state.event.place)}</p>
      </div>
      <span class="sidebar__label">Заседание</span>
      <nav class="sidebar__nav" aria-label="Разделы панели">
        <button type="button" class="active" data-action="scroll" data-target="admin-top">${icon('home')}<span>Заседания</span><span class="nav-badge">${state.polls.length}</span></button>
        <button type="button" data-action="scroll" data-target="admin-agenda">${icon('list')}<span>Повестка</span></button>
        <button type="button" data-action="scroll" data-target="admin-participants">${icon('users')}<span>Участники</span></button>
        <button type="button" data-action="archive">${icon('archive')}<span>Архив</span></button>
      </nav>
      <div class="sidebar__spacer"></div>
      <button type="button" class="sidebar__screen-btn" data-action="open-screen">${icon('monitor')}Экран зала${icon('arrowUpRight', 'arrow')}</button>
      <div class="sidebar__demo ${runtime.api ? 'sidebar__demo--server' : ''}"><b>${runtime.api ? 'Сервер' : 'Демо-режим'}</b>${runtime.api ? 'Данные синхронизируются с FastAPI.' : 'Изменения сохраняются только в вашем браузере.'}</div>
      <div class="sidebar__profile">
        <span class="avatar">${esc(adminInitials)}</span>
        <span class="sidebar__profile-copy"><b>${esc(adminName)}</b><small>Администратор</small></span>
        <button type="button" class="icon-btn" data-action="profile" aria-label="Профиль">${icon('settings')}</button>
      </div>
    </aside>`;
}

function statCard(item) {
  return `
    <article class="stat ${item.tone ? `stat--${item.tone}` : ''}">
      <div class="stat__top">
        <span class="stat__icon">${icon(item.icon)}</span>
        <span class="stat__label">${item.label}</span>
      </div>
      <div class="stat__value"><strong>${item.value}</strong><small>${item.note}</small></div>
      <div class="stat__line"><i style="width:${Math.max(2, Math.min(100, item.percent))}%"></i></div>
    </article>`;
}

function statGridMarkup() {
  const poll = activePoll();
  const total = totalVotes(poll);
  const notVoted = Math.max(0, state.registered - total);
  return `
    <section class="stat-grid" aria-label="Показатели заседания">
      ${statCard({ icon: 'users', label: 'В реестре', value: state.registered, note: 'членов', percent: 100 })}
      ${statCard({ icon: 'checkCircle', label: 'Присутствуют', value: state.present, note: `${pct(state.present, state.registered)}%`, percent: pct(state.present, state.registered), tone: 'green' })}
      ${statCard({ icon: 'ballot', label: 'Проголосовали', value: total, note: `${pct(total, state.registered)}%`, percent: pct(total, state.registered), tone: 'red' })}
      ${statCard({ icon: 'clock', label: 'Не голосовали', value: notVoted, note: 'чел.', percent: pct(notVoted, state.registered), tone: 'amber' })}
    </section>`;
}

function eventPanelMarkup() {
  return `
    <section class="panel panel--accent" id="admin-event">
      <div class="panel__head">
        <div><h2>Карточка мероприятия</h2><p>Название, дата и место для участников</p></div>
        <span class="badge badge--live"><i></i>Опубликовано</span>
      </div>
      <form class="event-form" data-event-form>
        <div class="field-grid">
          <label class="field field--full">
            <span>Название</span>
            <input value="${esc(state.event.title)}" maxlength="120" data-bind="event.title" aria-label="Название мероприятия" />
          </label>
          <label class="field">
            <span>Дата</span>
            <input type="date" value="${esc(state.event.date)}" data-bind="event.date" aria-label="Дата мероприятия" />
          </label>
          <label class="field">
            <span>День недели</span>
            <input id="admin-weekday" readonly value="${esc(weekdayRu(state.event.date))}" aria-label="День недели" />
          </label>
          <label class="field">
            <span>Время</span>
            <input type="time" value="${esc(state.event.time)}" data-bind="event.time" aria-label="Время начала" />
          </label>
          <label class="field">
            <span>Место</span>
            <input value="${esc(state.event.place)}" maxlength="120" data-bind="event.place" aria-label="Место проведения" />
          </label>
        </div>
        <div class="event-form__actions">
          <button type="button" class="btn btn--primary btn--sm" data-action="save-event">${icon('check')}Сохранить</button>
        </div>
        <div class="event-form__meta">${icon('checkCircle')}${runtime.api ? 'Изменения применяются после нажатия «Сохранить»' : 'Изменения сохраняются в браузере'}</div>
      </form>
    </section>`;
}

function agendaPanelMarkup() {
  const rows = state.polls.map((poll, index) => {
    const isActive = poll.id === activePoll().id;
    const meta = statusMeta(poll);
    return `
      <div class="agenda-row ${isActive ? 'active' : ''}" data-action="select-poll" data-poll="${esc(poll.id)}" role="button" tabindex="0">
        <span class="agenda-row__num">${pad(index + 1)}</span>
        <span class="agenda-row__copy">
          <b ${isActive ? 'data-live-active-poll-title' : ''}>${esc(poll.title)}</b>
          <small>${poll.options.length} ${plural(poll.options.length, 'вариант', 'варианта', 'вариантов')} · ${meta.label}</small>
        </span>
        <span class="agenda-row__state">${statusBadge(poll)}</span>
        <button type="button" class="agenda-row__del" data-action="remove-poll" data-poll="${esc(poll.id)}" aria-label="Удалить вопрос" title="Удалить вопрос из повестки">${icon('trash')}</button>
      </div>`;
  }).join('');
  return `
    <section class="panel" id="admin-agenda">
      <div class="panel__head">
        <div><h2>Повестка встречи</h2><p>Выберите вопрос, чтобы настроить голосование</p></div>
        <span class="badge badge--wait">${state.polls.length} ${plural(state.polls.length, 'вопрос', 'вопроса', 'вопросов')}</span>
      </div>
      <div class="agenda-list">${rows}</div>
      <button type="button" class="agenda-add" data-action="add-poll">${icon('plus')}Добавить вопрос в повестку</button>
    </section>`;
}

function pollEditorMarkup() {
  const poll = activePoll();
  const total = totalVotes(poll);
  const meta = statusMeta(poll);
  const editable = poll.status !== 'open' || !runtime.api;

  const options = poll.options.map((option) => `
    <div class="option-row">
      <span class="option-row__drag" aria-hidden="true">${icon('grip')}</span>
      <input value="${esc(option.label)}" maxlength="80" data-bind="option.label" data-poll="${esc(poll.id)}" data-option="${esc(option.id)}" aria-label="Вариант ответа" ${editable ? '' : 'readonly'} />
      ${poll.status === 'waiting' ? '' : `<span class="option-row__count">${option.count}</span>`}
      <button type="button" class="option-row__remove" data-action="remove-option" data-poll="${esc(poll.id)}" data-option="${esc(option.id)}" aria-label="Удалить вариант" title="${editable ? 'Удалить вариант' : 'Варианты нельзя менять, пока идёт голосование'}" ${editable ? '' : 'disabled'}>${icon('trash')}</button>
    </div>`).join('');

  let actions = '';
  if (poll.status === 'open') {
    actions = `
      <button type="button" class="btn btn--danger btn--sm" data-action="finish-poll">${icon('stop')}Завершить голосование</button>
      <button type="button" class="btn btn--ghost btn--sm" data-action="open-screen">${icon('monitor')}Экран зала</button>`;
  } else if (poll.status === 'closed') {
    actions = `
      <button type="button" class="btn btn--primary btn--sm" data-action="restart-poll">${icon('refresh')}Открыть заново</button>
      <button type="button" class="btn btn--ghost btn--sm" data-action="open-screen">${icon('monitor')}Экран зала</button>`;
  } else {
    actions = `
      <button type="button" class="btn btn--primary btn--sm" data-action="start-poll">${icon('play')}Запустить голосование</button>
      <button type="button" class="btn btn--ghost btn--sm" data-action="open-screen">${icon('monitor')}Экран зала</button>`;
  }

  return `
    <section class="panel" id="admin-poll">
      <div class="panel__head">
        <div><h2>Вопрос и варианты ответа</h2><p>Настройте формулировку и запустите голосование</p></div>
      </div>
      <div class="poll-editor">
        <div class="poll-editor__title">
          <span class="poll-editor__num">${icon('vote')}Вопрос ${pad(pollIndex(poll))} из ${pad(state.polls.length)}</span>
          <span class="poll-editor__status">${statusBadge(poll)}</span>
        </div>
        <input class="poll-title-input" value="${esc(poll.title)}" maxlength="120" data-bind="poll.title" data-poll="${esc(poll.id)}" aria-label="Формулировка вопроса" ${editable ? '' : 'readonly'} />
        <div class="option-group__head"><span>Варианты ответа</span><small>${poll.options.length} ${plural(poll.options.length, 'вариант', 'варианта', 'вариантов')}${poll.status === 'waiting' ? '' : ` · ${total} ${plural(total, 'голос', 'голоса', 'голосов')}`}${editable ? '' : ' · только просмотр'}</small></div>
        <div class="option-editor">${options}</div>
        <button type="button" class="option-add" data-action="add-option" ${editable ? '' : 'disabled'}>${icon('plus')}Добавить вариант</button>
        <div class="poll-settings">
          <span>${icon('lock')}Анонимное голосование</span>
          <span>${icon('users')}Все члены Совета</span>
          <label class="switch">
            <input type="checkbox" data-toggle-results ${state.showResults ? 'checked' : ''} />
            <i aria-hidden="true"></i>
            Показывать результаты
          </label>
        </div>
        <div class="poll-actions">${actions}</div>
        <div class="poll-hint">${icon('info')}${meta.tone === 'live' ? 'Вопрос активен: участники видят его в мини-приложении, а экран зала обновляется в реальном времени.' : 'После запуска вопрос появится у участников, а на экране зала откроется живой дашборд.'}</div>
      </div>
    </section>`;
}

function donutSvg(percent, id, extraClass = '') {
  const radius = 42;
  const circumference = 2 * Math.PI * radius;
  const safePercent = Math.max(0, Math.min(100, Number(percent) || 0));
  const offset = circumference * (1 - safePercent / 100);
  return `
    <svg class="donut ${extraClass}" viewBox="0 0 100 100" role="img" aria-label="Явка ${safePercent}%">
      <defs>
        <linearGradient id="donut-${id}" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stop-color="#ff8aa0" />
          <stop offset="1" stop-color="#fa234f" />
        </linearGradient>
      </defs>
      <circle cx="50" cy="50" r="${radius}" fill="none" stroke="rgba(255,255,255,.07)" stroke-width="7" />
      <circle cx="50" cy="50" r="${radius}" fill="none" stroke="url(#donut-${id})" stroke-width="7" stroke-linecap="round" stroke-dasharray="${circumference.toFixed(2)}" stroke-dashoffset="${offset.toFixed(2)}" transform="rotate(-90 50 50)" style="transition:stroke-dashoffset .8s ease" />
      <text x="50" y="48" text-anchor="middle" dominant-baseline="central" fill="#fff" font-size="18" font-weight="700">${safePercent}%</text>
      <text x="50" y="63" text-anchor="middle" fill="#8f8798" font-size="6.5" letter-spacing=".12em">ЯВКА</text>
    </svg>`;
}

function resultRowsMarkup(poll, total) {
  const maximum = maxVotes(poll);
  return poll.options.map((option) => {
    const share = pct(option.count, total);
    const leader = maximum > 0 && option.count === maximum;
    return `
      <div class="result-row ${leader ? 'leader' : ''}">
        <div class="result-row__top">
          ${leader ? icon('crown') : ''}
          <b data-live-option="${esc(option.id)}">${esc(option.label)}</b>
          <span>${option.count} · ${share}%</span>
        </div>
        <div class="result-row__bar"><i style="width:${Math.max(1, share)}%"></i></div>
      </div>`;
  }).join('');
}

function livePanelMarkup() {
  const poll = activePoll();
  const total = totalVotes(poll);
  const turnout = pct(total, state.registered);
  return `
    <section class="panel panel--accent" id="admin-live">
      <div class="panel__head">
        <div><h2>Живые результаты</h2><p>Данные обновляются по мере голосования</p></div>
        <div class="head-actions">
          ${runtime.api ? '' : `<button type="button" class="demo-vote" data-action="demo-vote" title="Добавить демо-голос">${icon('plus')}1</button>`}
          ${statusBadge(poll)}
        </div>
      </div>
      <div class="donut-row">
        ${donutSvg(turnout, 'admin')}
        <div class="donut-copy">
          <strong>${total} / ${state.registered}</strong>
          <span>проголосовали</span>
          <small>${state.registered - total} ${plural(state.registered - total, 'участник', 'участника', 'участников')} ещё не голосовали</small>
        </div>
      </div>
      <div class="result-list">${resultRowsMarkup(poll, total)}</div>
      <div class="live-panel__foot">
        <span>${icon('chart')}${poll.status === 'open' ? 'Обновляется в реальном времени' : 'Итоги голосования'}</span>
        <span>${poll.status === 'waiting' ? 'Ожидает запуска' : `${total} ${plural(total, 'голос', 'голоса', 'голосов')}`}</span>
      </div>
    </section>`;
}

function previewMarkup() {
  const poll = activePoll();
  const total = totalVotes(poll);
  const tab = state.previewTab || 'live';
  let body = '';
  if (tab === 'intro') {
    body = `
      <div class="screen-preview__body">
        <h3>${heroTitleMarkup(state.event.title)}</h3>
        <div class="screen-preview__foot" style="margin-top:8px;color:#a99fae">
          <span>${esc(dateRu(state.event.date))} · ${esc(state.event.time)} МСК</span>
          <b>${esc(state.event.place)}</b>
        </div>
      </div>`;
  } else if (tab === 'results') {
    const winner = leaderOption(poll);
    const leaders = leadersList(poll);
    body = `
      <div class="screen-preview__body">
        <h3>${winner ? esc(winner.label) : leaders.length > 1 ? 'Ничья' : 'Итоги'}</h3>
        <div class="preview-bars">
          ${poll.options.slice(0, 3).map((option) => {
            const width = pct(option.count, total);
            return `<div class="preview-bar"><span data-live-option="${esc(option.id)}">${esc(option.label)}</span><div class="preview-bar__track"><i style="width:${Math.max(1, width)}%"></i></div><b>${option.count}</b></div>`;
          }).join('')}
        </div>
        <div class="screen-preview__foot" style="margin-top:7px"><span>${winner ? 'Избран председателем' : 'Итоги опубликованы'}</span><b>${total} / ${state.registered}</b></div>
      </div>`;
  } else {
    body = `
      <div class="screen-preview__body">
        <h3>${titleMarkup(poll.title)}</h3>
        <div class="preview-bars">
          ${poll.options.slice(0, 3).map((option) => {
            const width = pct(option.count, total);
            return `<div class="preview-bar"><span data-live-option="${esc(option.id)}">${esc(option.label)}</span><div class="preview-bar__track"><i style="width:${Math.max(1, width)}%"></i></div><b>${option.count}</b></div>`;
          }).join('')}
        </div>
      </div>`;
  }
  return `
    <div class="screen-preview">
      <div class="screen-preview__top">
        <span class="screen-preview__brand"><img src="assets/logo.png" alt="Совет Старост" /></span>
        <span class="screen-preview__live"><i></i>${poll.status === 'open' ? 'LIVE' : poll.status === 'closed' ? 'FINISH' : 'PREVIEW'}</span>
      </div>
      ${body}
      <div class="screen-preview__foot"><span>${poll.status === 'open' ? 'Голосование идёт' : poll.status === 'closed' ? 'Голосование завершено' : 'Ожидает запуска'}</span><b>${total} / ${state.registered}</b></div>
    </div>`;
}

function previewPanelMarkup() {
  return `
    <section class="panel" id="admin-preview">
      <div class="panel__head">
        <div><h2>Экран зала</h2><p>Предпросмотр для проектора</p></div>
        <button type="button" class="icon-btn" data-action="open-screen" aria-label="Открыть экран зала" title="Открыть экран зала">${icon('arrowUpRight')}</button>
      </div>
      <div class="preview-tabs" role="tablist" aria-label="Режим предпросмотра">
        <button type="button" class="${state.previewTab === 'intro' ? 'active' : ''}" data-action="preview-tab" data-tab="intro" role="tab">Заставка</button>
        <button type="button" class="${state.previewTab === 'live' ? 'active' : ''}" data-action="preview-tab" data-tab="live" role="tab">Голосование</button>
        <button type="button" class="${state.previewTab === 'results' ? 'active' : ''}" data-action="preview-tab" data-tab="results" role="tab">Итоги</button>
      </div>
      ${previewMarkup()}
      <div class="preview-actions">
        <button type="button" class="btn btn--primary btn--sm" data-action="open-screen">${icon('monitor')}Открыть экран зала</button>
        <button type="button" class="btn btn--ghost btn--sm" data-action="fullscreen" aria-label="На весь экран">${icon('fullscreen')}</button>
      </div>
    </section>`;
}

function filteredParticipantRows() {
  const search = state.adminSearch.trim().toLowerCase();
  let rows;

  if (runtime.api) {
    const myTelegramId = runtime.me ? runtime.me.telegram_id : null;
    rows = runtime.participants.map((user) => ({
      userId: user.id,
      demoIndex: null,
      name: user.real_name,
      role: user.username ? `@${user.username}` : user.is_allowed ? 'Член Совета' : 'Доступ закрыт',
      isMe: user.telegram_id === myTelegramId,
      voted: Boolean(user.has_voted),
      isAdmin: user.role === 'admin',
      telegramId: user.telegram_id,
    }));
  } else {
    const poll = activePoll();
    const total = totalVotes(poll);
    const meVoted = poll && state.participant.votedPollId === poll.id;
    const othersVoted = Math.max(0, total - (meVoted ? 1 : 0));
    rows = PEOPLE.map((name, index) => {
      if (state.removedDemoIndexes.includes(index)) return null;
      const isMe = index === 0;
      const voted = isMe ? meVoted : index <= othersVoted;
      return {
        userId: null,
        demoIndex: index,
        name,
        role: ROLES[index % ROLES.length],
        isMe,
        voted,
        isAdmin: participantIsAdmin(index),
      };
    }).filter(Boolean);
  }

  if (state.adminFilter === 'voted') rows = rows.filter((row) => row.voted);
  if (state.adminFilter === 'not') rows = rows.filter((row) => !row.voted);
  if (state.adminFilter === 'admin') rows = rows.filter((row) => row.isAdmin);
  if (search) {
    rows = rows.filter((row) =>
      `${row.name} ${row.role} ${row.isAdmin ? 'админ' : 'участник'} ${row.telegramId || ''}`.toLowerCase().includes(search)
    );
  }
  return rows;
}

function participantRowsMarkup() {
  const rows = filteredParticipantRows();
  if (!rows.length) {
    return '<div class="empty-state">Никого не нашли. Попробуйте другой запрос или фильтр.</div>';
  }

  const lastAdmin = adminCount() <= 1;
  return rows.map((row) => {
    const target = row.userId != null
      ? `data-user-id="${esc(row.userId)}"`
      : `data-index="${row.demoIndex}"`;
    return `
    <div class="participant-row ${row.isMe ? 'participant-row--me' : ''}">
      <div class="participant-person">
        <span class="avatar avatar--sm">${esc(initials(row.name))}</span>
        <span class="participant-person__copy">
          <b>${esc(row.name)}${row.isMe ? '<span class="you">Вы</span>' : ''}</b>
          <small>${esc(row.role)}</small>
        </span>
      </div>
      <div class="participant-role-cell">
        <span class="role-badge ${row.isAdmin ? 'role-badge--admin' : ''}">${icon(row.isAdmin ? 'shield' : 'user')}${row.isAdmin ? 'Админ' : 'Участник'}</span>
        <button type="button" class="role-toggle ${row.isAdmin ? 'role-toggle--demote' : ''}" data-action="toggle-admin" ${target} ${row.isAdmin && lastAdmin ? 'disabled' : ''} title="${row.isAdmin ? 'Разжаловать участника' : 'Назначить администратором'}">
          ${row.isAdmin ? 'Разжаловать' : 'Назначить админом'}
        </button>
        ${row.isAdmin ? '' : `<button type="button" class="role-toggle role-toggle--delete" data-action="delete-participant" ${target} title="Удалить участника из реестра">Удалить</button>`}
      </div>
      <span class="participant-flag ${row.voted ? 'participant-flag--vote' : 'participant-flag--wait'}">${icon(row.voted ? 'checkCircle' : 'clock')}${row.voted ? 'Голос' : 'Ожидает'}</span>
    </div>`;
  }).join('');
}

function participantsFootMarkup() {
  const rows = filteredParticipantRows();
  const total = runtime.api ? runtime.participants.length : state.registered;
  return `<span>Показано <b>${rows.length}</b> из ${total}</span><span>${icon('shield')}${adminCount()} ${plural(adminCount(), 'админ', 'админа', 'админов')} · анонимное голосование</span>`;
}

function participantsPanelMarkup() {
  const poll = activePoll();
  const total = totalVotes(poll);
  const totalPeople = runtime.api ? runtime.participants.length : state.registered;
  return `
    <section class="panel" id="admin-participants">
      <div class="panel__head">
        <div><h2>Участники и роли</h2><p>${totalPeople} в реестре · ${total} проголосовали · ${adminCount()} ${plural(adminCount(), 'админ', 'админа', 'админов')}</p></div>
        <span class="badge badge--done">${icon('check')}${total} / ${totalPeople}</span>
      </div>
      <div class="table-tools">
        <label class="search-box">
          ${icon('search')}
          <input value="${esc(state.adminSearch)}" placeholder="Найти участника" data-participant-search aria-label="Поиск участника" />
        </label>
        <div class="filter-chips">
          <button type="button" class="chip ${state.adminFilter === 'all' ? 'active' : ''}" data-action="filter-participants" data-filter="all">Все</button>
          <button type="button" class="chip ${state.adminFilter === 'voted' ? 'active' : ''}" data-action="filter-participants" data-filter="voted">Голос</button>
          <button type="button" class="chip ${state.adminFilter === 'not' ? 'active' : ''}" data-action="filter-participants" data-filter="not">Ожидают</button>
          <button type="button" class="chip ${state.adminFilter === 'admin' ? 'active' : ''}" data-action="filter-participants" data-filter="admin">Админы</button>
        </div>
      </div>
      <div class="participant-head"><span>Участник</span><span>Роль</span><span>Голос</span></div>
      <div class="participants-list" id="participants-list">${participantRowsMarkup()}</div>
      <div class="participants-foot" id="participants-foot">${participantsFootMarkup()}</div>
    </section>`;
}

function adminAccessDeniedMarkup() {
  return `
    <div class="admin-shell" id="admin-top">
      <main class="admin-main">
        <header class="admin-topbar">
          <div>
            <div class="admin-topbar__crumb">${icon('lock')}Панель администратора</div>
            <h1>Доступ ограничен</h1>
            <div class="admin-topbar__meta"><span>${icon('shield')}Нужна роль администратора</span></div>
          </div>
        </header>
        <section class="panel">
          <div class="empty-state">
            Эта панель доступна только администраторам Совета. Попроси действующего администратора назначить тебе роль в разделе «Участники и роли».
          </div>
        </section>
      </main>
    </div>`;
}

function renderAdmin() {
  if (runtime.api && (!runtime.me || !runtime.me.is_admin)) {
    views.admin.innerHTML = adminAccessDeniedMarkup();
    return;
  }
  views.admin.innerHTML = `
    <div class="admin-shell" id="admin-top">
      ${sidebarMarkup()}
      <main class="admin-main">
        <header class="admin-topbar">
          <div>
            <div class="admin-topbar__crumb">${icon('settings')}Панель администратора <span>/</span> Заседания</div>
            <h1 data-live-event-title>${esc(state.event.title)}</h1>
            <div class="admin-topbar__meta" data-live-event-meta>${eventMetaMarkup()}</div>
          </div>
          <div class="admin-topbar__actions">
            ${statusBadge(activePoll())}
            <button type="button" class="btn btn--primary btn--sm" data-action="open-screen">${icon('monitor')}Открыть экран зала</button>
          </div>
        </header>
        ${statGridMarkup()}
        <div class="admin-grid">
          <div class="admin-col">
            ${eventPanelMarkup()}
            ${agendaPanelMarkup()}
          </div>
          <div class="admin-col">
            ${pollEditorMarkup()}
            ${participantsPanelMarkup()}
          </div>
          <div class="admin-col admin-col--right">
            ${livePanelMarkup()}
            ${previewPanelMarkup()}
          </div>
        </div>
      </main>
    </div>`;
}

/* ============================================================
   Broadcast screen
   ============================================================ */
function lineChartSvg(poll, id = 'screen') {
  const timeline = Array.isArray(poll.timeline) && poll.timeline.length ? poll.timeline.slice(-22) : [0];
  const values = timeline.length === 1 ? [0, timeline[0]] : timeline;
  const maximum = Math.max(...values, 1);
  const width = 560;
  const height = 172;
  const padX = 12;
  const padY = 17;
  const x = (index) => padX + (index / Math.max(1, values.length - 1)) * (width - padX * 2);
  const y = (value) => height - padY - (Number(value) / maximum) * (height - padY * 2);
  const points = values.map((value, index) => [x(index), y(value)]);
  const line = points.map((point, index) => `${index ? 'L' : 'M'}${point[0].toFixed(1)},${point[1].toFixed(1)}`).join(' ');
  const area = `${line} L${points[points.length - 1][0].toFixed(1)},${height - padY} L${points[0][0].toFixed(1)},${height - padY} Z`;
  const last = points[points.length - 1];
  const grid = [0.25, 0.5, 0.75].map((ratio) => `<path d="M${padX} ${(height - padY - ratio * (height - padY * 2)).toFixed(1)} H${width - padX}" />`).join('');
  return `
    <svg viewBox="0 0 ${width} ${height}" role="img" aria-label="Динамика голосования">
      <defs>
        <linearGradient id="chart-area-${id}" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stop-color="#fa234f" stop-opacity=".28" />
          <stop offset="1" stop-color="#fa234f" stop-opacity="0" />
        </linearGradient>
      </defs>
      <g stroke="rgba(255,255,255,.07)" stroke-width="1">${grid}</g>
      <path d="${area}" fill="url(#chart-area-${id})" />
      <path d="${line}" fill="none" stroke="#fa234f" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" style="filter:drop-shadow(0 0 6px rgba(250,35,79,.4))" />
      <circle cx="${last[0]}" cy="${last[1]}" r="8" fill="none" stroke="rgba(250,35,79,.35)" stroke-width="1" />
      <circle cx="${last[0]}" cy="${last[1]}" r="4" fill="#fa234f" stroke="#fff" stroke-width="1.4" />
    </svg>`;
}

function screenBarsMarkup(poll) {
  const total = totalVotes(poll);
  const maximum = maxVotes(poll);
  return poll.options.map((option, index) => {
    const leader = maximum > 0 && option.count === maximum && leadersList(poll).length === 1;
    const width = pct(option.count, total);
    return `
      <div class="screen-bar ${leader ? 'leader' : ''}">
        <span class="screen-bar__rank">${pad(index + 1)}</span>
        <div class="screen-bar__copy">
          <div class="screen-bar__name">
            <span>${esc(option.label)}</span>
            ${leader ? icon('crown', 'trophy') : ''}
          </div>
          <small class="screen-bar__role">${esc(option.role || 'Кандидат в повестке заседания')}</small>
          <div class="screen-bar__track"><i style="width:${Math.max(1, width)}%"></i></div>
        </div>
        <div class="screen-bar__value">${option.count}<small>${pct(option.count, total)}%</small></div>
      </div>`;
  }).join('');
}

function screenTurnoutCard(poll) {
  const total = totalVotes(poll);
  const turnout = pct(total, state.registered);
  return `
    <section class="screen-card">
      <div class="screen-card__head"><h3>Явка</h3><span>${total} / ${state.registered}</span></div>
      <div class="screen-turnout">
        ${donutSvg(turnout, 'screen', 'screen-donut')}
        <div class="turnout-copy">
          <strong>${total}</strong>
          <span>${plural(total, 'голос', 'голоса', 'голосов')} подано</span>
          <small>${state.present} в зале<br>${Math.max(0, state.registered - total)} ${plural(Math.max(0, state.registered - total), 'участник', 'участника', 'участников')} ещё не голосовали</small>
        </div>
      </div>
    </section>`;
}

function screenChartCard(poll) {
  return `
    <section class="screen-card">
      <div class="screen-card__head"><h3>Динамика</h3><span>${pollVoteCountLabel(poll)}</span></div>
      <div class="chart-wrap">${lineChartSvg(poll, 'screen')}</div>
      <div class="chart-labels"><span>старт</span><span>сейчас</span></div>
    </section>`;
}

function screenLeaderCard(poll) {
  const leader = leaderOption(poll);
  const leaders = leadersList(poll);
  const total = totalVotes(poll);
  if (poll.status === 'closed') {
    const winner = leader;
    const name = winner ? winner.label : leaders.length > 1 ? 'Ничья' : 'Голосов нет';
    return `
      <section class="screen-card winner-panel">
        <div class="winner-panel__icon">${icon(winner ? 'trophy' : 'vote')}</div>
        <small>${winner ? 'Избран председателем' : leaders.length > 1 ? 'Требуется обсуждение' : 'Итог голосования'}</small>
        <h2>${esc(name)}</h2>
        <p>${winner ? 'Решение принято по итогам анонимного голосования.' : leaders.length > 1 ? 'Голоса разделились между кандидатами.' : 'По вопросу не подано ни одного голоса.'}</p>
        ${winner ? `<div class="winner-panel__votes"><strong>${winner.count}</strong><span>${plural(winner.count, 'голос', 'голоса', 'голосов')} · ${pct(winner.count, total)}%</span></div>` : ''}
      </section>`;
  }
  if (!leader) {
    return `
      <section class="screen-card">
        <div class="screen-card__head"><h3>Лидер</h3><span>ожидание</span></div>
        <p style="color:var(--muted);font-size:clamp(8px,.72vw,10px);line-height:1.5">Как только появятся первые голоса, здесь будет показан лидирующий вариант.</p>
      </section>`;
  }
  return `
    <section class="screen-card">
      <div class="screen-card__head"><h3>Лидер голосования</h3><span>${poll.status === 'open' ? 'сейчас' : 'итог'}</span></div>
      <div class="screen-leader" style="margin-top:0">
        <span class="screen-leader__icon">${icon('crown')}</span>
        <span class="screen-leader__copy">
          <small>Лидирует</small>
          <b>${esc(leader.label)}</b>
          <span>${leader.count} ${plural(leader.count, 'голос', 'голоса', 'голосов')} · ${pct(leader.count, total)}%</span>
        </span>
      </div>
    </section>`;
}

function screenFooterMarkup(poll) {
  const total = totalVotes(poll);
  return `
    <footer class="screen-footer">
      <div class="screen-progress">
        <div class="screen-progress__top"><span>Проголосовали <b>${total} из ${state.registered}</b></span><span>${pct(total, state.registered)}% явка</span></div>
        <div class="screen-progress__track"><i style="width:${Math.max(1, pct(total, state.registered))}%"></i></div>
      </div>
      <div class="screen-footer__right">
        ${poll.status === 'open' ? `<span class="screen-countdown"><span>до завершения</span><b data-countdown>${countdownLabel()}</b></span>` : `<span class="screen-footer__note">${poll.status === 'closed' ? 'Итоги опубликованы' : 'Ожидание запуска'}</span>`}
        <span class="screen-footer__note">Анонимное голосование</span>
        <button type="button" class="screen-footer__full" data-action="fullscreen" aria-label="На весь экран" title="На весь экран">${icon('fullscreen')}</button>
      </div>
    </footer>`;
}

function screenTopMarkup() {
  return `
    <header class="screen-top">
      <div class="screen-brand">
        <img src="assets/logo.png" alt="Совет Старост" />
        <span class="screen-brand__divider"></span>
        <span class="screen-brand__copy"><small>Заседание Совета</small><b>${esc(dateRu(state.event.date, true))} · ${esc(state.event.time)} МСК</b></span>
      </div>
      <div class="screen-top__right">
        <span class="screen-live"><i></i>${activePoll().status === 'open' ? 'Прямой эфир' : activePoll().status === 'closed' ? 'Итоги' : 'Ожидание'}</span>
        <span class="screen-clock" data-clock>${timeFull()}</span>
      </div>
    </header>`;
}

function renderScreen() {
  const poll = activePoll();
  const total = totalVotes(poll);
  const isWaiting = poll.status === 'waiting';
  const showResults = state.showResults || poll.status !== 'open';

  let main = '';
  if (isWaiting) {
    main = `
      <div class="screen-main">
        <div class="screen-waiting">
          <span class="screen-kicker">Следующий вопрос</span>
          <h1 class="screen-title">${titleMarkup(poll.title)}</h1>
          <p class="screen-subtitle">${esc(state.event.title)} · ${esc(dateRu(state.event.date, true))} · ${esc(state.event.place)}</p>
          <span class="waiting-pill">${icon('clock')}Голосование откроется по команде организатора</span>
          <div class="screen-waiting__agenda">
            ${state.polls.map((item, index) => `<div class="waiting-agenda-row"><span>${pad(index + 1)}</span><b>${esc(item.title)}</b><small>${statusMeta(item).label}</small></div>`).join('')}
          </div>
        </div>
        <aside class="screen-side">
          <section class="screen-card">
            <div class="screen-card__head"><h3>Заседание</h3><span>${esc(dateBadge(state.event.date))}</span></div>
            <div style="font-size:clamp(14px,1.3vw,19px);font-weight:650;line-height:1.2;letter-spacing:-.035em">${esc(state.event.title)}</div>
            <p style="margin-top:8px;color:var(--muted);font-size:clamp(8px,.72vw,10px);line-height:1.55">${esc(dateRu(state.event.date))}, ${esc(weekdayRu(state.event.date))}<br>${esc(state.event.time)} МСК · ${esc(state.event.place)}</p>
          </section>
          <section class="screen-card">
            <div class="screen-card__head"><h3>Готовность</h3><span>${state.present} / ${state.registered}</span></div>
            <div class="screen-turnout">
              ${donutSvg(pct(state.present, state.registered), 'wait', 'screen-donut')}
              <div class="turnout-copy"><strong>${state.present}</strong><span>участников в зале</span><small>Можно начинать голосование</small></div>
            </div>
          </section>
        </aside>
      </div>`;
  } else {
    main = `
      <div class="screen-main">
        <div>
          <span class="screen-kicker">${poll.status === 'closed' ? 'Итоги голосования' : 'Голосование'} ${pad(pollIndex(poll))} / ${pad(state.polls.length)}</span>
          <h1 class="screen-title">${titleMarkup(poll.title)}</h1>
          <p class="screen-subtitle">${esc(state.event.title)} · ${esc(dateRu(state.event.date, true))} · ${esc(state.event.place)}</p>
          ${showResults ? `<div class="screen-bars">${screenBarsMarkup(poll)}</div>` : `<div class="screen-card" style="margin-top:28px;max-width:620px"><div class="screen-card__head"><h3>Результаты скрыты</h3><span>${icon('eyeOff')}</span></div><p style="color:var(--muted);font-size:clamp(9px,.82vw,12px);line-height:1.55">Организатор скрыл промежуточные результаты. Итоги появятся после завершения голосования.</p></div>`}
        </div>
        <aside class="screen-side">
          ${screenTurnoutCard(poll)}
          ${screenChartCard(poll)}
          ${screenLeaderCard(poll)}
        </aside>
      </div>`;
  }

  views.screen.innerHTML = `
    <div class="screen-view">
      <div class="screen-shell">
        ${screenTopMarkup()}
        ${main}
        ${screenFooterMarkup(poll)}
      </div>
    </div>`;
}

/* ============================================================
   Render orchestration
   ============================================================ */
function renderAll() {
  renderParticipant();
  renderAdmin();
  renderScreen();
}

function applyMode() {
  document.body.dataset.mode = state.mode;
  Object.entries(views).forEach(([name, view]) => {
    view.classList.toggle('active', name === state.mode);
  });
  document.querySelectorAll('.demo-seg button[data-mode]').forEach((button) => {
    button.classList.toggle('active', button.dataset.mode === state.mode);
    button.setAttribute('aria-pressed', button.dataset.mode === state.mode ? 'true' : 'false');
  });
}

function setMode(mode) {
  if (!['admin', 'participant', 'screen'].includes(mode)) return;
  if (runtime.api) {
    const admin = isAdminUser();
    if ((mode === 'admin' || mode === 'screen') && !admin) {
      toast(mode === 'screen' ? 'Экран зала доступен только администратору.' : 'Эта панель доступна только администратору.');
      return;
    }
    if (mode === 'participant' && admin) {
      toast('Администратору доступна панель администратора.');
      return;
    }
  }
  if (runtime.api && mode === 'participant') {
    const open = state.polls.find((poll) => poll.status === 'open');
    if (open) state.activePollId = open.id;
  }
  state.mode = mode;
  save();
  if (mode === 'participant') renderParticipant();
  if (mode === 'admin') renderAdmin();
  if (mode === 'screen') renderScreen();
  applyMode();
}

function rerenderParticipantPreserveScroll() {
  const scroll = document.getElementById('phone-scroll');
  const top = scroll ? scroll.scrollTop : 0;
  renderParticipant();
  const next = document.getElementById('phone-scroll');
  if (next) next.scrollTop = top;
}

function refreshDynamicText() {
  const poll = activePoll();
  document.querySelectorAll('[data-live-event-title]').forEach((el) => { el.textContent = state.event.title; });
  document.querySelectorAll('[data-live-event-meta]').forEach((el) => { el.innerHTML = eventMetaMarkup(); });
  document.querySelectorAll('[data-live-event-date]').forEach((el) => { el.textContent = dateBadge(state.event.date); });
  document.querySelectorAll('[data-live-active-poll-title]').forEach((el) => { el.textContent = poll.title; });
  poll.options.forEach((option) => {
    document.querySelectorAll(`[data-live-option="${option.id}"]`).forEach((el) => { el.textContent = option.label; });
  });
}

function rerenderParticipantList() {
  const list = document.getElementById('participants-list');
  if (list) list.innerHTML = participantRowsMarkup();
  const foot = document.getElementById('participants-foot');
  if (foot) foot.innerHTML = participantsFootMarkup();
  document.querySelectorAll('[data-action="filter-participants"]').forEach((button) => {
    button.classList.toggle('active', button.dataset.filter === state.adminFilter);
  });
}

/* ============================================================
   Actions
   ============================================================ */
async function castVote() {
  const poll = activePoll();
  if (!poll || poll.status !== 'open') {
    toast('Голосование сейчас недоступно.');
    return;
  }
  if (!state.participant.rsvpConfirmed) {
    toast('Сначала подтвердите присутствие на заседании.');
    return;
  }
  if (state.participant.rsvp !== 'yes') {
    toast('Вы отметили, что не сможете присутствовать на заседании.');
    return;
  }
  if (state.participant.votedPollId === poll.id) {
    toast('Вы уже проголосовали по этому вопросу.');
    return;
  }
  const selectedId = state.participant.selectedId;
  const option = poll.options.find((item) => item.id === selectedId);
  if (!option) {
    toast('Сначала выберите вариант ответа.');
    return;
  }

  if (runtime.api) {
    try {
      const result = await apiRequest(`/api/votings/${Number(poll.id)}/vote`, {
        method: 'POST',
        body: { option_id: Number(option.id) },
      });
      poll.options = (result.results || []).map((item) => ({
        id: String(item.id),
        label: item.label,
        role: item.role || '',
        count: Number(item.count) || 0,
      }));
      state.participant.votedPollId = poll.id;
      state.participant.choiceId = String(result.option_id);
      state.participant.votedAt = Date.now();
      state.participant.selectedId = null;
      await refreshApi({ silent: true });
      renderAll();
      applyMode();
      toast('Голос принят анонимно. Спасибо за участие!');
    } catch (error) {
      if (error.status === 409) {
        await refreshApi({ silent: true });
        renderAll();
        applyMode();
      }
      toast(apiErrorMessage(error));
    }
    return;
  }

  if (totalVotes(poll) < state.present) {
    option.count = (Number(option.count) || 0) + 1;
  }
  state.participant.votedPollId = poll.id;
  state.participant.choiceId = option.id;
  state.participant.votedAt = Date.now();
  state.participant.selectedId = null;
  poll.timeline = Array.isArray(poll.timeline) ? poll.timeline : [0];
  poll.timeline.push(totalVotes(poll));
  if (poll.timeline.length > 24) poll.timeline.shift();
  save();
  renderAll();
  toast('Голос принят анонимно. Спасибо за участие!');
}

async function startPoll(id) {
  const poll = pollById(id) || activePoll();
  if (!poll || poll.id === 'empty') return;

  if (runtime.api) {
    try {
      await apiRequest(`/api/admin/votings/${Number(poll.id)}/start`, { method: 'POST' });
      state.activePollId = poll.id;
      state.participant.selectedId = null;
      await refreshApi({ silent: true });
      renderAll();
      applyMode();
      toast('Голосование запущено. Участники уже видят вопрос.');
    } catch (error) {
      toast(apiErrorMessage(error));
    }
    return;
  }

  state.polls.forEach((item) => {
    if (item.id !== poll.id && item.status === 'open') item.status = 'closed';
  });
  poll.status = 'open';
  poll.startsAt = Date.now();
  poll.endsAt = Date.now() + 5 * 60 * 1000;
  if (!Array.isArray(poll.timeline) || !poll.timeline.length) poll.timeline = [0];
  state.activePollId = poll.id;
  state.participant.selectedId = null;
  save();
  renderAll();
  toast('Голосование запущено. Участники уже видят вопрос.');
}

async function finishPoll() {
  const poll = activePoll();
  if (!poll || poll.id === 'empty') return;

  if (runtime.api) {
    try {
      await apiRequest(`/api/admin/votings/${Number(poll.id)}/close`, { method: 'POST' });
      await refreshApi({ silent: true });
      renderAll();
      applyMode();
      toast('Голосование завершено. Итоги опубликованы на экране зала.');
    } catch (error) {
      toast(apiErrorMessage(error));
    }
    return;
  }

  poll.status = 'closed';
  poll.endsAt = Date.now();
  save();
  renderAll();
  toast('Голосование завершено. Итоги опубликованы на экране зала.');
}

async function restartPoll() {
  const poll = activePoll();
  if (!poll || poll.id === 'empty') return;

  if (runtime.api) {
    if (!window.confirm('Открыть голосование заново? Ранее поданные голоса сохранятся.')) return;
    try {
      await apiRequest(`/api/admin/votings/${Number(poll.id)}/reopen`, { method: 'POST' });
      await refreshApi({ silent: true });
      renderAll();
      applyMode();
      toast('Голосование открыто заново. Голоса сохранены.');
    } catch (error) {
      toast(apiErrorMessage(error));
    }
    return;
  }

  if (!window.confirm('Открыть голосование заново? Текущие голоса по этому вопросу будут сброшены.')) return;
  poll.status = 'open';
  poll.startsAt = Date.now();
  poll.endsAt = Date.now() + 5 * 60 * 1000;
  poll.timeline = [0];
  poll.options.forEach((option) => { option.count = 0; });
  if (state.participant.votedPollId === poll.id) {
    state.participant.votedPollId = null;
    state.participant.choiceId = null;
    state.participant.votedAt = null;
  }
  state.participant.selectedId = null;
  save();
  renderAll();
  toast('Голосование открыто заново. Счётчики обнулены.');
}

async function addPoll() {
  if (runtime.api) {
    try {
      const created = await apiRequest('/api/admin/votings', {
        method: 'POST',
        body: {
          title: 'Новый вопрос повестки',
          is_anonymous: true,
          options: [
            { label: 'За', role: 'Поддержать предложение' },
            { label: 'Против', role: 'Не поддерживать' },
            { label: 'Воздержался', role: 'Без позиции' },
          ],
        },
      });
      state.activePollId = String(created.id);
      state.participant.selectedId = null;
      await refreshApi({ silent: true });
      renderAll();
      applyMode();
      toast('Вопрос добавлен в повестку. Отредактируйте формулировку и варианты.');
    } catch (error) {
      toast(apiErrorMessage(error));
    }
    return;
  }

  const poll = {
    id: newId('poll'),
    title: 'Новый вопрос повестки',
    status: 'waiting',
    options: [
      { id: newId('opt'), label: 'За', role: 'Поддержать предложение', count: 0 },
      { id: newId('opt'), label: 'Против', role: 'Не поддерживать', count: 0 },
      { id: newId('opt'), label: 'Воздержался', role: 'Без позиции', count: 0 },
    ],
    timeline: [0],
    startsAt: null,
    endsAt: null,
  };
  state.polls.push(poll);
  state.activePollId = poll.id;
  state.participant.selectedId = null;
  save();
  renderAll();
  toast('Вопрос добавлен в повестку. Отредактируйте формулировку и варианты.');
}

async function removePoll(id) {
  const poll = pollById(id);
  if (!poll) return;
  const suffix = poll.status === 'open'
    ? ' Голосование по нему сейчас идёт.'
    : totalVotes(poll) > 0
      ? ' Все голоса по этому вопросу тоже будут удалены.'
      : '';
  if (!window.confirm(`Удалить вопрос «${poll.title}» из повестки?${suffix}`)) return;

  if (runtime.api) {
    try {
      await apiRequest(`/api/admin/votings/${Number(poll.id)}`, { method: 'DELETE' });
      if (state.activePollId === poll.id) state.activePollId = null;
      state.participant.selectedId = null;
      await refreshApi({ silent: true });
      renderAll();
      applyMode();
      toast('Вопрос удалён из повестки.');
    } catch (error) {
      toast(apiErrorMessage(error));
    }
    return;
  }

  state.polls = state.polls.filter((item) => item.id !== id);
  if (!state.polls.some((item) => item.id === state.activePollId)) {
    state.activePollId = state.polls[0] ? state.polls[0].id : null;
  }
  state.participant.selectedId = null;
  save();
  renderAll();
  toast('Вопрос удалён из повестки.');
}

async function addOption() {
  const poll = activePoll();
  if (!poll || poll.id === 'empty') return;
  if (runtime.api && poll.status === 'open') {
    toast('Варианты нельзя менять, пока идёт голосование.');
    return;
  }

  if (runtime.api) {
    try {
      await apiRequest(`/api/admin/votings/${Number(poll.id)}/options`, {
        method: 'POST',
        body: { label: 'Новый вариант', role: 'Описание варианта' },
      });
      await refreshApi({ silent: true });
      renderAll();
      applyMode();
      toast('Вариант ответа добавлен.');
    } catch (error) {
      toast(apiErrorMessage(error));
    }
    return;
  }

  poll.options.push({ id: newId('opt'), label: 'Новый вариант', role: 'Описание варианта', count: 0 });
  save();
  renderAll();
  toast('Вариант ответа добавлен.');
}

async function removeOption(pollId, optionId) {
  const poll = pollById(pollId) || activePoll();
  if (!poll) return;
  if (poll.options.length <= 2) {
    toast('В вопросе должно быть минимум два варианта ответа.');
    return;
  }
  const option = poll.options.find((item) => item.id === optionId);
  if (!option) return;
  if (runtime.api && poll.status === 'open') {
    toast('Варианты нельзя менять, пока идёт голосование.');
    return;
  }
  if (option.count > 0 && !window.confirm(`У варианта «${option.label}» уже ${option.count} ${plural(option.count, 'голос', 'голоса', 'голосов')}. Удалить вариант вместе с этими голосами?`)) {
    return;
  }

  if (runtime.api) {
    try {
      await apiRequest(`/api/admin/options/${Number(option.id)}`, { method: 'DELETE' });
      await refreshApi({ silent: true });
      renderAll();
      applyMode();
      toast('Вариант ответа удалён.');
    } catch (error) {
      toast(apiErrorMessage(error));
    }
    return;
  }

  poll.options = poll.options.filter((item) => item.id !== optionId);
  save();
  renderAll();
  toast('Вариант ответа удалён.');
}

function addDemoVote() {
  if (runtime.api) {
    toast('В режиме сервера голоса добавляются реальными участниками.');
    return;
  }
  const poll = activePoll();
  if (!poll || poll.status !== 'open') {
    toast('Демо-голос можно добавить только в открытый вопрос.');
    return;
  }
  const total = totalVotes(poll);
  const meVoted = state.participant.votedPollId === poll.id;
  const cap = Math.max(0, state.present - (meVoted ? 0 : 1));
  if (total >= cap) {
    toast(meVoted ? 'Все участники уже проголосовали.' : 'Все, кроме вас, уже проголосовали. Остался ваш голос.');
    return;
  }
  const pool = poll.options.map((option) => ({ option, weight: Math.max(1, option.count + 4) }));
  const sum = pool.reduce((acc, item) => acc + item.weight, 0);
  let point = Math.random() * sum;
  let chosen = pool[0].option;
  for (const item of pool) {
    point -= item.weight;
    if (point <= 0) { chosen = item.option; break; }
  }
  chosen.count = (Number(chosen.count) || 0) + 1;
  poll.timeline = Array.isArray(poll.timeline) ? poll.timeline : [0];
  poll.timeline.push(totalVotes(poll));
  if (poll.timeline.length > 24) poll.timeline.shift();
  save();
  if (state.mode === 'screen') renderScreen();
  else renderAdmin();
  toast('Демо-голос добавлен.');
}

function openNewEventModal() {
  const modal = document.getElementById('new-event-modal');
  const form = document.getElementById('new-event-form');
  if (!modal || !form) return;
  if (!form.elements.title.value) form.elements.title.value = 'Заседание Совета старост';
  if (!form.elements.date.value) form.elements.date.value = state.event.date || todayPlus(1);
  if (!form.elements.time.value) form.elements.time.value = '17:00';
  if (!form.elements.place.value) form.elements.place.value = 'Зал Учёного совета';
  const weekday = document.getElementById('new-event-weekday');
  if (weekday) weekday.value = weekdayRu(form.elements.date.value);
  modal.hidden = false;
  document.body.classList.add('modal-open');
  setTimeout(() => form.elements.title.focus(), 60);
}

function closeNewEventModal() {
  const modal = document.getElementById('new-event-modal');
  if (!modal) return;
  modal.hidden = true;
  document.body.classList.remove('modal-open');
}

async function saveEventToApi(payload) {
  try {
    const saved = await apiRequest('/api/admin/event', { method: 'PATCH', body: payload });
    applyEvent(saved);
    closeNewEventModal();
    await refreshApi({ silent: true });
    renderAll();
    applyMode();
    toast('Мероприятие сохранено. Повестка доступна ниже.');
    return true;
  } catch (error) {
    toast(apiErrorMessage(error));
    return false;
  }
}

async function saveEvent() {
  const payload = {
    title: String(state.event.title || '').trim(),
    date: String(state.event.date || '').trim(),
    time: String(state.event.time || '').trim(),
    place: String(state.event.place || '').trim(),
  };
  if (!payload.title || !payload.date || !payload.time || !payload.place) {
    toast('Заполните название, дату, время и место мероприятия.');
    return;
  }
  if (runtime.api) {
    await saveEventToApi(payload);
    return;
  }
  save();
  refreshDynamicText();
  toast('Карточка мероприятия сохранена.');
}

function scheduleApiSave(key, task) {
  const existing = runtime.saveTimers.get(key);
  if (existing) clearTimeout(existing);
  const timer = setTimeout(async () => {
    runtime.saveTimers.delete(key);
    try {
      await task();
    } catch (error) {
      toast(apiErrorMessage(error));
      await refreshApi({ silent: true });
      renderAll();
      applyMode();
    }
  }, 700);
  runtime.saveTimers.set(key, timer);
}

function createEventFromForm(event) {
  event.preventDefault();
  const form = event.target;
  const data = Object.fromEntries(new FormData(form));
  if (!String(data.title || '').trim() || !data.date || !data.time || !String(data.place || '').trim()) {
    toast('Заполните название, дату, время и место мероприятия.');
    return;
  }

  if (runtime.api) {
    saveEventToApi({
      title: String(data.title).trim(),
      date: String(data.date),
      time: String(data.time),
      place: String(data.place).trim(),
    });
    return;
  }

  state.event = {
    title: String(data.title).trim(),
    date: String(data.date),
    time: String(data.time),
    place: String(data.place).trim(),
  };
  const poll = {
    id: newId('poll'),
    title: 'Первый вопрос повестки',
    status: 'waiting',
    options: [
      { id: newId('opt'), label: 'За', role: 'Поддержать предложение', count: 0 },
      { id: newId('opt'), label: 'Против', role: 'Не поддерживать', count: 0 },
      { id: newId('opt'), label: 'Воздержался', role: 'Без позиции', count: 0 },
    ],
    timeline: [0],
    startsAt: null,
    endsAt: null,
  };
  state.polls = [poll];
  state.activePollId = poll.id;
  state.participant.votedPollId = null;
  state.participant.choiceId = null;
  state.participant.votedAt = null;
  state.participant.selectedId = null;
  state.adminFilter = 'all';
  state.adminSearch = '';
  state.previewTab = 'live';
  state.showResults = true;
  state.mode = 'admin';
  save();
  closeNewEventModal();
  renderAll();
  applyMode();
  toast('Мероприятие создано. Добавьте вопросы повестки.');
}

/* ============================================================
   Event delegation
   ============================================================ */
document.addEventListener('click', async (event) => {
  const actionEl = event.target.closest('[data-action]');
  if (!actionEl) return;
  const action = actionEl.dataset.action;

  if (action === 'set-mode') {
    setMode(actionEl.dataset.mode);
    return;
  }
  if (action === 'reset-demo') {
    if (runtime.api) {
      await refreshApi();
      renderAll();
      applyMode();
      toast('Данные обновлены с сервера.');
      return;
    }
    const fresh = makeDefaultState();
    Object.keys(state).forEach((key) => { delete state[key]; });
    Object.assign(state, fresh);
    save();
    renderAll();
    applyMode();
    toast('Демо сброшено: данные и голоса вернулись к исходному состоянию.');
    return;
  }
  if (action === 'select-option') {
    const poll = activePoll();
    if (!poll || poll.status !== 'open' || state.participant.votedPollId === poll.id) return;
    state.participant.selectedId = actionEl.dataset.option;
    save();
    rerenderParticipantPreserveScroll();
    return;
  }
  if (action === 'cast-vote') {
    await castVote();
    return;
  }
  if (action === 'select-rsvp') {
    if (state.participant.rsvpConfirmed) return;
    const choice = actionEl.dataset.rsvp;
    if (choice !== 'yes' && choice !== 'no') return;
    state.participant.rsvpDraft = choice;
    save();
    rerenderParticipantPreserveScroll();
    return;
  }
  if (action === 'confirm-rsvp') {
    if (state.participant.rsvpConfirmed) return;
    const choice = state.participant.rsvpDraft;
    if (choice !== 'yes' && choice !== 'no') {
      toast('Выберите «Да» или «Нет».');
      return;
    }

    if (runtime.api) {
      try {
        const me = await apiRequest('/api/me/rsvp', {
          method: 'PATCH',
          body: { will_attend: choice === 'yes' },
        });
        runtime.me = me;
        state.participant.rsvpConfirmed = Boolean(me.rsvp_confirmed);
        state.participant.rsvp = me.rsvp_confirmed
          ? (me.will_attend ? 'yes' : 'no')
          : null;
        state.participant.rsvpDraft = state.participant.rsvp;
        await refreshApi({ silent: true });
        renderAll();
        applyMode();
        toast('Ответ подтверждён. Изменить его нельзя.');
      } catch (error) {
        if (error.status === 409) {
          await refreshApi({ silent: true });
          renderAll();
          applyMode();
        }
        toast(apiErrorMessage(error));
      }
      return;
    }

    state.participant.rsvpConfirmed = true;
    state.participant.rsvp = choice;
    state.participant.rsvpDraft = choice;
    if (choice === 'no') state.present = Math.max(0, state.present - 1);
    save();
    renderParticipant();
    applyMode();
    toast('Ответ подтверждён. Изменить его нельзя.');
    return;
  }
  if (action === 'scroll') {
    const target = document.getElementById(actionEl.dataset.target);
    if (target) target.scrollIntoView({ behavior: 'smooth', block: 'start' });
    return;
  }
  if (action === 'profile') {
    toast(`${state.participant.name} · ${state.participant.role}. Профиль доступен в полной версии мини-приложения.`);
    return;
  }
  if (action === 'back') {
    toast('Это стартовый экран мини-приложения.');
    return;
  }
  if (action === 'archive') {
    toast('Архив заседаний доступен в полной версии панели администратора.');
    return;
  }
  if (action === 'save-event') {
    saveEvent();
    return;
  }
  if (action === 'new-event') {
    openNewEventModal();
    return;
  }
  if (action === 'close-modal') {
    closeNewEventModal();
    return;
  }
  if (action === 'select-poll') {
    state.activePollId = actionEl.dataset.poll;
    state.participant.selectedId = null;
    if (runtime.api) {
      const selected = pollById(state.activePollId);
      state.participant.votedPollId = selected && selected.myVoteOptionId != null ? selected.id : null;
      state.participant.choiceId = selected && selected.myVoteOptionId != null ? String(selected.myVoteOptionId) : null;
    }
    save();
    renderAll();
    return;
  }
  if (action === 'add-poll') {
    await addPoll();
    return;
  }
  if (action === 'remove-poll') {
    await removePoll(actionEl.dataset.poll);
    return;
  }
  if (action === 'start-poll') {
    await startPoll(activePoll().id);
    return;
  }
  if (action === 'finish-poll') {
    await finishPoll();
    return;
  }
  if (action === 'restart-poll') {
    await restartPoll();
    return;
  }
  if (action === 'add-option') {
    await addOption();
    return;
  }
  if (action === 'remove-option') {
    await removeOption(actionEl.dataset.poll, actionEl.dataset.option);
    return;
  }
  if (action === 'demo-vote') {
    addDemoVote();
    return;
  }
  if (action === 'preview-tab') {
    state.previewTab = actionEl.dataset.tab || 'live';
    save();
    renderAdmin();
    return;
  }
  if (action === 'open-screen') {
    setMode('screen');
    return;
  }
  if (action === 'toggle-admin') {
    if (runtime.api) {
      const userId = Number(actionEl.dataset.userId);
      if (!Number.isFinite(userId)) return;
      const user = runtime.participants.find((item) => Number(item.id) === userId);
      if (!user) return;
      const wasAdmin = user.role === 'admin';
      if (wasAdmin && adminCount() <= 1) {
        toast('В системе должен остаться хотя бы один администратор.');
        return;
      }
      const nextRole = wasAdmin ? 'user' : 'admin';
      try {
        await apiRequest(`/api/admin/users/${userId}/role`, {
          method: 'PATCH',
          body: { role: nextRole },
        });
        const selfDemoted = runtime.me && runtime.me.id === userId && nextRole === 'user';
        await refreshApi({ silent: true });
        if (selfDemoted) state.mode = 'participant';
        applyRuntimeUi();
        renderAll();
        applyMode();
        toast(`Роль обновлена: ${user.real_name} — ${wasAdmin ? 'участник' : 'администратор'}.`);
      } catch (error) {
        toast(apiErrorMessage(error));
      }
      return;
    }

    const index = Number(actionEl.dataset.index);
    if (!Number.isInteger(index) || index < 0 || index >= PEOPLE.length) return;
    const wasAdmin = participantIsAdmin(index);
    if (wasAdmin && adminCount() <= 1) {
      toast('В системе должен остаться хотя бы один администратор.');
      return;
    }
    if (wasAdmin) {
      state.adminIndexes = state.adminIndexes.filter((item) => item !== index);
      state.registered += 1;
      state.present = Math.min(state.registered, state.present + 1);
    } else {
      state.adminIndexes = [...state.adminIndexes, index];
      state.registered = Math.max(0, state.registered - 1);
      state.present = Math.max(0, state.present - 1);
    }
    save();
    rerenderParticipantList();
    toast(`Роль обновлена: ${PEOPLE[index]} — ${wasAdmin ? 'участник' : 'администратор'}.`);
    return;
  }
  if (action === 'delete-participant') {
    if (!window.confirm('Удалить участника из реестра? Его голос тоже будет удалён.')) return;
    const userId = Number(actionEl.dataset.userId);
    const index = Number(actionEl.dataset.index);
    if (runtime.api) {
      if (!Number.isFinite(userId)) return;
      try {
        await apiRequest(`/api/admin/users/${userId}`, { method: 'DELETE' });
        await refreshApi({ silent: true });
        renderAll();
        applyMode();
        toast('Участник удалён из реестра.');
      } catch (error) {
        toast(apiErrorMessage(error));
      }
      return;
    }
    if (!Number.isInteger(index) || index < 0 || index >= PEOPLE.length) return;
    if (state.removedDemoIndexes.includes(index)) return;
    state.removedDemoIndexes.push(index);
    state.adminIndexes = state.adminIndexes.filter((item) => item !== index);
    state.registered = Math.max(0, state.registered - 1);
    state.present = Math.max(0, state.present - 1);
    save();
    renderAll();
    applyMode();
    toast('Участник удалён из реестра.');
    return;
  }

  if (action === 'filter-participants') {
    state.adminFilter = actionEl.dataset.filter || 'all';
    save();
    rerenderParticipantList();
    return;
  }
  if (action === 'fullscreen') {
    if (document.fullscreenElement) {
      document.exitFullscreen().catch(() => toast('Не удалось выйти из полноэкранного режима.'));
    } else {
      document.documentElement.requestFullscreen().catch(() => toast('Полноэкранный режим недоступен в этом браузере.'));
    }
  }
});

document.addEventListener('input', (event) => {
  const input = event.target.closest('[data-bind]');
  if (input) {
    const path = input.dataset.bind;
    if (path === 'event.title') state.event.title = input.value;
    if (path === 'event.date') state.event.date = input.value;
    if (path === 'event.time') state.event.time = input.value;
    if (path === 'event.place') state.event.place = input.value;
    if (path === 'poll.title') {
      const poll = pollById(input.dataset.poll) || activePoll();
      if (poll) {
        poll.title = input.value;
        if (runtime.api && poll.status !== 'open' && poll.id !== 'empty' && poll.title.trim()) {
          const pollId = poll.id;
          scheduleApiSave(`poll-${pollId}`, () => {
            const current = pollById(pollId);
            if (!current) return Promise.resolve();
            return apiRequest(`/api/admin/votings/${Number(pollId)}`, {
              method: 'PATCH',
              body: { title: current.title },
            });
          });
        }
      }
    }
    if (path === 'option.label') {
      const poll = pollById(input.dataset.poll) || activePoll();
      const option = poll && poll.options.find((item) => item.id === input.dataset.option);
      if (option) {
        option.label = input.value;
        if (runtime.api && poll && poll.status !== 'open' && option.id && option.label.trim()) {
          const optionId = option.id;
          scheduleApiSave(`option-${optionId}`, () => {
            const currentPoll = pollById(input.dataset.poll) || activePoll();
            const current = currentPoll && currentPoll.options.find((item) => item.id === optionId);
            if (!current) return Promise.resolve();
            return apiRequest(`/api/admin/options/${Number(optionId)}`, {
              method: 'PATCH',
              body: { label: current.label },
            });
          });
        }
      }
    }
    if (path === 'event.date') {
      const weekday = document.getElementById('admin-weekday');
      if (weekday) weekday.value = weekdayRu(input.value);
    }
    save();
    refreshDynamicText();
    return;
  }

  if (event.target.matches('[data-participant-search]')) {
    state.adminSearch = event.target.value;
    save();
    rerenderParticipantList();
  }
});

document.addEventListener('change', (event) => {
  const target = event.target;
  if (target.matches('[data-toggle-results]')) {
    state.showResults = target.checked;
    save();
    renderAll();
    applyMode();
    toast(state.showResults ? 'Промежуточные результаты снова видны на экране.' : 'Промежуточные результаты скрыты до завершения голосования.');
    return;
  }
  if (target.closest('#new-event-form') && target.name === 'date') {
    const weekday = document.getElementById('new-event-weekday');
    if (weekday) weekday.value = weekdayRu(target.value);
  }
});

document.getElementById('new-event-form').addEventListener('submit', createEventFromForm);

document.addEventListener('keydown', (event) => {
  if (event.key === 'Escape') {
    const modal = document.getElementById('new-event-modal');
    if (modal && !modal.hidden) closeNewEventModal();
  }
  if (event.altKey && ['1', '2', '3'].includes(event.key)) {
    event.preventDefault();
    setMode({ '1': 'admin', '2': 'participant', '3': 'screen' }[event.key]);
  }
});

/* ============================================================
   Demo live stream
   ============================================================ */
function liveTick() {
  const poll = activePoll();
  if (!poll || poll.status !== 'open') return;
  const total = totalVotes(poll);
  const meVoted = state.participant.votedPollId === poll.id;
  // Reserve one slot for the participant's own vote, so "Вы уже проголосовали" never overflows the registry.
  const cap = Math.max(0, state.present - (meVoted ? 0 : 1));
  if (total >= cap) return;

  const pool = poll.options.map((option) => ({ option, weight: Math.max(1, option.count + 5) }));
  const sum = pool.reduce((acc, item) => acc + item.weight, 0);
  let point = Math.random() * sum;
  let chosen = pool[0].option;
  for (const item of pool) {
    point -= item.weight;
    if (point <= 0) { chosen = item.option; break; }
  }
  chosen.count = (Number(chosen.count) || 0) + 1;
  poll.timeline = Array.isArray(poll.timeline) ? poll.timeline : [0];
  poll.timeline.push(totalVotes(poll));
  if (poll.timeline.length > 24) poll.timeline.shift();
  save();

  if (state.mode === 'screen') {
    renderScreen();
  } else if (state.mode === 'admin' && !isEditing()) {
    renderAdmin();
  }
}

function updateClockAndCountdown() {
  document.querySelectorAll('[data-clock]').forEach((el) => { el.textContent = timeFull(); });
  document.querySelectorAll('[data-countdown]').forEach((el) => { el.textContent = countdownLabel(); });
}

function applyDemoQuery() {
  if (runtime.api) return;
  let queryStateChanged = false;
  if (params.get('closed') === '1') {
    const poll = activePoll();
    if (poll) {
      poll.status = 'closed';
      poll.endsAt = Date.now();
      queryStateChanged = true;
    }
  }

  if (params.get('voted') === '1') {
    const poll = activePoll();
    if (poll && poll.status === 'open' && state.participant.votedPollId !== poll.id) {
      const choice = poll.options[1] || poll.options[0];
      if (choice) {
        if (totalVotes(poll) < state.present) choice.count = (Number(choice.count) || 0) + 1;
        state.participant.votedPollId = poll.id;
        state.participant.choiceId = choice.id;
        state.participant.votedAt = Date.now();
        poll.timeline = Array.isArray(poll.timeline) ? poll.timeline : [0];
        poll.timeline.push(totalVotes(poll));
        queryStateChanged = true;
      }
    }
  }
  if (queryStateChanged) save();
}

async function refreshTick() {
  if (!runtime.api) {
    liveTick();
    return;
  }
  if (runtime.refreshing) return;
  await refreshApi({ silent: true });
  enforceRoleMode();
  applyRuntimeUi();
  renderCurrentMode();
}

/* ============================================================
   Boot
   ============================================================ */
async function initApp() {
  if (!(await canUseApi())) {
    runtime.api = false;
    return;
  }

  runtime.api = true;
  try {
    await loadApiState();
    if (!runtime.me) {
      runtime.api = false;
      runtime.authError = new Error('Telegram authorization is required');
      toast('Открой мини-приложение через Telegram. Для локальной проверки добавь ?dev_user_id=<id>.');
      return;
    }
    enforceRoleMode();
  } catch (error) {
    runtime.api = false;
    runtime.me = null;
    runtime.authError = error;
    console.warn('API unavailable, falling back to demo mode', error);
    toast('Сервер недоступен — открыт демо-режим.');
  }
}

async function boot() {
  const requestedMode = params.get('mode');
  if (['admin', 'participant', 'screen'].includes(requestedMode)) {
    state.mode = requestedMode;
  }

  await initApp();
  applyDemoQuery();
  applyRuntimeUi();
  renderAll();
  applyMode();

  setInterval(refreshTick, 4500);
  setInterval(updateClockAndCountdown, 1000);
}

boot();
