/* ============================================================
   VibeScreen — prototipo de la interfaz rediseñada
   Todo el estado vive aquí: no hay servidor detrás.
   ============================================================ */
(() => {
'use strict';

const $  = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));
const clamp = (n, a, b) => Math.min(b, Math.max(a, n));
const group = n => n.toLocaleString('en-US');

/* ── 1. Colores de realce ──────────────────────────────────────
   Dos tonos por color: el oscuro es claro sobre casi negro, el
   claro es oscuro sobre blanco. Los dos pasan de 4.5:1 en su
   fondo; no es el mismo color aclarado ni oscurecido.            */
const ACCENTS = [
  { id: 'rojo',     name: 'Rojo',     hex: '#ff6f61', light: '#cf3a2b' },
  { id: 'naranja',  name: 'Naranja',  hex: '#f59e5b', light: '#b25a08' },
  { id: 'amarillo', name: 'Amarillo', hex: '#e8c55a', light: '#856800' },
  { id: 'verde',    name: 'Verde',    hex: '#63d08a', light: '#0f7a4f' },
  { id: 'turquesa', name: 'Turquesa', hex: '#4ed8c4', light: '#0d7a72' },
  { id: 'azul',     name: 'Azul',     hex: '#6ba6ff', light: '#1f5fd6' },
  { id: 'violeta',  name: 'Violeta',  hex: '#a98bff', light: '#6741cf' },
  { id: 'rosa',     name: 'Rosa',     hex: '#ff7fb6', light: '#b8317a' },
];

/* ── 2. Temas del panel (480 × 320) ────────────────────────── */
const PANEL_W = 480, PANEL_H = 320;
let seq = 0;
const uid = p => `${p}-${++seq}`;

const TEMAS = {
  apaisado: {
    name: 'Ejemplo apaisado',
    bg: { type: 'image', src: 'assets/img/fondo-placa.jpg', iw: 960, ih: 640, fit: 'cover', color: '#07090d' },
    widgets: [
      { id: 'cpu-0-temperature-package', kind: 'Texto', sensor: 'cpu/0/temperature/package',
        x: 26, y: 20, w: 214, h: 58, fs: 50, color: '#f2c94c', value: 42, decimals: 0,
        unit: '°C', showUnit: true, unitPos: 'after', unitSep: true, refresh: 1000 },
      { id: 'cpu-0-voltage-vcore', kind: 'Texto', sensor: 'cpu/0/voltage/vcore',
        x: 26, y: 92, w: 140, h: 40, fs: 34, color: '#f2c94c', value: 1.18, decimals: 2,
        unit: 'V', showUnit: false, unitPos: 'after', unitSep: true, refresh: 1000 },
      { id: 'cpu-0-voltage-vcore-barra', kind: 'Barra', sensor: 'cpu/0/voltage/vcore',
        x: 26, y: 136, w: 196, h: 12, color: '#f2c94c', value: 1.18, min: 0.6, max: 1.5, refresh: 1000 },
      { id: 'cpu-0-clock-cores-average-effect', kind: 'Texto', sensor: 'cpu/0/clock/cores-average-effective',
        x: 26, y: 160, w: 140, h: 38, fs: 32, color: '#f2c94c', value: 0.41, decimals: 2,
        unit: 'GHz', showUnit: false, unitPos: 'after', unitSep: true, refresh: 1000 },
      { id: 'cpu-0-clock-barra', kind: 'Barra', sensor: 'cpu/0/clock/cores-average-effective',
        x: 26, y: 202, w: 196, h: 10, color: '#f2c94c', value: 0.41, min: 0, max: 4.3, refresh: 1000 },
      { id: 'texto-cpu', kind: 'Texto fijo', text: 'AMD RYZEN 7 3700X',
        x: 26, y: 238, w: 232, h: 24, fs: 19, color: '#f2c94c' },
      { id: 'cpu-0-load-total-arco', kind: 'Arco', sensor: 'cpu/0/load/total',
        x: 312, y: 38, w: 132, h: 132, color: '#63d08a', value: 38, min: 0, max: 100,
        unit: '%', showUnit: true, decimals: 0, inside: true, refresh: 1000 },
      { id: 'reloj', kind: 'Fecha y hora', format: 'dd/MM/yyyy + HH:mm:ss',
        x: 264, y: 244, w: 196, h: 56, fs: 22, color: '#45d6e0' },
    ],
  },
  orbita: {
    name: 'Órbita',
    bg: { type: 'image', src: 'assets/img/fondo-orbita.jpg', iw: 960, ih: 639, fit: 'cover', color: '#04060a' },
    widgets: [
      { id: 'gpu-0-temperature-core', kind: 'Texto', sensor: 'gpu/0/temperature/core',
        x: 28, y: 26, w: 200, h: 56, fs: 48, color: '#e6e9ef', value: 54, decimals: 0,
        unit: '°C', showUnit: true, unitPos: 'after', unitSep: true, refresh: 1000 },
      { id: 'texto-gpu', kind: 'Texto fijo', text: 'GEFORCE RTX 3070',
        x: 28, y: 88, w: 230, h: 22, fs: 17, color: '#8fb4ff' },
      { id: 'gpu-0-load-core-arco', kind: 'Arco', sensor: 'gpu/0/load/core',
        x: 320, y: 44, w: 124, h: 124, color: '#8fb4ff', value: 61, min: 0, max: 100,
        unit: '%', showUnit: true, decimals: 0, inside: true, refresh: 1000 },
      { id: 'gpu-0-power-package-barra', kind: 'Barra', sensor: 'gpu/0/power/package',
        x: 28, y: 132, w: 236, h: 14, color: '#8fb4ff', value: 148, min: 0, max: 240, refresh: 1000 },
      { id: 'reloj-2', kind: 'Fecha y hora', format: 'HH:mm',
        x: 28, y: 190, w: 250, h: 92, fs: 70, color: '#ffffff' },
    ],
  },
  minimo: {
    name: 'Mínimo',
    bg: { type: 'color', src: '', fit: 'cover', color: '#07090d' },
    widgets: [
      { id: 'cpu-0-temperature-package', kind: 'Texto', sensor: 'cpu/0/temperature/package',
        x: 40, y: 96, w: 200, h: 80, fs: 72, color: '#e6e9ef', value: 42, decimals: 0,
        unit: '°C', showUnit: true, unitPos: 'after', unitSep: true, refresh: 2000 },
      { id: 'texto-cpu', kind: 'Texto fijo', text: 'CPU',
        x: 40, y: 64, w: 120, h: 24, fs: 18, color: '#828c9e' },
      { id: 'gpu-0-temperature-core', kind: 'Texto', sensor: 'gpu/0/temperature/core',
        x: 272, y: 96, w: 200, h: 80, fs: 72, color: '#e6e9ef', value: 54, decimals: 0,
        unit: '°C', showUnit: true, unitPos: 'after', unitSep: true, refresh: 2000 },
      { id: 'texto-gpu', kind: 'Texto fijo', text: 'GPU',
        x: 272, y: 64, w: 120, h: 24, fs: 18, color: '#828c9e' },
    ],
  },
};
const TEMA_IDS = Object.keys(TEMAS);
const clone = o => JSON.parse(JSON.stringify(o));

/* ── 3. Estado ─────────────────────────────────────────────── */
const state = {
  view: 'panel',
  accent: 'rojo',
  theme: 'dark',                         // dark | light | system
  screenOn: true,
  brightness: 80,
  appliedId: 'apaisado',                 // lo que pinta el panel
  applied: clone(TEMAS.apaisado),
  editId: 'apaisado',                    // lo que hay abierto en el editor
  edit: clone(TEMAS.apaisado),
  dirty: false,
  selected: 'cpu-0-clock-cores-average-effect',
  grid: true, gridStep: 8, snap: true, zoom: 1.5,
  rate: 13284, rects: 1470, sent: 5.7, uptime: 41,
  history: [],
  sensorPick: null, devFilter: '', textFilter: '',
  alerts: [],
};
const BUDGET = 156960;

