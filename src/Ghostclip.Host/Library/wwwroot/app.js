'use strict';

// The clip library page. It talks to the app with JSON messages (see
// LibraryMessages.cs): it asks with {type, path, start, end} and listens for
// clips, meta, open, done, error and changed.

const $ = id => document.getElementById(id);
const MIN_RANGE = 0.5;

const state = {
  clips: [],
  meta: new Map(),       // path -> { meta, thumb }
  requested: new Set(),  // paths whose meta was asked for
  versions: new Map(),   // path -> version: a replaced file (same name, new trim) must not keep the old details
  game: null,            // null = all clips
  query: '',
  pendingOpen: null,
  current: null,         // clip shown in the clip view
  start: 0,
  end: 0,
};

const host = window.chrome && window.chrome.webview;
function post(message) {
  if (host) host.postMessage(JSON.stringify(message));
}

// Formatting

function pad(n) { return String(n).padStart(2, '0'); }

function fmtTime(seconds) {
  const s = Math.max(0, Math.round(seconds));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  return h > 0 ? `${h}:${pad(m)}:${pad(s % 60)}` : `${pad(m)}:${pad(s % 60)}`;
}

function fmtSize(bytes) {
  const mb = bytes / 1024 / 1024;
  return mb >= 1024 ? `${(mb / 1024).toFixed(1)} GB` : `${mb >= 100 ? Math.round(mb) : mb.toFixed(1)} MB`;
}

function savedDate(clip) { return new Date(clip.saved); }

function dayLabel(date) {
  const today = new Date();
  const start = d => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
  const days = Math.round((start(today) - start(date)) / 86400000);
  if (days === 0) return 'TODAY';
  if (days === 1) return 'YESTERDAY';
  const options = { month: 'short', day: 'numeric' };
  if (date.getFullYear() !== today.getFullYear()) options.year = 'numeric';
  return date.toLocaleDateString('en-US', options).toUpperCase();
}

function clockLabel(date) { return `${pad(date.getHours())}:${pad(date.getMinutes())}`; }

function fileName(path) { return path.slice(path.lastIndexOf('\\') + 1); }

function codecLabel(codec) {
  return { h264: 'H.264', hevc: 'HEVC' }[codec] || (codec || '').toUpperCase();
}

function tracksLabel(count) {
  return count >= 2 ? 'GAME + MIC' : count === 1 ? 'GAME' : 'NO AUDIO';
}

function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

function icon(paths) {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('viewBox', '0 0 24 24');
  svg.setAttribute('aria-hidden', 'true');
  svg.innerHTML = paths;
  return svg;
}

const ICON_COPY = '<rect x="8" y="8" width="12" height="12" rx="2"/><path d="M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2"/>';
const ICON_FOLDER = '<path d="M4 6a2 2 0 0 1 2-2h4l2 2h6a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2z"/>';

// Same rule as ClipMedia.SnapStart: a stream-copy trim starts at the keyframe at or before the asked time.
function snapStart(keyframes, seconds) {
  let snapped = 0;
  for (const k of keyframes) {
    if (k > seconds) break;
    snapped = k;
  }
  return snapped;
}

// Library view

function visibleClips() {
  const q = state.query.trim().toLowerCase();
  return state.clips.filter(c =>
    (state.game === null || c.game === state.game) &&
    (q === '' || c.game.toLowerCase().includes(q) || fileName(c.path).toLowerCase().includes(q)));
}

function renderSidebar() {
  const counts = new Map();
  for (const c of state.clips) counts.set(c.game, (counts.get(c.game) || 0) + 1);
  if (state.game !== null && !counts.has(state.game)) state.game = null;
  const games = [...counts].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]));

  const nav = $('games');
  nav.replaceChildren();
  const row = (label, count, game) => {
    const b = el('button', 'game' + (state.game === game ? ' on' : ''));
    b.type = 'button';
    b.append(el('span', 'name', label), el('span', 'mono', String(count)));
    b.addEventListener('click', () => {
      state.game = game;
      if (state.current) closeClip();
      showView('library');
      renderSidebar();
      renderGrid();
    });
    nav.append(b);
  };
  row('All clips', state.clips.length, null);
  for (const [game, count] of games) row(game, count, game);

  const bytes = state.clips.reduce((sum, c) => sum + c.bytes, 0);
  $('total').textContent = state.clips.length ? fmtSize(bytes) : '';
}

