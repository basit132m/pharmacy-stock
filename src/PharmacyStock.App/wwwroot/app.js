/* ===========================================================================
   Ten Percent Discount Pharmacy - Stock Manager
   Browser side. Plain JavaScript on purpose: no build step, no internet needed,
   nothing to install. The whole UI is served out of the .exe.
   =========================================================================== */

'use strict';

const state = {
  page: 'dashboard',
  counts: null,
  settings: null,
  sources: [],
  board: { search: '', status: 'needs', supplier: '', sort: 'urgency', desc: false, page: 1, pageSize: 50 },
  orders: { includeDead: false },
  lastAlertId: 0,
  alertsPrimed: false,
  timer: null,
};

/* ------------------------------------------------------------------ helpers */

const $ = (sel) => document.querySelector(sel);
const $$ = (sel) => Array.from(document.querySelectorAll(sel));

function esc(value) {
  if (value === null || value === undefined) return '';
  return String(value)
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

/** Trims pointless decimals: 5 not 5.00, but 2.5 stays 2.5. */
function num(value, decimals = 2) {
  if (value === null || value === undefined || value === '') return '0';
  const n = Number(value);
  if (!isFinite(n)) return '0';
  return n.toLocaleString(undefined, { maximumFractionDigits: decimals });
}

function money(value) {
  const n = Number(value) || 0;
  return 'Rs ' + n.toLocaleString(undefined, { maximumFractionDigits: 0 });
}

function timeAgo(iso) {
  if (!iso) return 'never';
  const then = new Date(iso).getTime();
  if (!isFinite(then)) return 'never';
  const seconds = Math.max(0, Math.round((Date.now() - then) / 1000));
  if (seconds < 10) return 'just now';
  if (seconds < 60) return seconds + 's ago';
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return minutes + 'm ago';
  const hours = Math.round(minutes / 60);
  if (hours < 24) return hours + 'h ago';
  return Math.round(hours / 24) + 'd ago';
}

function clockTime(iso) {
  if (!iso) return '';
  const d = new Date(iso);
  if (isNaN(d.getTime())) return '';
  return d.toLocaleString(undefined, { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' });
}

function pillClass(stateText) {
  switch (stateText) {
    case 'OUT': return 'out';
    case 'LOW': return 'low';
    case 'OK': return 'ok';
    default: return 'off';
  }
}

/**
 * How much is on the shelf, written the way a pharmacist reads it.
 * Pharmacy systems count in tablets, so 0.17 packs of Augmentin really means
 * "none left, 1 loose capsule" - which is what gets shown.
 */
function stockParts(row) {
  const perPack = Number(row.unitsPerPack) || 1;
  if (perPack > 1) {
    return {
      main: num(row.wholePacks, 0),
      loose: Number(row.looseUnits) || 0,
    };
  }
  return { main: num(row.qtyPacks), loose: 0 };
}

function stockCell(row) {
  const parts = stockParts(row);
  const loose = parts.loose > 0
    ? ` <span class="tag" title="loose units outside a full pack">+${num(parts.loose, 0)}</span>`
    : '';
  return `<strong>${parts.main}</strong>${loose}`;
}

/** "nothing left" / "1 pack left" / "4 packs left" - never "1 packs". */
function packsPhrase(packs) {
  const n = Number(packs) || 0;
  if (n <= 0) return 'nothing left on the shelf';
  if (n < 1) return 'less than 1 pack left';
  if (n < 2) return '1 pack left';
  return num(n) + ' packs left';
}

/** 30-day sales converted from tablets/capsules into packs. */
function soldPacks(row) {
  const perPack = Number(row.unitsPerPack) || 1;
  return (Number(row.sold30) || 0) / perPack;
}

async function api(path, options = {}) {
  const response = await fetch(path, {
    headers: options.body && !options.raw ? { 'Content-Type': 'application/json' } : undefined,
    ...options,
    body: options.body && !options.raw ? JSON.stringify(options.body) : options.body,
  });
  if (!response.ok) {
    let message = 'Request failed (' + response.status + ')';
    try {
      const data = await response.json();
      if (data && data.message) message = data.message;
    } catch { /* keep the generic message */ }
    throw new Error(message);
  }
  const type = response.headers.get('content-type') || '';
  return type.includes('json') ? response.json() : response.text();
}

function toast(message, tone = '') {
  const el = document.createElement('div');
  el.className = 'toast' + (tone ? ' ' + tone : '');
  el.textContent = message;
  $('#toasts').appendChild(el);
  setTimeout(() => {
    el.style.transition = 'opacity .25s ease';
    el.style.opacity = '0';
    setTimeout(() => el.remove(), 260);
  }, tone === 'bad' ? 7000 : 4000);
}

/** Short beep for out-of-stock, generated in the browser so no sound file is needed. */
function beep() {
  if (!state.settings || !state.settings.soundAlerts) return;
  try {
    const ctx = new (window.AudioContext || window.webkitAudioContext)();
    const osc = ctx.createOscillator();
    const gain = ctx.createGain();
    osc.connect(gain); gain.connect(ctx.destination);
    osc.type = 'sine';
    osc.frequency.value = 880;
    gain.gain.setValueAtTime(0.0001, ctx.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.14, ctx.currentTime + 0.02);
    gain.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + 0.45);
    osc.start();
    osc.stop(ctx.currentTime + 0.46);
    setTimeout(() => ctx.close(), 700);
  } catch { /* sound is a nicety, never an error */ }
}

/* --------------------------------------------------------------- navigation */

function show(page) {
  state.page = page;
  $$('.page').forEach((el) => { el.hidden = el.id !== 'page-' + page; });
  $$('#nav button').forEach((b) => b.setAttribute('aria-selected', String(b.dataset.page === page)));
  window.scrollTo({ top: 0 });
  refreshPage();
}

/** Tile and "see all" buttons jump to the board with a filter already applied. */
function goto(target) {
  if (target === 'orders' || target === 'alerts' || target === 'levels') return show(target);
  state.board.status = target === 'needs' ? 'needs' : target;
  state.board.page = 1;
  $('#boardStatus').value = state.board.status;
  show('board');
}

/* ---------------------------------------------------------------- dashboard */

async function loadDashboard() {
  const data = await api('/api/dashboard');
  state.counts = data.counts;
  applyHeader(data);

  const c = data.counts;
  $('#kpiOut').textContent = num(c.outOfStock, 0);
  $('#kpiLow').textContent = num(c.belowLevel, 0);
  $('#kpiOk').textContent = num(c.ok, 0);
  $('#kpiValue').textContent = money(c.orderValueEstimate);
  $('#kpiValueNote').textContent = (c.outOfStock + c.belowLevel) + ' medicines need ordering';
  $('#unmanagedText').textContent =
    num(c.unmanaged, 0) + ' of ' + num(c.totalItems, 0) +
    ' medicines have no shelf level set, so they are never flagged.';

  const notice = $('#dashNotice');
  if (c.unmanaged === c.totalItems && c.totalItems > 0) {
    notice.innerHTML =
      '<div class="note warn"><strong>No shelf levels are set yet.</strong> ' +
      'Nothing can be flagged until you tell the app how many packs to keep. ' +
      'Go to <strong>Shelf Levels</strong> and press "Work out shelf levels" - it will fill ' +
      'in every medicine from its own sales history in one click.</div>';
  } else if (c.syncStatus === 'error') {
    notice.innerHTML = '<div class="note bad"><strong>Could not read stock.</strong> ' +
      esc(c.syncMessage || '') + ' The figures below are the last ones successfully read.</div>';
  } else if (c.dataSource === 'demo') {
    notice.innerHTML = '<div class="note"><strong>Demo data.</strong> These are sample ' +
      'medicines so you can see the app working. Your shelf levels are saved and will carry ' +
      'over when SeaSoft is connected.</div>';
  } else {
    notice.innerHTML = '';
  }

  const urgent = await api('/api/stock?status=needs&pageSize=10&sort=urgency');
  const body = $('#urgentTable tbody');
  if (!urgent.items.length) {
    body.innerHTML = '<tr><td colspan="5"><div class="empty">' +
      '<div class="empty-icon">&#10003;</div><div class="empty-title">Nothing to order</div>' +
      '<div>Every managed medicine is at or above its shelf level.</div></div></td></tr>';
  } else {
    body.innerHTML = urgent.items.map((row) => `
      <tr>
        <td>
          <div class="med-name">${esc(row.name)}</div>
          <div class="med-meta">${esc(row.supplier || 'No supplier')}</div>
        </td>
        <td class="num">${stockCell(row)}</td>
        <td class="num">${num(row.levelPacks)}</td>
        <td class="num"><strong>${num(row.shortfallPacks)}</strong></td>
        <td><span class="pill ${pillClass(row.stateText)}">${esc(row.stateText)}</span></td>
      </tr>`).join('');
  }

  const alerts = await api('/api/alerts?limit=8');
  renderFeed($('#dashFeed'), alerts, false);
}

/* -------------------------------------------------------------- stock board */

async function loadBoard() {
  const b = state.board;
  const query = new URLSearchParams({
    status: b.status,
    sort: b.sort,
    desc: String(b.desc),
    page: String(b.page),
    pageSize: String(b.pageSize),
  });
  if (b.search) query.set('search', b.search);
  if (b.supplier) query.set('supplier', b.supplier);

  const data = await api('/api/stock?' + query.toString());

  $('#boardCount').textContent = num(data.total, 0) + ' medicines';
  $('#boardPageText').textContent = data.page + ' / ' + data.pages;
  $('#boardPrev').disabled = data.page <= 1;
  $('#boardNext').disabled = data.page >= data.pages;

  const body = $('#boardTable tbody');
  if (!data.items.length) {
    body.innerHTML = '<tr><td colspan="8"><div class="empty">' +
      '<div class="empty-icon">&#9906;</div><div class="empty-title">Nothing matches</div>' +
      '<div>Try a different search or filter.</div></div></td></tr>';
    return;
  }

  body.innerHTML = data.items.map((row) => {
    const level = row.levelPacks === null || row.levelPacks === undefined ? '' : num(row.levelPacks);
    const ratio = row.levelPacks > 0 ? Math.min(100, Math.round((row.qtyPacks / row.levelPacks) * 100)) : 0;
    const tone = pillClass(row.stateText);
    const dead = row.isDeadStock ? ' <span class="tag">no recent sale</span>' : '';
    const ordered = row.orderStatus && row.orderStatus !== 'Pending'
      ? ` <span class="tag ordered">${esc(row.orderStatus)}</span>` : '';

    return `
      <tr>
        <td>
          <div class="med-name">${esc(row.name)}${ordered}</div>
          <div class="med-meta">${esc(row.generic || '')}${row.company ? ' &middot; ' + esc(row.company) : ''}${dead}</div>
        </td>
        <td class="hide-sm"><span class="med-meta">${esc(row.supplier || '-')}</span></td>
        <td class="num">
          ${stockCell(row)}
          <div class="bar" data-tone="${tone}" title="${ratio}% of shelf level"><span style="width:${ratio}%"></span></div>
        </td>
        <td class="num">
          <input class="level-input" type="number" min="0" step="1"
                 value="${esc(level)}" data-code="${esc(row.itemCode)}"
                 aria-label="Shelf level for ${esc(row.name)}">
        </td>
        <td class="num">${row.shortfallPacks > 0 ? '<strong>' + num(row.shortfallPacks) + '</strong>' : '-'}</td>
        <td class="num hide-sm" title="packs sold in the last 30 days">${num(soldPacks(row), 0)}</td>
        <td><span class="pill ${tone}">${esc(row.stateText)}</span></td>
        <td class="num">
          ${level === '' || Number(level) === 0
            ? `<button class="suggest-chip" data-suggest="${esc(row.itemCode)}" data-value="${esc(row.suggestedLevelPacks)}">use ${num(row.suggestedLevelPacks)}</button>`
            : (row.shortfallPacks > 0
                ? `<button class="btn small" data-order="${esc(row.itemCode)}" data-qty="${esc(row.shortfallPacks)}">Add to buy</button>`
                : '')}
        </td>
      </tr>`;
  }).join('');
}

/* -------------------------------------------------------------- shelf levels */

async function loadLevels() {
  const data = await api('/api/stock?sort=sold&desc=true&pageSize=25&status=');
  const body = $('#levelsTable tbody');

  body.innerHTML = data.items.map((row) => {
    const level = row.levelPacks === null || row.levelPacks === undefined ? '' : num(row.levelPacks);
    return `
      <tr>
        <td>
          <div class="med-name">${esc(row.name)}</div>
          <div class="med-meta">${esc(row.company || '')}${row.supplier ? ' &middot; ' + esc(row.supplier) : ''}</div>
        </td>
        <td class="num">${num(soldPacks(row), 0)}</td>
        <td class="num">${stockCell(row)}</td>
        <td class="num">
          <input class="level-input" type="number" min="0" step="1"
                 value="${esc(level)}" data-code="${esc(row.itemCode)}"
                 aria-label="Shelf level for ${esc(row.name)}">
        </td>
        <td class="num">
          <button class="suggest-chip" data-suggest="${esc(row.itemCode)}" data-value="${esc(row.suggestedLevelPacks)}">${num(row.suggestedLevelPacks)}</button>
        </td>
        <td>
          <input type="checkbox" ${row.isManaged ? 'checked' : ''}
                 data-managed="${esc(row.itemCode)}" aria-label="Manage ${esc(row.name)}">
        </td>
      </tr>`;
  }).join('');
}

/* --------------------------------------------------------------- buying list */

async function loadOrders() {
  const groups = await api('/api/orders?includeDead=' + (state.orders.includeDead ? 'true' : 'false'));
  const host = $('#ordersList');

  if (!groups.length) {
    host.innerHTML = '<div class="card"><div class="empty">' +
      '<div class="empty-icon">&#10003;</div><div class="empty-title">Nothing to buy</div>' +
      '<div>No managed medicine is below its shelf level.</div></div></div>';
    return;
  }

  host.innerHTML = groups.map((group) => {
    const outCount = group.lines.filter((l) => l.state === 'OUT').length;
    return `
      <div class="card supplier-card">
        <div class="supplier-head">
          <div>
            <div class="supplier-name">${esc(group.supplier)}</div>
            <div class="supplier-meta">
              ${group.lines.length} medicines
              ${outCount ? ' &middot; <strong style="color:var(--out)">' + outCount + ' finished</strong>' : ''}
              &middot; approx ${money(group.total)}
            </div>
          </div>
          <div>
            <button class="btn small" data-supplier-text="${esc(group.supplier)}">Copy order</button>
            <button class="btn small primary" data-supplier-ordered="${esc(group.supplier)}">Mark all ordered</button>
          </div>
        </div>
        <div class="table-wrap"><table>
          <thead><tr>
            <th>Medicine</th>
            <th class="num">In stock</th>
            <th class="num">Shelf level</th>
            <th class="num">Order</th>
            <th class="num hide-sm">Value</th>
            <th>Status</th>
            <th></th>
          </tr></thead>
          <tbody>
            ${group.lines.map((line) => `
              <tr>
                <td>
                  <div class="med-name">${esc(line.name)}</div>
                  <div class="med-meta">${esc(line.company || '')}${line.isDeadStock ? ' &middot; no recent sale' : ''}</div>
                </td>
                <td class="num">${stockCell({ unitsPerPack: line.unitsPerPack, wholePacks: line.wholePacks, looseUnits: line.looseUnits, qtyPacks: line.inStockPacks })}</td>
                <td class="num">${num(line.levelPacks)}</td>
                <td class="num">
                  <input class="level-input" type="number" min="0" step="1" value="${esc(num(line.qtyPacks))}"
                         data-order-qty="${esc(line.itemCode)}" data-id="${esc(line.orderId || '')}"
                         data-supplier="${esc(group.supplier)}" aria-label="Order quantity for ${esc(line.name)}">
                </td>
                <td class="num hide-sm">${money(line.lineValue)}</td>
                <td>
                  <span class="pill ${pillClass(line.state)}">${esc(line.state)}</span>
                  ${line.status !== 'Pending' ? ' <span class="tag ordered">' + esc(line.status) + '</span>' : ''}
                </td>
                <td class="num">
                  ${line.status === 'Ordered'
                    ? `<button class="btn small" data-received="${esc(line.itemCode)}" data-id="${esc(line.orderId || '')}">Received</button>`
                    : `<button class="btn small" data-mark-ordered="${esc(line.itemCode)}" data-id="${esc(line.orderId || '')}" data-supplier="${esc(group.supplier)}">Ordered</button>`}
                </td>
              </tr>`).join('')}
          </tbody>
        </table></div>
      </div>`;
  }).join('');
}

/* -------------------------------------------------------------------- alerts */

function renderFeed(host, events, allowActions) {
  if (!events.length) {
    host.innerHTML = '<li><div class="empty"><div class="empty-icon">&#9788;</div>' +
      '<div class="empty-title">Nothing has changed</div>' +
      '<div>Alerts appear here when a medicine crosses its shelf level.</div></div></li>';
    return;
  }

  host.innerHTML = events.map((ev) => {
    const recovered = ev.to === 'Ok';
    const title = recovered
      ? esc(ev.name) + ' is back above its shelf level'
      : ev.to === 'Out'
        ? esc(ev.name) + ' has finished'
        : esc(ev.name) + ' is running short';
    const tone = recovered ? 'ok' : ev.to === 'Out' ? 'out' : 'low';

    return `
      <li class="${ev.seen ? '' : 'unseen'}">
        <span class="feed-time">${esc(clockTime(ev.atUtc))}</span>
        <span class="feed-body">
          <span class="feed-title">${title}</span>
          <div class="feed-note">
            ${packsPhrase(ev.qtyPacks)} &middot; shelf level is ${num(ev.levelPacks)}
          </div>
        </span>
        <span class="pill ${tone}">${recovered ? 'BACK' : ev.to === 'Out' ? 'OUT' : 'LOW'}</span>
        ${allowActions && !recovered
          ? `<button class="btn small" data-snooze="${esc(ev.itemCode)}" title="Stay quiet about this medicine for a day">Quiet 24h</button>`
          : ''}
      </li>`;
  }).join('');
}

async function loadAlerts() {
  const events = await api('/api/alerts?limit=200');
  renderFeed($('#alertsFeed'), events, true);
  await api('/api/alerts/seen', { method: 'POST' });
  updateNavCounts({ ...state.counts, newAlerts: 0 });
}

/* ------------------------------------------------------------------ settings */

async function loadSettings() {
  const data = await api('/api/settings');
  state.settings = { ...(state.settings || {}), ...data.settings };
  state.sources = data.sources;

  const select = $('#setDataSource');
  select.innerHTML = data.sources
    .map((s) => `<option value="${esc(s.key)}">${esc(s.display)}</option>`)
    .join('') + '<option value="seasoft" disabled>SeaSoft database (not connected yet)</option>';
  select.value = data.settings.dataSource;

  $('#setCsvPath').value = data.settings.csvPath || '';
  $('#setSyncSeconds').value = data.settings.syncSeconds;
  $('#setHysteresis').value = data.settings.hysteresisPacks;
  $('#setLeadTime').value = data.settings.leadTimeDays;
  $('#setSafety').value = data.settings.safetyFactor;
  $('#setDeadStock').value = data.settings.deadStockDays;
  $('#setSound').checked = data.settings.soundAlerts;
  $('#setPharmacyName').value = data.settings.pharmacyName;
  $('#setAllowLan').checked = data.settings.allowLan;
  $('#setPort').value = data.settings.port;
  $('#dataFolder').textContent = data.dataFolder;
  $('#csvPathField').hidden = data.settings.dataSource !== 'csv';
  $('#demoNote').hidden = data.settings.dataSource !== 'demo';
}

/* ------------------------------------------------------- header and counters */

function applyHeader(data) {
  if (data.pharmacyName) {
    $('#pharmacyName').textContent = data.pharmacyName;
    document.title = data.pharmacyName + ' - Stock Manager';
  }
  state.settings = { ...(state.settings || {}), soundAlerts: data.soundAlerts, syncSeconds: data.syncSeconds };

  const counts = data.counts;
  const pill = $('#syncPill');
  pill.dataset.state = counts.syncStatus;
  $('#syncText').textContent = counts.syncStatus === 'error'
    ? 'Read failed'
    : 'Updated ' + timeAgo(counts.lastSyncUtc);
  pill.title = counts.syncMessage || '';

  updateNavCounts(counts);
  handleNewAlerts(counts);
}

function updateNavCounts(counts) {
  if (!counts) return;

  const board = $('#navBoard');
  const needs = (counts.outOfStock || 0) + (counts.belowLevel || 0);
  board.hidden = needs === 0;
  board.textContent = needs;
  board.dataset.tone = counts.outOfStock > 0 ? '' : 'low';

  const orders = $('#navOrders');
  orders.hidden = !counts.openOrders;
  orders.textContent = counts.openOrders || 0;

  const alerts = $('#navAlerts');
  alerts.hidden = !counts.newAlerts;
  alerts.textContent = counts.newAlerts || 0;
}

/** Windows notification + beep the first time a new shortage appears. */
async function handleNewAlerts(counts) {
  if (!state.alertsPrimed) { state.alertsPrimed = true; return; }
  if (!counts.newAlerts) return;

  const events = await api('/api/alerts?limit=5').catch(() => []);
  const fresh = events.filter((e) => e.id > state.lastAlertId && !e.seen);
  if (!fresh.length) return;

  state.lastAlertId = Math.max(state.lastAlertId, ...events.map((e) => e.id));

  const wentOut = fresh.find((e) => e.to === 'Out');
  if (wentOut) beep();

  const first = fresh[0];
  const text = fresh.length === 1
    ? first.name + (first.to === 'Out' ? ' has finished' : ' is running short')
    : fresh.length + ' medicines need attention';

  toast(text, wentOut ? 'bad' : 'warn');

  if ('Notification' in window && Notification.permission === 'granted') {
    try {
      new Notification('Stock alert', { body: text, icon: '/favicon.svg', tag: 'stock-alert' });
    } catch { /* notifications are optional */ }
  }
}

/* ------------------------------------------------------------------ actions */

async function saveLevel(input) {
  const code = input.dataset.code;
  const value = input.value.trim();
  const level = value === '' ? 0 : Number(value);
  if (!isFinite(level) || level < 0) { toast('Shelf level must be 0 or more.', 'bad'); return; }

  try {
    await api('/api/stock/' + encodeURIComponent(code) + '/level', {
      method: 'PUT',
      body: { levelPacks: level, isManaged: level > 0 },
    });
    input.classList.add('saved');
    setTimeout(() => input.classList.remove('saved'), 900);
    await loadCounts();
  } catch (err) {
    toast(err.message, 'bad');
  }
}

async function loadCounts() {
  const data = await api('/api/dashboard').catch(() => null);
  if (data) { state.counts = data.counts; applyHeader(data); }
}

async function refreshNow() {
  const button = $('#refreshBtn');
  const pill = $('#syncPill');
  button.disabled = true;
  pill.dataset.state = 'busy';
  $('#syncText').textContent = 'Reading...';

  try {
    const result = await api('/api/sync', { method: 'POST' });
    if (!result.ok) toast(result.message, 'bad');
    await refreshPage();
    await loadCounts();
  } catch (err) {
    toast(err.message, 'bad');
  } finally {
    button.disabled = false;
  }
}

async function refreshPage() {
  try {
    switch (state.page) {
      case 'dashboard': await loadDashboard(); break;
      case 'board': await loadBoard(); await loadCounts(); break;
      case 'levels': await loadLevels(); await loadCounts(); break;
      case 'orders': await loadOrders(); await loadCounts(); break;
      case 'alerts': await loadAlerts(); break;
      case 'settings': await loadSettings(); await loadCounts(); break;
    }
  } catch (err) {
    toast(err.message, 'bad');
  }
}

function showCopyBox(title, text) {
  const host = $('#ordersList');
  const card = document.createElement('div');
  card.className = 'card';
  card.innerHTML = `
    <div class="card-head">
      <h2>${esc(title)}</h2>
      <button class="btn small" data-close-copy="1">Close</button>
    </div>
    <div class="card-body">
      <p class="page-sub" style="margin-bottom:8px">Select all and copy, then paste into WhatsApp.</p>
      <textarea class="copybox" readonly></textarea>
    </div>`;
  card.querySelector('textarea').value = text;
  host.prepend(card);
  const area = card.querySelector('textarea');
  area.focus();
  area.select();
}

/* --------------------------------------------------------------- event wiring */

function wire() {
  $('#nav').addEventListener('click', (e) => {
    const button = e.target.closest('button[data-page]');
    if (button) show(button.dataset.page);
  });

  document.addEventListener('click', (e) => {
    const jump = e.target.closest('[data-goto]');
    if (jump) { goto(jump.dataset.goto); return; }

    const closeCopy = e.target.closest('[data-close-copy]');
    if (closeCopy) { closeCopy.closest('.card').remove(); return; }
  });

  $('#refreshBtn').addEventListener('click', refreshNow);

  $('#themeBtn').addEventListener('click', () => {
    const root = document.documentElement;
    const current = root.dataset.theme;
    const next = current === 'dark' ? 'light' : current === 'light' ? '' : 'dark';
    root.dataset.theme = next;
    try { localStorage.setItem('theme', next); } catch { /* private mode */ }
  });

  /* ---- stock board ---- */

  let searchTimer = null;
  $('#boardSearch').addEventListener('input', (e) => {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(() => {
      state.board.search = e.target.value.trim();
      state.board.page = 1;
      loadBoard().catch((err) => toast(err.message, 'bad'));
    }, 250);
  });

  $('#boardStatus').addEventListener('change', (e) => {
    state.board.status = e.target.value;
    state.board.page = 1;
    loadBoard().catch((err) => toast(err.message, 'bad'));
  });

  $('#boardSupplier').addEventListener('change', (e) => {
    state.board.supplier = e.target.value;
    state.board.page = 1;
    loadBoard().catch((err) => toast(err.message, 'bad'));
  });

  $('#boardPageSize').addEventListener('change', (e) => {
    state.board.pageSize = Number(e.target.value);
    state.board.page = 1;
    loadBoard().catch((err) => toast(err.message, 'bad'));
  });

  $('#boardPrev').addEventListener('click', () => {
    if (state.board.page > 1) { state.board.page--; loadBoard(); }
  });
  $('#boardNext').addEventListener('click', () => {
    state.board.page++; loadBoard();
  });

  $('#boardTable').addEventListener('click', (e) => {
    const header = e.target.closest('th.sortable');
    if (header) {
      const sort = header.dataset.sort;
      state.board.desc = state.board.sort === sort ? !state.board.desc : sort !== 'name';
      state.board.sort = sort;
      state.board.page = 1;
      loadBoard();
    }
  });

  $('#boardExport').addEventListener('click', () => { window.location.href = '/api/levels/export.csv'; });

  /* ---- shelf level editing (board + levels page) ---- */

  document.addEventListener('change', (e) => {
    const level = e.target.closest('input.level-input[data-code]');
    if (level) { saveLevel(level); return; }

    const managed = e.target.closest('input[type="checkbox"][data-managed]');
    if (managed) {
      api('/api/stock/' + encodeURIComponent(managed.dataset.managed) + '/managed', {
        method: 'POST',
        body: { isManaged: managed.checked },
      }).then(() => loadCounts()).catch((err) => toast(err.message, 'bad'));
      return;
    }

    const qty = e.target.closest('input[data-order-qty]');
    if (qty) {
      const id = qty.dataset.id ? Number(qty.dataset.id) : null;
      api('/api/orders', {
        method: 'POST',
        body: {
          itemCode: qty.dataset.orderQty,
          qtyPacks: Number(qty.value) || 0,
          status: 'Pending',
          supplier: qty.dataset.supplier,
          id: id || null,
        },
      }).then(() => { toast('Order quantity saved.'); loadOrders(); }).catch((err) => toast(err.message, 'bad'));
    }
  });

  document.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && e.target.closest('input.level-input')) {
      e.preventDefault();
      e.target.blur();
    }
  });

  document.addEventListener('click', (e) => {
    const suggest = e.target.closest('[data-suggest]');
    if (suggest) {
      const code = suggest.dataset.suggest;
      const value = Number(suggest.dataset.value) || 0;
      api('/api/stock/' + encodeURIComponent(code) + '/level', {
        method: 'PUT',
        body: { levelPacks: value, isManaged: value > 0 },
      }).then(() => { toast('Shelf level set to ' + num(value) + ' packs.'); refreshPage(); })
        .catch((err) => toast(err.message, 'bad'));
      return;
    }

    const addOrder = e.target.closest('[data-order]');
    if (addOrder) {
      api('/api/orders', {
        method: 'POST',
        body: { itemCode: addOrder.dataset.order, qtyPacks: Number(addOrder.dataset.qty) || 1, status: 'Pending' },
      }).then(() => { toast('Added to the buying list.'); loadCounts(); })
        .catch((err) => toast(err.message, 'bad'));
      return;
    }

    const snooze = e.target.closest('[data-snooze]');
    if (snooze) {
      api('/api/alerts/' + encodeURIComponent(snooze.dataset.snooze) + '/snooze', {
        method: 'POST', body: { hours: 24 },
      }).then((r) => { toast(r.message); }).catch((err) => toast(err.message, 'bad'));
      return;
    }

    const markOrdered = e.target.closest('[data-mark-ordered]');
    if (markOrdered) {
      api('/api/orders', {
        method: 'POST',
        body: {
          itemCode: markOrdered.dataset.markOrdered,
          qtyPacks: 0,
          status: 'Ordered',
          supplier: markOrdered.dataset.supplier,
          id: markOrdered.dataset.id ? Number(markOrdered.dataset.id) : null,
        },
      }).then(() => { toast('Marked as ordered.'); loadOrders(); loadCounts(); })
        .catch((err) => toast(err.message, 'bad'));
      return;
    }

    const received = e.target.closest('[data-received]');
    if (received) {
      api('/api/orders', {
        method: 'POST',
        body: {
          itemCode: received.dataset.received,
          qtyPacks: 0,
          status: 'Received',
          id: received.dataset.id ? Number(received.dataset.id) : null,
        },
      }).then(() => { toast('Marked as received.'); loadOrders(); loadCounts(); })
        .catch((err) => toast(err.message, 'bad'));
      return;
    }

    const supplierOrdered = e.target.closest('[data-supplier-ordered]');
    if (supplierOrdered) {
      const supplier = supplierOrdered.dataset.supplierOrdered;
      api('/api/orders/supplier/' + encodeURIComponent(supplier) + '/ordered', { method: 'POST' })
        .then((r) => { toast(r.message); loadOrders(); loadCounts(); })
        .catch((err) => toast(err.message, 'bad'));
      return;
    }

    const supplierText = e.target.closest('[data-supplier-text]');
    if (supplierText) {
      const supplier = supplierText.dataset.supplierText;
      api('/api/orders/export.txt?supplier=' + encodeURIComponent(supplier))
        .then(async (text) => {
          try {
            await navigator.clipboard.writeText(text);
            toast('Order copied. Paste it into WhatsApp.');
          } catch {
            showCopyBox('Order for ' + supplier, text);
          }
        })
        .catch((err) => toast(err.message, 'bad'));
    }
  });

  /* ---- shelf levels page ---- */

  $('#applySuggestBtn').addEventListener('click', async (e) => {
    e.target.disabled = true;
    try {
      const result = await api('/api/levels/suggest', {
        method: 'POST', body: { scope: $('#suggestScope').value },
      });
      $('#suggestResult').textContent = result.message;
      toast(result.message);
      await loadLevels();
      await loadCounts();
    } catch (err) {
      toast(err.message, 'bad');
    } finally {
      e.target.disabled = false;
    }
  });

  $('#levelsExport').addEventListener('click', () => { window.location.href = '/api/levels/export.csv'; });
  $('#levelsImportBtn').addEventListener('click', () => $('#levelsImportFile').click());

  $('#levelsImportFile').addEventListener('change', async (e) => {
    const file = e.target.files && e.target.files[0];
    if (!file) return;
    try {
      const text = await file.text();
      const result = await api('/api/levels/import', { method: 'POST', body: text, raw: true });
      $('#importResult').textContent = result.message;
      toast(result.message);
      await loadLevels();
      await loadCounts();
    } catch (err) {
      toast(err.message, 'bad');
    } finally {
      e.target.value = '';
    }
  });

  /* ---- buying list ---- */

  $('#ordersAutofill').addEventListener('click', async (e) => {
    e.target.disabled = true;
    try {
      const result = await api('/api/orders/autofill', { method: 'POST' });
      toast(result.message);
      await loadOrders();
      await loadCounts();
    } catch (err) {
      toast(err.message, 'bad');
    } finally {
      e.target.disabled = false;
    }
  });

  $('#ordersCsv').addEventListener('click', () => { window.location.href = '/api/orders/export.csv'; });

  $('#ordersText').addEventListener('click', async () => {
    try {
      const text = await api('/api/orders/export.txt');
      try {
        await navigator.clipboard.writeText(text);
        toast('Full order copied. Paste it into WhatsApp.');
      } catch {
        showCopyBox('Full order', text);
      }
    } catch (err) {
      toast(err.message, 'bad');
    }
  });

  $('#ordersIncludeDead').addEventListener('change', (e) => {
    state.orders.includeDead = e.target.checked;
    loadOrders().catch((err) => toast(err.message, 'bad'));
  });

  /* ---- alerts ---- */

  $('#alertsSeen').addEventListener('click', async () => {
    await api('/api/alerts/seen', { method: 'POST' }).catch(() => null);
    await loadAlerts();
    toast('Alerts marked as read.');
  });

  /* ---- settings ---- */

  $('#setDataSource').addEventListener('change', (e) => {
    $('#csvPathField').hidden = e.target.value !== 'csv';
  });

  $('#testSourceBtn').addEventListener('click', async (e) => {
    e.target.disabled = true;
    try {
      const result = await api('/api/providers/test', { method: 'POST' });
      $('#testResult').textContent = result.message;
      toast(result.message, result.ok ? '' : 'bad');
    } catch (err) {
      toast(err.message, 'bad');
    } finally {
      e.target.disabled = false;
    }
  });

  $('#saveSettingsBtn').addEventListener('click', async (e) => {
    e.target.disabled = true;
    try {
      await api('/api/settings', {
        method: 'PUT',
        body: {
          pharmacyName: $('#setPharmacyName').value,
          syncSeconds: Number($('#setSyncSeconds').value) || 60,
          hysteresisPacks: Number($('#setHysteresis').value) || 0,
          leadTimeDays: Number($('#setLeadTime').value) || 3,
          safetyFactor: Number($('#setSafety').value) || 1.5,
          deadStockDays: Number($('#setDeadStock').value) || 180,
          soundAlerts: $('#setSound').checked,
          dataSource: $('#setDataSource').value,
          csvPath: $('#setCsvPath').value,
          port: Number($('#setPort').value) || 8420,
          allowLan: $('#setAllowLan').checked,
        },
      });
      toast('Settings saved.');
      await loadSettings();
      await loadCounts();
      restartTimer();
    } catch (err) {
      toast(err.message, 'bad');
    } finally {
      e.target.disabled = false;
    }
  });
}