/* ── 4. Avisos flotantes y diálogo ─────────────────────────── */
const toasts = $('#toasts');
function toast(text, icon = 'i-check') {
  const el = document.createElement('div');
  el.className = 'toast';
  el.innerHTML = `<svg class="ico" aria-hidden="true"><use href="#${icon}"/></svg><span></span>`;
  el.querySelector('span').textContent = text;
  toasts.append(el);
  setTimeout(() => { el.classList.add('is-out'); setTimeout(() => el.remove(), 200); }, 2600);
}

const modal = $('#modal');
function confirmar({ title, text, ok = 'Aceptar', danger = true }) {
  $('#modal-title').textContent = title;
  $('#modal-text').textContent = text;
  const okBtn = $('#modal-ok');
  okBtn.textContent = ok;
  okBtn.className = danger ? 'btn btn-danger' : 'btn btn-primary';
  modal.showModal();
  return new Promise(res => modal.addEventListener('close', () => res(modal.returnValue === 'ok'), { once: true }));
}

/* ── 5. Dibujo del panel ───────────────────────────────────── */
const fmt = (v, d) => Number(v).toFixed(d);

function arcPath(cx, cy, r, from, to) {
  const p = a => [cx + r * Math.cos(a * Math.PI / 180), cy + r * Math.sin(a * Math.PI / 180)];
  const [x1, y1] = p(from), [x2, y2] = p(to);
  return `M ${x1.toFixed(2)} ${y1.toFixed(2)} A ${r} ${r} 0 ${to - from > 180 ? 1 : 0} 1 ${x2.toFixed(2)} ${y2.toFixed(2)}`;
}

function widgetInner(w) {
  if (w.kind === 'Texto' || w.kind === 'Texto fijo') {
    const txt = w.kind === 'Texto fijo' ? w.text
      : fmt(w.value, w.decimals) + (w.showUnit ? (w.unitSep ? ' ' : '') + w.unit : '');
    const el = document.createElement('span');
    el.className = 'w-text';
    el.style.fontSize = `${(w.fs / PANEL_W * 100).toFixed(3)}cqw`;
    el.style.color = w.color;
    el.textContent = txt;
    return el;
  }
  if (w.kind === 'Barra') {
    const pct = clamp((w.value - w.min) / (w.max - w.min) * 100, 0, 100);
    const track = document.createElement('span');
    track.className = 'w-bar-track';
    const fill = document.createElement('span');
    fill.className = 'w-bar-fill';
    fill.style.width = `${pct.toFixed(1)}%`;
    fill.style.background = w.color;
    track.append(fill);
    return track;
  }
  if (w.kind === 'Arco') {
    const pct = clamp((w.value - w.min) / (w.max - w.min), 0, 1);
    const ns = 'http://www.w3.org/2000/svg';
    const svg = document.createElementNS(ns, 'svg');
    svg.setAttribute('viewBox', '0 0 100 100');
    svg.setAttribute('class', 'w-arc');
    const mk = (cls, d, sw) => {
      const p = document.createElementNS(ns, 'path');
      p.setAttribute('class', cls); p.setAttribute('d', d); p.setAttribute('stroke-width', sw);
      return p;
    };
    svg.append(mk('w-arc-track', arcPath(50, 50, 40, 135, 405), 9));
    const fill = mk('w-arc-fill', arcPath(50, 50, 40, 135, 135 + 270 * pct), 9);
    fill.setAttribute('stroke', w.color);
    svg.append(fill);
    if (w.inside) {
      const t = document.createElementNS(ns, 'text');
      t.setAttribute('x', 50); t.setAttribute('y', 58);
      t.setAttribute('text-anchor', 'middle');
      t.setAttribute('fill', w.color);
      t.setAttribute('font-size', 26);
      t.setAttribute('font-family', 'JetBrains Mono, monospace');
      t.setAttribute('font-weight', 700);
      t.textContent = fmt(w.value, w.decimals) + (w.showUnit ? w.unit : '');
      svg.append(t);
    }
    return svg;
  }
  // Fecha y hora
  const box = document.createElement('span');
  box.className = 'w-sub';
  box.style.color = w.color;
  box.style.fontSize = `${(w.fs / PANEL_W * 100).toFixed(3)}cqw`;
  box.style.fontWeight = '700';
  box.style.textAlign = 'center';
  box.style.display = 'grid';
  box.style.gap = '0.15em';
  const now = new Date();
  const two = n => String(n).padStart(2, '0');
  const fecha = `${two(now.getDate())}/${two(now.getMonth() + 1)}/${now.getFullYear()}`;
  const hora  = `${two(now.getHours())}:${two(now.getMinutes())}:${two(now.getSeconds())}`;
  if (w.format === 'HH:mm') {
    box.append(Object.assign(document.createElement('span'), { textContent: hora.slice(0, 5) }));
  } else if (w.format === 'HH:mm:ss') {
    box.append(Object.assign(document.createElement('span'), { textContent: hora }));
  } else {
    box.append(Object.assign(document.createElement('span'), { textContent: fecha }));
    box.append(Object.assign(document.createElement('span'), { textContent: hora }));
  }
  return box;
}

function drawTheme(host, theme, { editable = false } = {}) {
  host.replaceChildren();
  host.style.background = theme.bg.color;
  if (theme.bg.type === 'image') {
    const img = document.createElement('img');
    img.className = 'render-bg';
    img.src = theme.bg.src;
    img.alt = '';
    img.width = theme.bg.iw; img.height = theme.bg.ih;
    img.style.objectFit = theme.bg.fit === 'stretch' ? 'fill' : theme.bg.fit;
    host.append(img);
  }
  if (editable && state.grid) {
    const g = document.createElement('div');
    g.className = 'grid-overlay';
    const sx = state.gridStep / PANEL_W * 100, sy = state.gridStep / PANEL_H * 100;
    g.style.backgroundImage =
      'linear-gradient(to right, rgba(255,255,255,.10) 1px, transparent 1px),' +
      'linear-gradient(to bottom, rgba(255,255,255,.10) 1px, transparent 1px)';
    g.style.backgroundSize = `${sx}% ${sy}%`;
    host.append(g);
  }
  theme.widgets.forEach(w => {
    const el = document.createElement(editable ? 'button' : 'div');
    el.className = 'w';
    if (editable) {
      el.type = 'button';
      el.dataset.wid = w.id;
      el.setAttribute('aria-label', `${w.kind} · ${w.sensor || w.text || w.format}`);
      if (w.id === state.selected) {
        el.classList.add('is-sel');
        ['nw', 'ne', 'sw', 'se'].forEach(c => {
          const h = document.createElement('span');
          h.className = `handle handle-${c}`;
          h.setAttribute('aria-hidden', 'true');
          el.append(h);
        });
      }
    }
    el.style.left   = `${(w.x / PANEL_W * 100).toFixed(3)}%`;
    el.style.top    = `${(w.y / PANEL_H * 100).toFixed(3)}%`;
    el.style.width  = `${(w.w / PANEL_W * 100).toFixed(3)}%`;
    el.style.height = `${(w.h / PANEL_H * 100).toFixed(3)}%`;
    el.style.padding = '0'; el.style.border = '0'; el.style.background = 'none';
    el.append(widgetInner(w));
    host.append(el);
  });
}

function paintAll() {
  const live = $('#panel-live');
  drawTheme(live, state.applied);
  live.classList.toggle('is-off', !state.screenOn);
  live.style.filter = state.screenOn ? `brightness(${0.45 + state.brightness / 100 * 0.75})` : '';
  drawTheme($('#theme-thumb'), state.applied);
  $('#theme-thumb').classList.toggle('is-off', !state.screenOn);
  drawTheme($('#editor-render'), state.edit, { editable: true });
  $('#panel-live-name').textContent = state.applied.name;
  $('#theme-name').textContent = state.applied.name;
  $('#titlebar-ctx').textContent = state.edit.name;
}