const observer = new IntersectionObserver(entries => {
  for (const entry of entries) {
    if (!entry.isIntersecting) continue;
    observer.unobserve(entry.target);
    requestMeta(entry.target.dataset.path);
  }
}, { rootMargin: '200px' });

function requestMeta(path) {
  if (state.requested.has(path)) return;
  state.requested.add(path);
  post({ type: 'meta', path });
}

function renderCard(clip) {
  const card = el('div', 'card');
  card.tabIndex = 0;
  card.dataset.path = clip.path;
  const thumb = el('div', 'thumb');
  card.append(thumb);
  if (clip.derived) {
    const name = fileName(clip.path);
    thumb.append(el('span', 'tag', /_discord(_\d+)?\.mp4$/i.test(name) ? 'DISCORD' : 'TRIM'));
  }
  const quick = el('div', 'quick');
  const quickButton = (paths, label, type) => {
    const b = el('button');
    b.type = 'button';
    b.setAttribute('aria-label', label);
    b.append(icon(paths));
    b.addEventListener('click', e => { e.stopPropagation(); post({ type, path: clip.path }); });
    quick.append(b);
  };
  quickButton(ICON_COPY, 'Copy file', 'copy');
  quickButton(ICON_FOLDER, 'Show in folder', 'reveal');
  thumb.append(quick);

  const text = el('div', 'card-text');
  const date = savedDate(clip);
  text.append(el('div', 'card-title', state.game === null ? clip.game : fileName(clip.path).replace(/\.mp4$/i, '')),
    el('div', 'card-sub', `${clockLabel(date)} · ${fmtSize(clip.bytes)}`));
  card.append(text);

  card.addEventListener('click', () => openClip(clip.path));
  card.addEventListener('keydown', e => { if (e.key === 'Enter') openClip(clip.path); });
  applyMeta(card, clip.path);
  if (!state.meta.has(clip.path)) observer.observe(card);
  return card;
}

function applyMeta(card, path) {
  const entry = state.meta.get(path);
  if (!entry) return;
  const thumb = card.querySelector('.thumb');
  if (entry.thumb && !thumb.style.backgroundImage) {
    thumb.style.backgroundImage = `url("${entry.thumb}")`;
    thumb.classList.add('loaded');
  }
  if (entry.meta && !thumb.querySelector('.chip')) thumb.append(el('span', 'chip', fmtTime(entry.meta.duration)));
}

function renderGrid() {
  const grid = $('grid');
  grid.replaceChildren();
  const clips = visibleClips();
  $('empty').hidden = state.clips.length > 0;
  $('no-match').hidden = state.clips.length === 0 || clips.length > 0;
  $('count').textContent = state.clips.length ? `${clips.length} ${clips.length === 1 ? 'CLIP' : 'CLIPS'}` : '';

  let label = null;
  let cards = null;
  for (const clip of clips) {
    const day = dayLabel(savedDate(clip));
    if (day !== label) {
      label = day;
      grid.append(el('div', 'day', day));
      cards = el('div', 'cards');
      grid.append(cards);
    }
    cards.append(renderCard(clip));
  }
}

// Clip view

const video = $('video');
const canvas = $('wave');
let dragging = null;
let frame = 0;

function showView(name) {
  $('library').hidden = name !== 'library';
  $('clip').hidden = name !== 'clip';
  $('settings').hidden = name !== 'settings';
  // The sidebar marks where you are: a game, or Settings.
  document.body.classList.toggle('settings-open', name === 'settings');
  $('nav-settings').classList.toggle('on', name === 'settings');
}