/* --------------------------------------------------------------- auto refresh */

function restartTimer() {
  if (state.timer) clearInterval(state.timer);
  const seconds = Math.max(10, Number(state.settings && state.settings.syncSeconds) || 60);
  state.timer = setInterval(() => {
    if (document.hidden) return;
    loadCounts();
    if (state.page === 'dashboard' || state.page === 'board' || state.page === 'orders') refreshPage();
  }, seconds * 1000);
}

/* ---------------------------------------------------------------- start-up */

async function start() {
  try {
    const saved = localStorage.getItem('theme');
    if (saved) document.documentElement.dataset.theme = saved;
  } catch { /* private mode */ }

  wire();

  try {
    await loadSettings();
    await loadDashboard();

    const suppliers = await api('/api/suppliers');
    $('#boardSupplier').innerHTML = '<option value="">All suppliers</option>' +
      suppliers.map((s) => `<option value="${esc(s)}">${esc(s)}</option>`).join('');

    const events = await api('/api/alerts?limit=1');
    if (events.length) state.lastAlertId = events[0].id;
  } catch (err) {
    toast('Could not start: ' + err.message, 'bad');
  }

  $('#boardStatus').value = state.board.status;
  restartTimer();

  // Ask about desktop notifications only after the user has clicked something,
  // because browsers reject the request otherwise.
  document.addEventListener('click', function askOnce() {
    document.removeEventListener('click', askOnce);
    if ('Notification' in window && Notification.permission === 'default') {
      Notification.requestPermission().catch(() => null);
    }
  }, { once: true });
}

start();