/* ── 6. Coste del tema ─────────────────────────────────────── */
function themeCost(theme) {
  let total = 0, worst = { id: '—', cost: 0 };
  theme.widgets.forEach(w => {
    if (!w.refresh) return;                    // texto fijo: se pinta una vez
    const px = w.w * w.h;
    const hz = 1000 / w.refresh;
    const cost = Math.round(px * 2 * hz * 0.31);
    total += cost;
    if (cost > worst.cost) worst = { id: w.sensor || w.id, cost };
  });
  return { total, worst };
}
function paintCost() {
  const { total, worst } = themeCost(state.edit);
  const pct = clamp(Math.round(total / BUDGET * 100), 0, 100);
  $('#cost-rate').textContent = `${group(total)} B/s`;
  $('#cost-pct').textContent = `${pct} %`;
  $('#cost-worst').textContent = worst.id;
  const bar = $('#cost-bar');
  bar.style.width = `${pct}%`;
  bar.classList.toggle('is-warn', pct > 70);
  $('.editor-foot .bar').setAttribute('aria-label', `El tema usa el ${pct} % del presupuesto del enlace`);
}

/* ── 7. Vistas ─────────────────────────────────────────────── */
function setView(v) {
  state.view = v;
  $$('.view').forEach(s => {
    const on = s.id === `view-${v}`;
    s.hidden = !on;
    s.classList.toggle('is-active', on);
  });
  $$('.rail-item').forEach(b => {
    const on = b.dataset.view === v;
    b.classList.toggle('is-active', on);
    if (on) b.setAttribute('aria-current', 'page'); else b.removeAttribute('aria-current');
  });
  $('#content').scrollTop = 0;
}
$$('.rail-item').forEach(b => b.addEventListener('click', () => setView(b.dataset.view)));

/* ── 8. Panel ──────────────────────────────────────────────── */
function setScreen(on) {
  state.screenOn = on;
  const t = $('#screen-toggle');
  t.setAttribute('aria-checked', String(on));
  $('#screen-toggle-text').textContent = on ? 'Encendida' : 'Apagada';
  $('#screen-flag').textContent = on ? 'Encendida' : 'Apagada';
  $('#screen-flag').className = on ? 'flag flag-ok' : 'flag';
  $('#service-flag').innerHTML = on
    ? '<span class="dot dot-ok"></span>Pintando'
    : '<span class="dot dot-off"></span>En espera';
  $('#service-flag').className = on ? 'flag flag-ok' : 'flag';
  $('#status-state').textContent = on ? 'Pintando' : 'En espera';
  $('#status-state').classList.toggle('is-off', !on);
  $('#status-dot').className = on ? 'dot dot-ok' : 'dot dot-off';
  $('#rail-dot').className = on ? 'dot dot-ok' : 'dot dot-off';
  $('#rail-state').textContent = on ? 'COM3 · pintando' : 'COM3 · en espera';
  $('#rail-device').classList.toggle('is-off', !on);
  if (!on) addAlert('Pantalla apagada desde la ventana. El enlace sigue abierto.');
  paintAll();
}
$('#screen-toggle').addEventListener('click', () => setScreen(!state.screenOn));

function addAlert(text) {
  const now = new Date();
  const two = n => String(n).padStart(2, '0');
  state.alerts.unshift({ text, time: `${two(now.getHours())}:${two(now.getMinutes())}` });
  const list = $('#alerts-list');
  list.replaceChildren();
  state.alerts.slice(0, 4).forEach(a => {
    const li = document.createElement('li');
    li.className = 'alert';
    li.innerHTML = '<svg class="ico" aria-hidden="true"><use href="#i-alert"/></svg>' +
      '<span class="od-stack" style="--od-gap:2px"><span class="alert-text"></span><span class="alert-time"></span></span>';
    li.querySelector('.alert-text').textContent = a.text;
    li.querySelector('.alert-time').textContent = a.time;
    list.append(li);
  });
  list.hidden = false;
  $('#alerts-empty').hidden = true;
  $('#alerts-flag').textContent = `${state.alerts.length} sin leer`;
  $('#alerts-flag').className = 'flag flag-warn';
}

const bright = $('#bright');
function paintBright() {
  bright.style.setProperty('--fill', `${state.brightness}%`);
  $('#bright-out').textContent = state.brightness;
  const live = $('#panel-live');
  if (state.screenOn) live.style.filter = `brightness(${0.45 + state.brightness / 100 * 0.75})`;
}
bright.addEventListener('input', () => { state.brightness = +bright.value; paintBright(); });
bright.addEventListener('change', () => toast(`Brillo al ${state.brightness} %`, 'i-sun'));

$('#theme-reload').addEventListener('click', () => {
  paintAll();
  toast(`Recargado «${state.applied.name}»`, 'i-refresh');
});
$('#theme-random').addEventListener('click', () => {
  const otros = TEMA_IDS.filter(t => t !== state.appliedId);
  const pick = otros[Math.floor(Math.random() * otros.length)];
  state.appliedId = pick;
  state.applied = clone(TEMAS[pick]);
  paintAll();
  toast(`Pintando «${state.applied.name}»`, 'i-shuffle');
});
$('#theme-mode').addEventListener('change', e => {
  const map = {
    random: 'En el siguiente arranque elegirá otro.',
    fixed:  'En el siguiente arranque pintará este mismo tema.',
    last:   'En el siguiente arranque repetirá el último aplicado.',
  };
  $('#theme-next').textContent = map[e.target.value];
});

/* sparkline del enlace */
function paintSpark() {
  const h = state.history;
  const min = Math.min(...h), max = Math.max(...h), span = Math.max(1, max - min);
  const pts = h.map((v, i) => [i / (h.length - 1) * 240, 44 - (v - min) / span * 38]);
  const d = pts.map((p, i) => `${i ? 'L' : 'M'} ${p[0].toFixed(1)} ${p[1].toFixed(1)}`).join(' ');
  $('#spark-line').setAttribute('d', d);
  $('#spark-area').setAttribute('d', `${d} L 240 48 L 0 48 Z`);
  $('#spark').setAttribute('aria-label',
    `Caudal del último minuto, entre ${group(Math.round(min))} y ${group(Math.round(max))} B/s`);
}

/* ── 9. Editor ─────────────────────────────────────────────── */
function setDirty(d) {
  state.dirty = d;
  const el = $('#save-state');
  el.classList.toggle('is-dirty', d);
  el.innerHTML = d
    ? '<svg class="ico" aria-hidden="true"><use href="#i-alert"/></svg>sin guardar'
    : '<svg class="ico" aria-hidden="true"><use href="#i-check"/></svg>guardado';
}

function selected() { return state.edit.widgets.find(w => w.id === state.selected) || null; }

function paintWidgetList() {
  const list = $('#wlist');
  list.replaceChildren();
  state.edit.widgets.forEach(w => {
    const li = document.createElement('li');
    li.setAttribute('role', 'none');
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'witem' + (w.id === state.selected ? ' is-sel' : '');
    b.setAttribute('role', 'option');
    b.setAttribute('aria-selected', String(w.id === state.selected));
    b.innerHTML = '<span class="witem-kind"></span><span class="witem-id"></span>';
    b.querySelector('.witem-kind').textContent = w.kind;
    b.querySelector('.witem-id').textContent = w.sensor || w.id;
    b.addEventListener('click', () => select(w.id));
    li.append(b);
    list.append(li);
  });
  $('#widget-count').textContent = state.edit.widgets.length;
}