function openClip(path) {
  const clip = state.clips.find(c => c.path.toLowerCase() === path.toLowerCase());
  if (!clip) {
    toast('That clip is no longer in your clips folder.', true);
    return;
  }
  state.current = clip;
  $('confirm').hidden = true;
  $('clip-game').textContent = clip.game;
  $('no-video').hidden = true;
  // The version keeps WebView2 from serving a cached copy of a file that was replaced under the same name.
  video.src = `${clip.url}?v=${encodeURIComponent(clip.version)}`;
  state.start = 0;
  state.end = 0;
  showView('clip');
  requestMeta(clip.path);
  setupTimeline();
}

function closeClip() {
  releaseVideo();
  state.current = null;
  showView('library');
}

// The video element holds the file open; let go before deleting or leaving.
function releaseVideo() {
  video.pause();
  video.removeAttribute('src');
  video.load();
}

function currentMeta() {
  const entry = state.current && state.meta.get(state.current.path);
  return entry && entry.meta;
}

function setupTimeline() {
  const meta = currentMeta();
  const clip = state.current;
  if (!clip) return;
  if (!meta) {
    $('clip-info').textContent = fmtSize(clip.bytes);
    $('t-range').textContent = '';
    $('t-end').textContent = '';
    drawWave();
    return;
  }
  if (state.end === 0) {
    state.start = 0;
    state.end = meta.duration;
  }
  $('clip-info').textContent = [`${meta.height}P${Math.round(meta.fps)}`, codecLabel(meta.videoCodec),
    tracksLabel(meta.audioTracks), fmtSize(clip.bytes)].join(' · ');
  $('t-end').textContent = fmtTime(meta.duration);
  layoutRange();
}

function layoutRange() {
  const meta = currentMeta();
  if (!meta) return;
  const pct = t => `${(t / meta.duration) * 100}%`;
  $('range').style.left = pct(state.start);
  $('range').style.width = `${((state.end - state.start) / meta.duration) * 100}%`;
  $('h-start').style.left = pct(state.start);
  $('h-end').style.left = pct(state.end);
  const length = state.end - state.start;
  const hint = dragging === 'start' ? ' · STARTS ON A KEYFRAME' : '';
  $('t-range').textContent = `${fmtTime(state.start)} → ${fmtTime(state.end)} · ${Math.round(length)} S${hint}`;
  drawWave();
  placePlayhead();
}

function drawWave() {
  const ratio = window.devicePixelRatio || 1;
  const width = Math.max(1, Math.round(canvas.clientWidth * ratio));
  const height = Math.max(1, Math.round(canvas.clientHeight * ratio));
  if (canvas.width !== width || canvas.height !== height) {
    canvas.width = width;
    canvas.height = height;
  }
  const ctx = canvas.getContext('2d');
  ctx.clearRect(0, 0, width, height);
  const meta = currentMeta();
  if (!meta || !meta.peaks || meta.peaks.length === 0) return;

  const peaks = meta.peaks;
  const gap = Math.max(1, Math.round(ratio));
  const bars = Math.min(peaks.length, Math.floor(width / (2 * gap)));
  const barWidth = width / bars - gap;
  const middle = height / 2;
  for (let i = 0; i < bars; i++) {
    const from = Math.floor((i * peaks.length) / bars);
    const to = Math.max(from + 1, Math.floor(((i + 1) * peaks.length) / bars));
    let peak = 0;
    for (let j = from; j < to; j++) peak = Math.max(peak, peaks[j]);
    const t = ((i + 0.5) / bars) * meta.duration;
    const inside = t >= state.start && t <= state.end;
    ctx.fillStyle = inside ? 'rgba(92, 225, 230, 0.85)' : 'rgba(232, 234, 237, 0.22)';
    const h = Math.max(gap, peak * (height - 8 * ratio));
    ctx.fillRect(i * (barWidth + gap), middle - h / 2, Math.max(1, barWidth), h);
  }
}

function placePlayhead() {
  const meta = currentMeta();
  if (!meta) return;
  $('playhead').style.left = `${(video.currentTime / meta.duration) * 100}%`;
}

// Only runs while the video plays: an idle window draws nothing.
function tick() {
  if (state.current && video.currentTime >= state.end && state.end > 0) {
    video.currentTime = state.start;
  }
  placePlayhead();
  frame = requestAnimationFrame(tick);
}