const UNITS = ['°C', 'V', 'GHz', 'MHz', 'W', '%', 'RPM', 'MB', 'B/s'];
function field(label, control) {
  return `<div class="prop-row"><span class="field-label">${label}</span>${control}</div>`;
}
function paintProps() {
  const w = selected();
  const body = $('#props-body');
  $('#props-id').textContent = w ? w.id : '—';
  if (!w) {
    body.innerHTML = '<p class="field-help">Elige un widget en el lienzo o en la lista de arriba para ver sus propiedades.</p>';
    return;
  }
  const opts = (arr, cur) => arr.map(o => `<option${o === cur ? ' selected' : ''}>${o}</option>`).join('');
  let html = field('Id', `<input class="input mono" data-p="id" value="${w.id}">`);

  html += `<details class="prop-sub" open><summary>Caja<span class="prop-summary">${w.x},${w.y} · ${w.w}×${w.h}${w.refresh ? ` · ${w.refresh} ms` : ''}</span></summary><div class="prop-sub-body">
    ${field('Posición', `<span class="od-row" style="--od-gap:8px"><input class="input input-num mono" type="number" data-p="x" value="${w.x}" aria-label="Posición X"><input class="input input-num mono" type="number" data-p="y" value="${w.y}" aria-label="Posición Y"></span>`)}
    ${field('Tamaño', `<span class="od-row" style="--od-gap:8px"><input class="input input-num mono" type="number" data-p="w" value="${w.w}" aria-label="Ancho"><input class="input input-num mono" type="number" data-p="h" value="${w.h}" aria-label="Alto"></span>`)}
    ${w.fs ? field('Letra', `<input class="input input-num mono" type="number" data-p="fs" value="${w.fs}" aria-label="Tamaño de la letra">`) : ''}
    ${w.refresh ? field('Refresco', `<span class="od-row" style="--od-gap:8px"><input class="input input-num mono" type="number" data-p="refresh" step="100" value="${w.refresh}" aria-label="Refresco en milisegundos"><span class="field-help">ms</span></span>`) : ''}
  </div></details>`;

  if (w.kind === 'Texto fijo') {
    html += `<details class="prop-sub" open><summary>Texto</summary><div class="prop-sub-body">
      ${field('Contenido', `<input class="input" data-p="text" value="${w.text}">`)}
      ${field('Color', `<input class="input mono" data-p="color" value="${w.color}">`)}
    </div></details>`;
  } else if (w.kind === 'Fecha y hora') {
    html += `<details class="prop-sub" open><summary>Formato</summary><div class="prop-sub-body">
      ${field('Formato', `<select class="select" data-p="format">${opts(['dd/MM/yyyy + HH:mm:ss', 'HH:mm:ss', 'HH:mm'], w.format)}</select>`)}
      ${field('Color', `<input class="input mono" data-p="color" value="${w.color}">`)}
      <p class="field-help mono">Ahora se vería: ${w.format === 'HH:mm' ? '19:56' : w.format === 'HH:mm:ss' ? '19:56:20' : '26/09/2026 · 19:56:20'}</p>
    </div></details>`;
  } else {
    html += `<details class="prop-sub" open><summary>Valor<span class="prop-summary">${w.sensor.split('/').pop()}</span></summary><div class="prop-sub-body">
      ${field('Sensor', `<input class="input mono" data-p="sensor" value="${w.sensor}">`)}
      ${w.kind !== 'Barra' ? field('Decimales', `<input class="input input-num mono" type="number" min="0" max="3" data-p="decimals" value="${w.decimals}">`) : ''}
      ${w.kind !== 'Barra' ? field('Unidad', `<select class="select" data-p="unit">${opts(UNITS, w.unit)}</select>`) : ''}
      ${w.kind !== 'Barra' ? `<label class="check"><input type="checkbox" data-p="showUnit"${w.showUnit ? ' checked' : ''}><span class="check-box" aria-hidden="true"><svg class="ico"><use href="#i-check"/></svg></span><span>Mostrar la unidad</span></label>` : ''}
      ${w.kind !== 'Barra' ? `<label class="check"><input type="checkbox" data-p="unitSep"${w.unitSep ? ' checked' : ''}><span class="check-box" aria-hidden="true"><svg class="ico"><use href="#i-check"/></svg></span><span>Separarla del número</span></label>` : ''}
      ${w.kind === 'Barra' || w.kind === 'Arco' ? field('Recorrido', `<span class="od-row" style="--od-gap:8px"><input class="input input-num mono" type="number" step="0.1" data-p="min" value="${w.min}" aria-label="Mínimo"><input class="input input-num mono" type="number" step="0.1" data-p="max" value="${w.max}" aria-label="Máximo"></span>`) : ''}
      ${field('Color', `<input class="input mono" data-p="color" value="${w.color}">`)}
    </div></details>`;

    html += `<details class="prop-sub"><summary>Color por valor<span class="prop-summary">2 cortes</span></summary><div class="prop-sub-body">
      <div class="cut-row"><span class="cut-swatch" style="background:#63d08a"></span><span>Verde</span><span class="cut-range">menos de 40 ${w.unit || ''}</span></div>
      <div class="cut-row"><span class="cut-swatch" style="background:#e8a33d"></span><span>Ámbar</span><span class="cut-range">40 a 80 ${w.unit || ''}</span></div>
      <div class="cut-row"><span class="cut-swatch" style="background:#ff6f61"></span><span>Rojo</span><span class="cut-range">80 ${w.unit || ''} o más</span></div>
      <button type="button" class="btn btn-sm" data-spread="1">Repartir por el recorrido</button>
    </div></details>`;
  }
  body.innerHTML = html;
  const spread = body.querySelector('[data-spread]');
  if (spread) spread.addEventListener('click', () => toast('Cortes repartidos por el recorrido', 'i-check'));
}

/* un solo oyente para todas las propiedades: el cuerpo se reescribe, el nodo no */
$('#props-body').addEventListener('input', e => {
  const p = e.target.dataset.p;
  if (!p) return;
  const w = selected();
  if (!w) return;
  w[p] = e.target.type === 'checkbox' ? e.target.checked
       : e.target.type === 'number' ? Number(e.target.value)
       : e.target.value;
  if (p === 'id') { state.selected = w.id; $('#props-id').textContent = w.id; }
  setDirty(true);
  drawTheme($('#editor-render'), state.edit, { editable: true });
  paintWidgetList();
  paintCost();
});

function select(id) {
  state.selected = id;
  drawTheme($('#editor-render'), state.edit, { editable: true });
  paintWidgetList();
  paintProps();
}

$('#editor-render').addEventListener('click', e => {
  const el = e.target.closest('[data-wid]');
  if (el) select(el.dataset.wid);
});

$('#layout-pick').addEventListener('change', async e => {
  const next = e.target.value;
  if (state.dirty) {
    const ok = await confirmar({
      title: 'Hay cambios sin guardar',
      text: `«${state.edit.name}» tiene cambios sin guardar. Si abres otro tema se pierden.`,
      ok: 'Abrir de todas formas',
    });
    if (!ok) { e.target.value = state.editId; return; }
  }
  state.editId = next;
  state.edit = clone(TEMAS[next]);
  state.selected = state.edit.widgets[0].id;
  setDirty(false);
  paintAll(); paintWidgetList(); paintProps(); paintCost();
});

$('#ed-save').addEventListener('click', () => { setDirty(false); toast(`Guardado «${state.edit.name}»`, 'i-save'); });
$('#ed-new').addEventListener('click', () => toast('Tema nuevo: parte de un panel vacío', 'i-plus'));
$('#ed-import').addEventListener('click', () => toast('Importar: elige un .json de tema', 'i-import'));
$('#ed-apply').addEventListener('click', () => {
  state.appliedId = state.editId;
  state.applied = clone(state.edit);
  paintAll();
  toast(`Enviado al panel: «${state.applied.name}»`, 'i-send');
});
$('#ed-delete').addEventListener('click', async () => {
  const ok = await confirmar({
    title: `¿Eliminar «${state.edit.name}»?`,
    text: 'Se borra su carpeta con la imagen de fondo dentro. No hay vuelta atrás.',
    ok: 'Eliminar el tema',
  });
  if (ok) { addAlert(`Tema «${state.edit.name}» eliminado.`); toast('Tema eliminado', 'i-trash'); }
});