function togglePlay() {
  if (!state.current) return;
  if (video.paused) {
    if (video.currentTime < state.start || video.currentTime >= state.end) video.currentTime = state.start;
    video.play().catch(() => toast("This clip can't be played here.", true));
  } else {
    video.pause();
  }
}

video.addEventListener('play', () => {
  document.body.classList.add('playing');
  cancelAnimationFrame(frame);
  frame = requestAnimationFrame(tick);
});
video.addEventListener('pause', () => {
  document.body.classList.remove('playing');
  cancelAnimationFrame(frame);
  placePlayhead();
});
video.addEventListener('seeked', placePlayhead);
// Without the HEVC extension the audio plays but no picture is decoded; say so instead of a black frame.
video.addEventListener('loadedmetadata', () => { $('no-video').hidden = video.videoWidth > 0; });
document.querySelector('.stage').addEventListener('click', togglePlay);

function timeAt(clientX) {
  const meta = currentMeta();
  const box = $('timeline').getBoundingClientRect();
  const x = Math.min(Math.max(clientX - box.left, 0), box.width);
  return (x / box.width) * meta.duration;
}

function moveHandle(which, t) {
  const meta = currentMeta();
  if (which === 'start') {
    state.start = snapStart(meta.keyframes, Math.min(t, state.end - MIN_RANGE));
  } else {
    state.end = Math.min(meta.duration, Math.max(t, state.start + MIN_RANGE));
  }
  layoutRange();
}