$('#grid-on').addEventListener('change', e => { state.grid = e.target.checked; drawTheme($('#editor-render'), state.edit, { editable: true }); });
$('#grid-step').addEventListener('input', e => { state.gridStep = clamp(+e.target.value || 8, 2, 32); drawTheme($('#editor-render'), state.edit, { editable: true }); });
$('#grid-snap').addEventListener('change', e => { state.snap = e.target.checked; });
$('#zoom').addEventListener('change', e => {
  state.zoom = +e.target.value;
  $('#editor-render').style.setProperty('--zoom', state.zoom);
});
$$('.seg-btn[data-bg]').forEach(b => b.addEventListener('click', () => {
  $$('.seg-btn[data-bg]').forEach(o => { o.classList.toggle('is-on', o === b); o.setAttribute('aria-checked', String(o === b)); });
  state.edit.bg.type = b.dataset.bg;
  $('#bg-image-field').hidden = b.dataset.bg !== 'image';
  $('#bg-color-field').hidden = b.dataset.bg !== 'color';
  setDirty(true);
  drawTheme($('#editor-render'), state.edit, { editable: true });
}));
$('#bg-fit').addEventListener('change', e => {
  state.edit.bg.fit = e.target.value;
  setDirty(true);
  drawTheme($('#editor-render'), state.edit, { editable: true });
});

function addWidget(kind, sensor) {
  const base = {
    id: uid(kind === 'Fecha y hora' ? 'reloj' : (sensor || 'texto').replace(/\//g, '-')),
    kind, x: 32, y: 32, w: kind === 'Arco' ? 110 : 160, h: kind === 'Barra' ? 12 : (kind === 'Arco' ? 110 : 40),
    color: '#f2c94c', refresh: 1000,
  };
  if (kind === 'Texto fijo') Object.assign(base, { text: 'TEXTO', fs: 20, refresh: 0 });
  else if (kind === 'Fecha y hora') Object.assign(base, { format: 'HH:mm:ss', fs: 26, color: '#45d6e0', refresh: 0 });
  else Object.assign(base, {
    sensor: sensor || 'cpu/0/temperature/package', value: 42, decimals: kind === 'Barra' ? 0 : 1,
    unit: '°C', showUnit: true, unitPos: 'after', unitSep: true, min: 0, max: 100, fs: kind === 'Barra' ? 0 : 28,
    inside: kind === 'Arco',
  });
  if (kind === 'Barra') delete base.fs;
  state.edit.widgets.push(base);
  setDirty(true);
  select(base.id);
  paintCost();
  return base;
}
$$('[data-add]').forEach(b => b.addEventListener('click', () => {
  const sensor = $('#add-search').value.trim() || undefined;
  const w = addWidget(b.dataset.add, sensor);
  toast(`Añadido: ${w.kind}`, 'i-plus');
}));

/* ── 10. Sensores ──────────────────────────────────────────── */
const DEVICES = [];
function dev(name, id, rows) { DEVICES.push({ name, id, rows }); }
function span(n, f) { return Array.from({ length: n }, (_, i) => f(i + 1)); }

dev('AMD Ryzen 7 3700X', 'cpu-0', [
  { id: 'cpu/0/clock/bus-speed', name: 'Bus Speed', v: 100.001, d: 3, u: 'MHz', j: 0.002 },
  ...span(8, i => ({ id: `cpu/0/clock/core-${i}-effective`, name: `Core #${i} (Effective)`, v: 180 + i * 96, d: 1, u: 'MHz', j: 240 })),
  ...span(8, i => ({ id: `cpu/0/clock/core-${i - 1}`, name: `Core #${i}`, v: 4250, d: 0, u: 'MHz', j: 0 })),
  { id: 'cpu/0/clock/cores-average', name: 'Cores (Average)', v: 4250, d: 0, u: 'MHz', j: 0 },
  { id: 'cpu/0/clock/cores-average-effective', name: 'Cores (Average Effective)', v: 533, d: 0, u: 'MHz', j: 160 },
  ...span(8, i => ({ id: `cpu/0/factor/core-${i}`, name: `Core #${i}`, v: 42.5, d: 1, u: '', j: 0 })),
  ...span(8, i => ({ id: `cpu/0/load/core-${i}`, name: `CPU Core #${i}`, v: 3 + i * 2, d: 1, u: '%', j: 9 })),
  ...span(8, i => ({ id: `cpu/0/load/core-${i}-thread-1`, name: `CPU Core #${i} Thread #1`, v: 2 + i, d: 1, u: '%', j: 8 })),
  ...span(8, i => ({ id: `cpu/0/load/core-${i}-thread-2`, name: `CPU Core #${i} Thread #2`, v: 1 + i, d: 1, u: '%', j: 8 })),
  { id: 'cpu/0/load/total', name: 'CPU Total', v: 38, d: 1, u: '%', j: 6 },
  { id: 'cpu/0/load/core-max', name: 'CPU Core Max', v: 21.9, d: 1, u: '%', j: 12 },
  ...span(8, i => ({ id: `cpu/0/temperature/core-${i}`, name: `Core #${i}`, v: 39 + i * 0.4, d: 1, u: '°C', j: 1.6 })),
  { id: 'cpu/0/temperature/package', name: 'Core (Tctl/Tdie)', v: 42, d: 1, u: '°C', j: 1.8 },
  { id: 'cpu/0/temperature/ccd-1', name: 'CCD #1 (Tdie)', v: 40.5, d: 1, u: '°C', j: 1.4 },
  { id: 'cpu/0/temperature/core-max', name: 'Core Max', v: 43.2, d: 1, u: '°C', j: 1.5 },
  { id: 'cpu/0/temperature/core-average', name: 'Core Average', v: 41.1, d: 1, u: '°C', j: 1.2 },
  { id: 'cpu/0/voltage/vcore', name: 'Core (SVI2 TFN)', v: 1.18, d: 3, u: 'V', j: 0.14 },
  { id: 'cpu/0/voltage/soc', name: 'SoC (SVI2 TFN)', v: 1.1, d: 3, u: 'V', j: 0.02 },
  ...span(8, i => ({ id: `cpu/0/voltage/core-${i}`, name: `Core #${i} VID`, v: 1.2, d: 3, u: 'V', j: 0.12 })),
  { id: 'cpu/0/power/package', name: 'Package', v: 44.8, d: 1, u: 'W', j: 9 },
  { id: 'cpu/0/power/cores', name: 'Core (SVI2 TFN)', v: 18.2, d: 1, u: 'W', j: 7 },
  { id: 'cpu/0/power/soc', name: 'SoC (SVI2 TFN)', v: 12.9, d: 1, u: 'W', j: 1.5 },
  ...span(8, i => ({ id: `cpu/0/power/core-${i}`, name: `Core #${i} Power`, v: 1.4 + i * 0.2, d: 2, u: 'W', j: 1.1 })),
]);

dev('NVIDIA GeForce RTX 3070', 'gpu-0', [
  { id: 'gpu/0/temperature/core', name: 'GPU Core', v: 54, d: 1, u: '°C', j: 2.2 },
  { id: 'gpu/0/temperature/hot-spot', name: 'GPU Hot Spot', v: 63.4, d: 1, u: '°C', j: 2.4 },
  { id: 'gpu/0/temperature/memory-junction', name: 'GPU Memory Junction', v: 60, d: 1, u: '°C', j: 2 },
  { id: 'gpu/0/clock/core', name: 'GPU Core', v: 1875, d: 0, u: 'MHz', j: 90 },
  { id: 'gpu/0/clock/memory', name: 'GPU Memory', v: 7000, d: 0, u: 'MHz', j: 0 },
  { id: 'gpu/0/clock/shader', name: 'GPU Shader', v: 1875, d: 0, u: 'MHz', j: 90 },
  { id: 'gpu/0/load/core', name: 'GPU Core', v: 61, d: 1, u: '%', j: 14 },
  { id: 'gpu/0/load/memory-controller', name: 'GPU Memory Controller', v: 27, d: 1, u: '%', j: 11 },
  { id: 'gpu/0/load/video-engine', name: 'GPU Video Engine', v: 0, d: 1, u: '%', j: 3 },
  { id: 'gpu/0/load/bus', name: 'GPU Bus', v: 6, d: 1, u: '%', j: 4 },
  { id: 'gpu/0/load/memory', name: 'GPU Memory', v: 44.8, d: 1, u: '%', j: 3 },
  { id: 'gpu/0/load/board-power', name: 'GPU Board Power', v: 63.7, d: 1, u: '%', j: 9 },
  { id: 'gpu/0/power/package', name: 'GPU Package', v: 148, d: 1, u: 'W', j: 22 },
  { id: 'gpu/0/voltage/core', name: 'GPU Core', v: 0.95, d: 3, u: 'V', j: 0.05 },
  { id: 'gpu/0/fan/1', name: 'GPU Fan 1', v: 1320, d: 0, u: 'RPM', j: 90 },
  { id: 'gpu/0/fan/2', name: 'GPU Fan 2', v: 1310, d: 0, u: 'RPM', j: 90 },
  { id: 'gpu/0/control/fan', name: 'GPU Fan', v: 42, d: 0, u: '%', j: 4 },
  { id: 'gpu/0/memory/used', name: 'GPU Memory Used', v: 3572, d: 0, u: 'MB', j: 120 },
  { id: 'gpu/0/memory/free', name: 'GPU Memory Free', v: 4620, d: 0, u: 'MB', j: 120 },
  { id: 'gpu/0/memory/total', name: 'GPU Memory Total', v: 8192, d: 0, u: 'MB', j: 0 },
  { id: 'gpu/0/throughput/pcie-rx', name: 'GPU PCIe Rx', v: 184, d: 0, u: 'MB', j: 90 },
  { id: 'gpu/0/throughput/pcie-tx', name: 'GPU PCIe Tx', v: 96, d: 0, u: 'MB', j: 60 },
]);

dev('Memoria', 'ram', [
  { id: 'ram/load/memory', name: 'Memory', v: 46.2, d: 1, u: '%', j: 3 },
  { id: 'ram/data/used', name: 'Memory Used', v: 14.7, d: 2, u: 'GB', j: 0.6 },
  { id: 'ram/data/available', name: 'Memory Available', v: 17.1, d: 2, u: 'GB', j: 0.6 },
  { id: 'ram/load/virtual', name: 'Virtual Memory', v: 39.4, d: 1, u: '%', j: 2 },
  { id: 'ram/data/virtual-used', name: 'Virtual Memory Used', v: 15.9, d: 2, u: 'GB', j: 0.5 },
  { id: 'ram/data/virtual-available', name: 'Virtual Memory Available', v: 24.4, d: 2, u: 'GB', j: 0.5 },
  ...span(4, i => ({ id: `ram/clock/dimm-${i}`, name: `DIMM #${i}`, v: 1600, d: 0, u: 'MHz', j: 0 })),
  ...span(4, i => ({ id: `ram/temperature/dimm-${i}`, name: `DIMM #${i}`, v: 34 + i, d: 1, u: '°C', j: 1 })),
]);

dev('Samsung SSD 980 PRO 1TB', 'storage-0', [
  { id: 'storage/0/temperature/drive', name: 'Temperature', v: 41, d: 1, u: '°C', j: 1.5 },
  { id: 'storage/0/load/used-space', name: 'Used Space', v: 68.4, d: 1, u: '%', j: 0.1 },
  { id: 'storage/0/load/read-activity', name: 'Read Activity', v: 2.4, d: 1, u: '%', j: 6 },
  { id: 'storage/0/load/write-activity', name: 'Write Activity', v: 1.1, d: 1, u: '%', j: 5 },
  { id: 'storage/0/load/total-activity', name: 'Total Activity', v: 3.5, d: 1, u: '%', j: 8 },
  { id: 'storage/0/throughput/read-rate', name: 'Read Rate', v: 3.2, d: 2, u: 'MB', j: 12 },
  { id: 'storage/0/throughput/write-rate', name: 'Write Rate', v: 1.8, d: 2, u: 'MB', j: 9 },
  { id: 'storage/0/level/available-spare', name: 'Available Spare', v: 100, d: 0, u: '%', j: 0 },
  { id: 'storage/0/level/remaining-life', name: 'Remaining Life', v: 98, d: 0, u: '%', j: 0 },
  { id: 'storage/0/data/host-reads', name: 'Total Host Reads', v: 18422, d: 0, u: 'GB', j: 0 },
  { id: 'storage/0/data/host-writes', name: 'Total Host Writes', v: 21877, d: 0, u: 'GB', j: 0 },
  { id: 'storage/0/factor/temperature', name: 'Temperature Factor', v: 0.41, d: 2, u: '', j: 0 },
]);

dev('WDC WD20EZBX 2TB', 'storage-1', [
  { id: 'storage/1/temperature/drive', name: 'Temperature', v: 33, d: 1, u: '°C', j: 1 },
  { id: 'storage/1/load/used-space', name: 'Used Space', v: 51.9, d: 1, u: '%', j: 0.1 },
  { id: 'storage/1/load/read-activity', name: 'Read Activity', v: 0.4, d: 1, u: '%', j: 3 },
  { id: 'storage/1/load/write-activity', name: 'Write Activity', v: 0.2, d: 1, u: '%', j: 2 },
  { id: 'storage/1/load/total-activity', name: 'Total Activity', v: 0.6, d: 1, u: '%', j: 4 },
  { id: 'storage/1/throughput/read-rate', name: 'Read Rate', v: 0.3, d: 2, u: 'MB', j: 2 },
  { id: 'storage/1/throughput/write-rate', name: 'Write Rate', v: 0.1, d: 2, u: 'MB', j: 1 },
  { id: 'storage/1/level/remaining-life', name: 'Remaining Life', v: 91, d: 0, u: '%', j: 0 },
]);

dev('ASUS TUF GAMING B550-PLUS', 'mb', [
  ...span(6, i => ({ id: `mb/temperature/sensor-${i}`, name: `Temperature #${i}`, v: 30 + i * 1.7, d: 1, u: '°C', j: 1.2 })),
  { id: 'mb/temperature/motherboard', name: 'Motherboard', v: 35.5, d: 1, u: '°C', j: 1 },
  { id: 'mb/temperature/chipset', name: 'Chipset', v: 52.4, d: 1, u: '°C', j: 1.4 },
  { id: 'mb/temperature/vrm', name: 'VRM', v: 46.2, d: 1, u: '°C', j: 1.8 },
  ...span(6, i => ({ id: `mb/fan/fan-${i}`, name: `Fan #${i}`, v: i === 1 ? 780 : 640 + i * 55, d: 0, u: 'RPM', j: 45 })),
  ...span(6, i => ({ id: `mb/control/fan-${i}`, name: `Fan Control #${i}`, v: 35 + i * 3, d: 0, u: '%', j: 2 })),
  ...span(9, i => ({ id: `mb/voltage/rail-${i}`, name: `Voltage #${i}`, v: [3.36, 5.02, 12.1, 1.35, 1.05, 0.9, 1.2, 2.5, 1.8][i - 1], d: 3, u: 'V', j: 0.02 })),
  { id: 'mb/voltage/cpu-core', name: 'CPU Core', v: 1.184, d: 3, u: 'V', j: 0.12 },
  { id: 'mb/voltage/3v-standby', name: '3V Standby', v: 3.33, d: 3, u: 'V', j: 0.01 },
  { id: 'mb/voltage/cmos-battery', name: 'CMOS Battery', v: 3.12, d: 3, u: 'V', j: 0 },
]);

dev('Red', 'net', [
  { id: 'net/0/throughput/download', name: 'Ethernet Download Speed', v: 1.42, d: 2, u: 'MB', j: 2.4 },
  { id: 'net/0/throughput/upload', name: 'Ethernet Upload Speed', v: 0.21, d: 2, u: 'MB', j: 0.4 },
  { id: 'net/0/load/utilization', name: 'Ethernet Utilization', v: 1.4, d: 1, u: '%', j: 2 },
  { id: 'net/0/data/downloaded', name: 'Ethernet Data Downloaded', v: 92.4, d: 2, u: 'GB', j: 0 },
  { id: 'net/0/data/uploaded', name: 'Ethernet Data Uploaded', v: 11.8, d: 2, u: 'GB', j: 0 },
  { id: 'net/1/throughput/download', name: 'Wi-Fi Download Speed', v: 0, d: 2, u: 'MB', j: 0 },
  { id: 'net/1/throughput/upload', name: 'Wi-Fi Upload Speed', v: 0, d: 2, u: 'MB', j: 0 },
  { id: 'net/1/load/utilization', name: 'Wi-Fi Utilization', v: 0, d: 1, u: '%', j: 0 },
]);

dev('VibeScreen', 'panel-0', [
  { id: 'panel/0/throughput/serial', name: 'Caudal del enlace', v: 13284, d: 0, u: 'B/s', j: 700 },
  { id: 'panel/0/load/budget', name: 'Uso del presupuesto', v: 16, d: 1, u: '%', j: 1 },
  { id: 'panel/0/count/rectangles', name: 'Rectángulos por segundo', v: 36, d: 0, u: '', j: 5 },
  { id: 'panel/0/level/brightness', name: 'Brillo', v: 80, d: 0, u: '%', j: 0 },
  { id: 'panel/0/temperature/controller', name: 'Controlador', v: 38.5, d: 1, u: '°C', j: 1 },
]);

const ALL_SENSORS = DEVICES.flatMap(d => d.rows.map(r => ({ ...r, dev: d.name, devId: d.id, cur: r.v })));

function paintDevices() {
  const sel = $('#dev-pick');
  DEVICES.forEach(d => {
    const o = document.createElement('option');
    o.value = d.id; o.textContent = `${d.name} · ${d.rows.length}`;
    sel.append(o);
  });
}

function filtered() {
  const t = state.textFilter.toLowerCase();
  return ALL_SENSORS.filter(s =>
    (!state.devFilter || s.devId === state.devFilter) &&
    (!t || s.id.toLowerCase().includes(t) || s.name.toLowerCase().includes(t)));
}

function valueHTML(s) {
  const v = s.d === 0 ? group(Math.round(s.cur)) : s.cur.toFixed(s.d);
  return `${v}${s.u ? `<span class="u">${s.u}</span>` : ''}`;
}

function paintSensors() {
  const rows = filtered();
  const body = $('#sensor-body');
  body.replaceChildren();
  let lastDev = null;
  const frag = document.createDocumentFragment();
  rows.forEach(s => {
    if (s.dev !== lastDev) {
      lastDev = s.dev;
      const tr = document.createElement('tr');
      tr.className = 'group-row';
      const th = document.createElement('th');
      th.colSpan = 3; th.scope = 'colgroup'; th.textContent = s.dev;
      tr.append(th); frag.append(tr);
    }
    const tr = document.createElement('tr');
    tr.className = 'srow' + (state.sensorPick === s.id ? ' is-sel' : '');
    tr.tabIndex = 0;
    tr.dataset.sid = s.id;
    tr.setAttribute('aria-selected', String(state.sensorPick === s.id));
    tr.innerHTML = `<td class="sid"></td><td class="sname"></td><td class="sval">${valueHTML(s)}</td>`;
    tr.children[0].textContent = s.id;
    tr.children[1].textContent = s.name;
    frag.append(tr);
  });
  body.append(frag);
  $('#sensor-count').textContent = group(rows.length);
  $('#sensor-empty').hidden = rows.length > 0;
  $('#sensor-table').hidden = rows.length === 0;
  paintPick();
}

function paintPick() {
  const has = !!state.sensorPick;
  $('#sensor-pick').textContent = has ? state.sensorPick : 'ninguno elegido';
  $('#sensor-pick').className = has ? 'mono' : '';
  $('#sensor-copy').disabled = !has;
  $('#sensor-use').disabled = !has;
}

function pickSensor(id) {
  state.sensorPick = id;
  $$('#sensor-body .srow').forEach(r => {
    const on = r.dataset.sid === id;
    r.classList.toggle('is-sel', on);
    r.setAttribute('aria-selected', String(on));
  });
  paintPick();
}

$('#sensor-body').addEventListener('click', e => {
  const r = e.target.closest('.srow');
  if (r) pickSensor(r.dataset.sid);
});
$('#sensor-body').addEventListener('keydown', e => {
  const r = e.target.closest('.srow');
  if (!r) return;
  if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); pickSensor(r.dataset.sid); }
  if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
    e.preventDefault();
    const rows = $$('#sensor-body .srow');
    const i = rows.indexOf(r);
    const next = rows[clamp(i + (e.key === 'ArrowDown' ? 1 : -1), 0, rows.length - 1)];
    if (next) { next.focus(); pickSensor(next.dataset.sid); }
  }
});
$('#dev-pick').addEventListener('change', e => { state.devFilter = e.target.value; paintSensors(); });
$('#sensor-filter').addEventListener('input', e => { state.textFilter = e.target.value.trim(); paintSensors(); });
$('#sensor-clear').addEventListener('click', () => {
  state.devFilter = ''; state.textFilter = '';
  $('#dev-pick').value = ''; $('#sensor-filter').value = '';
  paintSensors();
});