for (const [id, which] of [['h-start', 'start'], ['h-end', 'end']]) {
  const handle = $(id);
  handle.addEventListener('pointerdown', e => {
    if (!currentMeta()) return;
    e.stopPropagation();
    dragging = which;
    handle.setPointerCapture(e.pointerId);
    layoutRange();
  });
  handle.addEventListener('pointermove', e => {
    if (dragging === which) moveHandle(which, timeAt(e.clientX));
  });
  handle.addEventListener('pointerup', () => {
    dragging = null;
    layoutRange();
    video.currentTime = which === 'start' ? state.start : Math.max(state.start, state.end - 2);
  });
  handle.addEventListener('keydown', e => {
    const meta = currentMeta();
    if (!meta || (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight')) return;
    e.preventDefault();
    const step = e.key === 'ArrowLeft' ? -1 : 1;
    if (which === 'start') {
      // Keyframe to keyframe.
      const next = step > 0
        ? meta.keyframes.find(k => k > state.start + 0.01)
        : [...meta.keyframes].reverse().find(k => k < state.start - 0.01);
      if (next !== undefined) moveHandle('start', next);
    } else {
      moveHandle('end', state.end + step);
    }
  });
}

$('timeline').addEventListener('pointerdown', e => {
  if (!currentMeta() || e.target.classList.contains('handle')) return;
  video.currentTime = timeAt(e.clientX);
  placePlayhead();
});

window.addEventListener('resize', () => { if (state.current) drawWave(); });

// Actions

function busy(button, on) { button.classList.toggle('busy', on); }

$('back').addEventListener('click', closeClip);
$('a-trim').addEventListener('click', () => {
  if (!state.current || !currentMeta()) return;
  busy($('a-trim'), true);
  toast('Saving trim…');
  post({ type: 'trim', path: state.current.path, start: state.start, end: state.end });
});
$('a-discord').addEventListener('click', () => {
  if (!state.current) return;
  busy($('a-discord'), true);
  toast('Compressing on the GPU…');
  post({ type: 'compress', path: state.current.path });
});
$('a-copy').addEventListener('click', () => { if (state.current) post({ type: 'copy', path: state.current.path }); });
$('a-folder').addEventListener('click', () => { if (state.current) post({ type: 'reveal', path: state.current.path }); });
$('a-delete').addEventListener('click', () => { $('confirm').hidden = false; });
$('c-no').addEventListener('click', () => { $('confirm').hidden = true; });
$('c-yes').addEventListener('click', () => {
  if (!state.current) return;
  const path = state.current.path;
  $('confirm').hidden = true;
  releaseVideo();
  post({ type: 'delete', path });
});
$('open-folder').addEventListener('click', () => post({ type: 'openFolder' }));
$('empty-folder').addEventListener('click', () => post({ type: 'openFolder' }));
$('search').addEventListener('input', e => { state.query = e.target.value; renderGrid(); });

document.addEventListener('keydown', e => {
  if (e.target instanceof HTMLInputElement) return;
  if (e.key === 'Escape' && state.current) closeClip();
  else if (e.key === 'Escape' && Settings.isOpen()) showView('library');
  if (e.key === ' ' && state.current && !(e.target instanceof HTMLButtonElement)) {
    e.preventDefault();
    togglePlay();
  }
});

// Toast

let toastTimer = 0;
function toast(text, error = false, show = null) {
  const box = $('toast');
  $('toast-text').textContent = text;
  box.classList.toggle('error', error);
  const button = $('toast-show');
  button.hidden = !show;
  button.onclick = show ? () => { box.hidden = true; show(); } : null;
  box.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { box.hidden = true; }, error ? 7000 : 4500);
}

// Messages from the app

function onDone(message) {
  const open = message.output ? () => openWhenListed(message.output) : null;
  switch (message.op) {
    case 'trim':
      busy($('a-trim'), false);
      toast('Trim saved', false, open);
      break;
    case 'compress':
      busy($('a-discord'), false);
      toast(`Compressed to ${message.message}`, false, open);
      break;
    case 'copy':
      toast('Copied. Paste it in Discord.');
      break;
    case 'delete':
      toast('Moved to the Recycle Bin');
      if (state.current && state.current.path === message.path) {
        state.current = null;
        showView('library');
      }
      break;
  }
}

function openWhenListed(path) {
  if (state.clips.some(c => c.path.toLowerCase() === path.toLowerCase())) openClip(path);
  else {
    state.pendingOpen = path;
    post({ type: 'list' });
  }
}

function onMessage(message) {
  if (Settings.onMessage(message)) return;
  switch (message.type) {
    case 'clips': {
      state.clips = message.clips || [];
      const listed = new Map(state.clips.map(c => [c.path, c.version]));
      let reopen = null;
      for (const [path, version] of state.versions) {
        if (listed.get(path) === version) continue;
        state.meta.delete(path);
        state.requested.delete(path);
        if (state.current && state.current.path === path && listed.has(path)) reopen = path;
      }
      state.versions = listed;
      $('total').title = message.root || '';
      renderSidebar();
      renderGrid();
      if (state.current && !state.clips.some(c => c.path === state.current.path)) closeClip();
      if (state.pendingOpen) {
        const path = state.pendingOpen;
        state.pendingOpen = null;
        if (state.clips.some(c => c.path.toLowerCase() === path.toLowerCase())) openClip(path);
      } else if (reopen) {
        openClip(reopen);
      }
      break;
    }
    case 'meta': {
      state.meta.set(message.path, { meta: message.meta, thumb: message.thumb });
      const card = [...document.querySelectorAll('.card')].find(c => c.dataset.path === message.path);
      if (card) applyMeta(card, message.path);
      if (state.current && state.current.path === message.path) setupTimeline();
      break;
    }
    case 'open':
      openWhenListed(message.path);
      break;
    case 'done':
      onDone(message);
      break;
    case 'error':
      if (message.op === 'trim') busy($('a-trim'), false);
      if (message.op === 'compress') busy($('a-discord'), false);
      // A clip whose details can't be read keeps its plain tile and is asked for again when the list next
      // changes (a clip still being copied in, say); only say so when it's open.
      if (message.op === 'meta') state.requested.delete(message.path);
      if (message.op === 'meta' && !(state.current && state.current.path === message.path)) break;
      toast(message.message || 'Something went wrong.', true);
      break;
    case 'changed':
      post({ type: 'list' });
      break;
  }
}

function start() {
  if (!host) return;
  host.addEventListener('message', e => onMessage(JSON.parse(e.data)));
  post({ type: 'list' });
}