async function copiar(text, msg) {
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    const ta = document.createElement('textarea');
    ta.value = text; ta.style.position = 'fixed'; ta.style.opacity = '0';
    document.body.append(ta); ta.select();
    try { document.execCommand('copy'); } catch {}
    ta.remove();
  }
  toast(msg, 'i-copy');
}
$('#sensor-copy').addEventListener('click', () => copiar(state.sensorPick, 'Identificador copiado'));
$('#sensor-use').addEventListener('click', () => {
  const s = ALL_SENSORS.find(x => x.id === state.sensorPick);
  setView('editor');
  const w = addWidget('Texto', state.sensorPick);
  if (s) { w.unit = s.u || '°C'; w.decimals = Math.min(s.d, 3); w.value = s.cur; select(w.id); }
  toast(`${state.sensorPick} añadido al tema`, 'i-send');
});

/* ── 11. Ajustes ───────────────────────────────────────────── */
/* tema de la ventana: el panel no se entera, su tema es del servicio */
const prefersLight = window.matchMedia('(prefers-color-scheme: light)');
const effectiveTheme = () => state.theme === 'system' ? (prefersLight.matches ? 'light' : 'dark') : state.theme;
const accentHex = a => effectiveTheme() === 'light' ? a.light : a.hex;

function applyTheme(mode) {
  state.theme = mode;
  const eff = effectiveTheme();
  document.documentElement.dataset.theme = eff;
  $$('#theme-seg .seg-btn').forEach(b => {
    const on = b.dataset.theme === mode;
    b.classList.toggle('is-on', on);
    b.setAttribute('aria-checked', String(on));
  });
  const quick = $('#theme-quick');
  const toLight = eff !== 'light';
  quick.setAttribute('aria-label', toLight ? 'Cambiar al modo claro' : 'Cambiar al modo oscuro');
  $('#theme-quick use').setAttribute('href', toLight ? '#i-sun' : '#i-moon');
  setAccent(state.accent);
}
prefersLight.addEventListener('change', () => { if (state.theme === 'system') applyTheme('system'); });

$$('#theme-seg .seg-btn').forEach(b => b.addEventListener('click', () => {
  applyTheme(b.dataset.theme);
  const dicho = { dark: 'Modo oscuro', light: 'Modo claro', system: `El del sistema: ${effectiveTheme() === 'light' ? 'claro' : 'oscuro'}` }[b.dataset.theme];
  toast(dicho, effectiveTheme() === 'light' ? 'i-sun' : 'i-moon');
}));
$('#theme-quick').addEventListener('click', () => {
  const next = effectiveTheme() === 'light' ? 'dark' : 'light';
  applyTheme(next);
  toast(next === 'light' ? 'Modo claro' : 'Modo oscuro', next === 'light' ? 'i-sun' : 'i-moon');
});

function setAccent(id) {
  const a = ACCENTS.find(x => x.id === id) || ACCENTS[0];
  state.accent = a.id;
  document.documentElement.style.setProperty('--accent', accentHex(a));
  $$('#swatches .swatch').forEach(b => {
    const s = ACCENTS.find(x => x.id === b.dataset.accent);
    if (s) { b.style.background = accentHex(s); b.style.color = accentHex(s); }
    const on = b.dataset.accent === a.id;
    b.setAttribute('aria-checked', String(on));
    b.tabIndex = on ? 0 : -1;
  });
}
function paintSwatches() {
  const box = $('#swatches');
  ACCENTS.forEach(a => {
    const b = document.createElement('button');
    b.type = 'button'; b.className = 'swatch'; b.dataset.accent = a.id;
    b.setAttribute('role', 'radio'); b.setAttribute('aria-checked', String(a.id === state.accent));
    b.setAttribute('aria-label', a.name);
    b.style.background = accentHex(a); b.style.color = accentHex(a);
    b.tabIndex = a.id === state.accent ? 0 : -1;
    b.innerHTML = '<svg class="ico" aria-hidden="true" style="color:var(--on-accent)"><use href="#i-check"/></svg>';
    b.addEventListener('click', () => { setAccent(a.id); toast(`Realce: ${a.name.toLowerCase()}`, 'i-check'); });
    box.append(b);
  });
  box.addEventListener('keydown', e => {
    if (!['ArrowRight', 'ArrowLeft', 'ArrowUp', 'ArrowDown'].includes(e.key)) return;
    e.preventDefault();
    const i = ACCENTS.findIndex(a => a.id === state.accent);
    const d = (e.key === 'ArrowRight' || e.key === 'ArrowDown') ? 1 : -1;
    const next = ACCENTS[(i + d + ACCENTS.length) % ACCENTS.length];
    setAccent(next.id);
    $(`#swatches .swatch[data-accent="${next.id}"]`).focus();
  });
}
$('#font-pick').addEventListener('change', e => {
  document.documentElement.style.setProperty('--font-data', e.target.value);
  toast('Letra cambiada', 'i-check');
});
$('#render-apply').addEventListener('click', async () => {
  const modo = $('#render-pick').value;
  const ok = await confirmar({
    title: 'Reiniciar la ventana',
    text: `La ventana se cerrará y volverá a abrirse con ${modo}. El panel sigue pintando mientras tanto.`,
    ok: 'Reiniciar',
    danger: false,
  });
  if (ok) { $('#render-next').textContent = modo; toast(`Tras reiniciar: ${modo}`, 'i-restore'); }
});
$('#cfg-copy').addEventListener('click', () => copiar($('#cfg-path').textContent, 'Ruta copiada'));
$('#factory').addEventListener('click', async () => {
  const ok = await confirmar({
    title: '¿Volver a los ajustes de fábrica?',
    text: 'Se pierden el tema de la ventana, el color de realce, la letra y el modo de render. Los temas del panel y su configuración no se tocan.',
    ok: 'Volver a fábrica',
  });
  if (ok) {
    applyTheme('dark');
    setAccent('rojo');
    $('#font-pick').selectedIndex = 0;
    document.documentElement.style.setProperty('--font-data', $('#font-pick').value);
    $('#render-pick').value = 'Software'; $('#render-next').textContent = 'Software';
    toast('Ajustes de fábrica restaurados', 'i-restore');
  }
});

/* ── 12. Botones de la ventana ─────────────────────────────── */
$$('[data-window-action]').forEach(b => b.addEventListener('click', () => {
  const msg = {
    min: 'La ventana se minimizaría',
    max: 'La ventana se maximizaría',
    close: 'La ventana se va a la bandeja: el servicio sigue pintando',
  }[b.dataset.windowAction];
  toast(msg, 'i-panel');
}));

/* ── 13. Latido: valores que se refrescan ──────────────────── */
function jitter(s) {
  if (!s.j) return s.cur;
  const next = s.v + (Math.random() - 0.5) * s.j * 2;
  return Math.max(0, next);
}
function tickSensors() {
  if (state.view !== 'sensores') return;         // la ventana escondida no trabaja
  const rows = $$('#sensor-body .srow');
  const n = Math.min(rows.length, 26);
  for (let k = 0; k < n; k++) {
    const r = rows[Math.floor(Math.random() * rows.length)];
    const s = ALL_SENSORS.find(x => x.id === r.dataset.sid);
    if (!s || !s.j) continue;
    s.cur = jitter(s);
    const cell = r.querySelector('.sval');
    cell.innerHTML = valueHTML(s);
    cell.classList.remove('is-tick');
    void cell.offsetWidth;
    cell.classList.add('is-tick');
  }
}
function tickLink() {
  if (!state.screenOn) return;
  state.rate = Math.round(13284 + (Math.random() - 0.5) * 1400);
  state.rects += Math.round(30 + Math.random() * 14);
  state.sent = +(state.sent + state.rate / 1024 / 1024 * 2).toFixed(2);
  state.history.push(state.rate);
  if (state.history.length > 40) state.history.shift();
  $('#rate-out').textContent = group(state.rate);
  $('#status-rate').textContent = group(state.rate);
  $('#rects-out').textContent = group(state.rects);
  $('#sent-out').textContent = `${state.sent.toFixed(1)} MB`;
  const pct = Math.round(state.rate / BUDGET * 100);
  $('#budget-out').textContent = `${pct} %`;
  $('#budget-bar').style.width = `${pct}%`;
  $('.meter .bar').setAttribute('aria-label', `${pct} % del presupuesto de 156.960 B/s`);
  paintSpark();
}
function tickPanel() {
  if (state.view !== 'panel' || !state.screenOn) return;
  // los valores que pinta el panel se mueven como en la máquina real
  state.applied.widgets.forEach(w => {
    if (w.kind === 'Texto' && w.sensor) {
      const s = ALL_SENSORS.find(x => x.id === w.sensor);
      if (s) { s.cur = jitter(s); w.value = s.u === 'MHz' && w.unit === 'GHz' ? s.cur / 1000 : s.cur; }
    } else if ((w.kind === 'Barra' || w.kind === 'Arco') && w.sensor) {
      const s = ALL_SENSORS.find(x => x.id === w.sensor);
      if (s) { s.cur = jitter(s); w.value = s.u === 'MHz' && w.max <= 10 ? s.cur / 1000 : s.cur; }
    }
  });
  drawTheme($('#panel-live'), state.applied);
  drawTheme($('#theme-thumb'), state.applied);
}

/* ── 14. Arranque ──────────────────────────────────────────── */
for (let i = 0; i < 40; i++) state.history.push(Math.round(13284 + (Math.random() - 0.5) * 1800));

paintDevices();
paintSensors();
paintSwatches();
applyTheme(state.theme);
setAccent('rojo');
paintBright();
paintAll();
paintWidgetList();
paintProps();
paintCost();
paintSpark();
$('#editor-render').style.setProperty('--zoom', state.zoom);

setInterval(tickLink, 2200);
setInterval(tickSensors, 1200);
setInterval(tickPanel, 1000);
setInterval(() => { state.uptime += 1; $('#uptime').textContent = `${state.uptime} min`; $('#uptime-2').textContent = `${state.uptime} min`; }, 60000);

})();
