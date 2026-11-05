'use strict';

// The clip library page. It talks to the app with JSON messages (see
// LibraryMessages.cs): it asks with {type, path, start, end} and listens for
// clips, meta, open, done, error, changed and language.

const $ = id => document.getElementById(id);
const MIN_RANGE = 0.5;

// "en", "pt" or "es": the window opens the page with ?lang=, and a "language" message changes it live.
// Texts built here carry every translation where they are used: L('Settings', 'Configurações', 'Configuración').
function knownLang(code) { return code === 'pt' || code === 'es' ? code : 'en'; }
let lang = knownLang(new URLSearchParams(location.search).get('lang'));

// A soft light follows the pointer (app.css, "Light"): --mx/--my over the window, --lx/--ly inside
// the card, button or sidebar item it's over. One write per frame, however fast the pointer moves.
const LIT = '.card, .card-s, .act, .game, .update-card';
let pointer = null;
document.addEventListener('pointermove', e => {
  const first = pointer === null;
  pointer = e;
  if (first) requestAnimationFrame(() => {
    const p = pointer;
    pointer = null;
    const root = document.documentElement.style;
    root.setProperty('--mx', `${p.clientX}px`);
    root.setProperty('--my', `${p.clientY}px`);
    const lit = p.target instanceof Element && p.target.closest(LIT);
    if (lit) {
      const r = lit.getBoundingClientRect();
      lit.style.setProperty('--lx', `${p.clientX - r.left}px`);
      lit.style.setProperty('--ly', `${p.clientY - r.top}px`);
    }
  });
}, { passive: true });
// Leaving the window takes the light with it.
document.documentElement.addEventListener('pointerleave', () => {
  document.documentElement.style.setProperty('--mx', '-1000px');
  document.documentElement.style.setProperty('--my', '-1000px');
});
function L(en, pt, es) { return lang === 'pt' ? pt : lang === 'es' ? es : en; }

// The page's fixed texts: English is what the HTML carries, the others sit next to it in data-pt and data-es,
// data-pt-label (aria-label), data-pt-placeholder and data-pt-title, and the same for es. Never on an element code rewrites.
function translatePage() {
  document.documentElement.lang = L('en', 'pt-BR', 'es');
  for (const node of document.querySelectorAll('[data-pt]')) {
    if (node.dataset.en === undefined) node.dataset.en = node.textContent;
    node.textContent = L(node.dataset.en, node.dataset.pt, node.dataset.es);
  }
  for (const [name, attribute] of [['Label', 'aria-label'], ['Placeholder', 'placeholder'], ['Title', 'title']]) {
    for (const node of document.querySelectorAll(`[data-pt-${name.toLowerCase()}]`)) {
      if (node.dataset['en' + name] === undefined) node.dataset['en' + name] = node.getAttribute(attribute);
      node.setAttribute(attribute, L(node.dataset['en' + name], node.dataset['pt' + name], node.dataset['es' + name]));
    }
  }
}

function setLanguage(next) {
  lang = knownLang(next);
  translatePage();
  // Texts built by code are built again; the open clip and the settings draft stay as they are.
  if (state.listed) {
    renderSidebar();
    renderGrid();
  }
  if (state.current) setupTimeline();
  Settings.translate();
}

const state = {
  clips: [],
  listed: false,         // the first list arrived
  meta: new Map(),       // path -> { meta, thumb }
  requested: new Set(),  // paths whose meta was asked for
  versions: new Map(),   // path -> version: a replaced file (same name, new trim) must not keep the old details
  game: null,            // null = all clips
  favorites: false,      // only favorites, across games
  saveKey: 'Alt+F10',    // the clip hotkey as this keyboard prints it, for the empty library
  query: '',
  sort: 'newest',        // set from what was last chosen, below
  kind: 'all',
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
  if (days === 0) return L('TODAY', 'HOJE', 'HOY');
  if (days === 1) return L('YESTERDAY', 'ONTEM', 'AYER');
  const options = { month: 'short', day: 'numeric' };
  if (date.getFullYear() !== today.getFullYear()) options.year = 'numeric';
  return date.toLocaleDateString(L('en-US', 'pt-BR', 'es-419'), options).toUpperCase();
}

function clockLabel(date) { return `${pad(date.getHours())}:${pad(date.getMinutes())}`; }

function fileName(path) { return path.slice(path.lastIndexOf('\\') + 1); }

function stem(path) { return fileName(path).replace(/\.(mp4|jpg|png)$/i, ''); }

function extension(path) { const m = /\.[^.\\]+$/.exec(path); return m ? m[0] : ''; }

function codecLabel(codec) {
  return { h264: 'H.264', hevc: 'HEVC' }[codec] || (codec || '').toUpperCase();
}

// Track names as saving wrote them; clips cached before names were read only know how many there are.
function tracksLabel(meta) {
  const names = { Game: L('GAME', 'JOGO', 'JUEGO'), Microphone: 'MIC', Discord: 'DISCORD' };
  if (meta.audioTitles && meta.audioTitles.length) return meta.audioTitles.map(t => names[t] || t.toUpperCase()).join(' + ');
  const count = meta.audioTracks;
  return count >= 2 ? L('GAME + MIC', 'JOGO + MIC', 'JUEGO + MIC') : count === 1 ? L('GAME', 'JOGO', 'JUEGO') : L('NO AUDIO', 'SEM ÁUDIO', 'SIN AUDIO');
}

// One name per track, for choosing among them. A clip with unnamed tracks only says how many it has.
function trackNames(meta) {
  const names = { Game: L('GAME', 'JOGO', 'JUEGO'), Microphone: 'MIC', Discord: 'DISCORD' };
  if (meta.audioTitles && meta.audioTitles.length) return meta.audioTitles.map(t => names[t] || t.toUpperCase());
  return Array.from({ length: meta.audioTracks || 0 }, (_, i) => L(`TRACK ${i + 1}`, `FAIXA ${i + 1}`, `PISTA ${i + 1}`));
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
const ICON_STAR = '<path d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2L12 17.3 6.4 20.2l1.1-6.2L3 9.6l6.2-.9z"/>';

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
  const clips = state.clips.filter(c =>
    (state.game === null || c.game === state.game) && (!state.favorites || c.favorite) &&
    (state.kind === 'all' || (state.kind === 'images') === !!c.image) &&
    (q === '' || c.game.toLowerCase().includes(q) || fileName(c.path).toLowerCase().includes(q)));
  return sortClips(clips);
}

// The list comes newest first. A sort keeps that order among equals, and a clip whose length isn't known yet goes last.
function sortClips(clips) {
  const length = c => (state.meta.get(c.path)?.meta?.duration) ?? -1;
  switch (state.sort) {
    case 'oldest': return clips.sort((a, b) => savedDate(a) - savedDate(b));
    case 'largest': return clips.sort((a, b) => b.bytes - a.bytes);
    case 'longest': return clips.sort((a, b) => length(b) - length(a));
    default: return clips;
  }
}

function readChoice(key, allowed) {
  try {
    const value = localStorage.getItem(key);
    return allowed.includes(value) ? value : allowed[0];
  } catch {
    return allowed[0];
  }
}

function keepChoice(key, value) {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Private storage off: the choice lasts until the window closes.
  }
}

function renderSidebar() {
  const counts = new Map();
  for (const c of state.clips) counts.set(c.game, (counts.get(c.game) || 0) + 1);
  if (state.game !== null && !counts.has(state.game)) state.game = null;
  const games = [...counts].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]));

  const nav = $('games');
  nav.replaceChildren();
  const row = (label, count, game, favorites = false) => {
    const b = el('button', 'game' + (state.game === game && state.favorites === favorites ? ' on' : ''));
    b.type = 'button';
    b.append(el('span', 'name', label), el('span', 'mono', String(count)));
    b.addEventListener('click', () => {
      state.game = game;
      state.favorites = favorites;
      if (state.current) closeClip();
      showView('library');
      renderSidebar();
      renderGrid();
    });
    nav.append(b);
  };
  row(L('All clips', 'Todos os clipes', 'Todos los clips'), state.clips.length, null);
  row(L('★ Favorites', '★ Favoritos', '★ Favoritos'), state.clips.filter(c => c.favorite).length, null, true);
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

const gifObserver = new IntersectionObserver(entries => {
  for (const entry of entries) {
    if (!entry.isIntersecting) continue;
    const card = entry.target;
    gifObserver.unobserve(card);
    const image = new Image();
    image.onload = () => {
      const canvas = el('canvas', 'gif-frame');
      canvas.width = image.naturalWidth;
      canvas.height = image.naturalHeight;
      canvas.getContext('2d').drawImage(image, 0, 0);
      card.querySelector('.thumb').prepend(canvas);
    };
    image.src = card.dataset.gif;
  }
});

function renderCard(clip) {
  const card = el('div', 'card');
  card.tabIndex = 0;
  card.dataset.path = clip.path;
  const thumb = el('div', 'thumb');
  card.append(thumb);
  const pick = el('button', 'pick');
  pick.type = 'button';
  pick.setAttribute('role', 'checkbox');
  pick.setAttribute('aria-checked', String(selected.has(clip.path)));
  pick.setAttribute('aria-label', L(`Select ${fileName(clip.path)}`, `Selecionar ${fileName(clip.path)}`, `Seleccionar ${fileName(clip.path)}`));
  pick.append(icon('<path d="M5 12l5 5 9-10"/>'));
  pick.addEventListener('click', e => { e.stopPropagation(); togglePick(clip.path, e.shiftKey); });
  thumb.append(pick);
  if (clip.image) {
    // A picture is its own thumbnail. A GIF would play in every card at once, so its card shows the first frame.
    const url = `${clip.url}?v=${encodeURIComponent(clip.version)}`;
    if (/\.gif$/i.test(clip.path)) {
      thumb.append(el('span', 'tag', 'GIF'));
      card.dataset.gif = url;
      gifObserver.observe(card);
    } else {
      thumb.append(el('span', 'tag', L('SCREENSHOT', 'PRINT', 'CAPTURA')));
      thumb.style.backgroundImage = `url("${url}")`;
    }
  } else if (clip.derived) {
    const name = fileName(clip.path);
    thumb.append(el('span', 'tag', /_discord(_\d+)?\.mp4$/i.test(name) ? 'DISCORD' : /_vertical(_\d+)?\.mp4$/i.test(name) ? 'VERTICAL' : /_joined(_\d+)?\.mp4$/i.test(name) ? L('JOINED', 'JUNTOS', 'UNIDOS') : L('TRIM', 'CORTE', 'RECORTE')));
  } else if (clip.bookmarks) {
    thumb.append(el('span', 'tag', L(`SESSION · ${clip.bookmarks.length} ◆`, `SESSÃO · ${clip.bookmarks.length} ◆`, `SESIÓN · ${clip.bookmarks.length} ◆`)));
  }
  if (clip.favorite) {
    const star = el('span', 'star');
    star.append(icon(ICON_STAR));
    thumb.append(star);
  }
  const quick = el('div', 'quick');
  const quickButton = (paths, label, message, className) => {
    const b = el('button', className);
    b.type = 'button';
    b.setAttribute('aria-label', label);
    b.append(icon(paths));
    b.addEventListener('click', e => { e.stopPropagation(); post({ ...message, path: clip.path }); });
    quick.append(b);
  };
  quickButton(ICON_STAR, clip.favorite ? L('Remove from favorites', 'Tirar dos favoritos', 'Quitar de favoritos') : L('Add to favorites', 'Adicionar aos favoritos', 'Añadir a favoritos'),
    { type: 'favorite', favorite: !clip.favorite }, clip.favorite ? 'fav' : '');
  quickButton(ICON_COPY, L('Copy file', 'Copiar arquivo', 'Copiar archivo'), { type: 'copy' });
  quickButton(ICON_FOLDER, L('Show in folder', 'Mostrar na pasta', 'Mostrar en la carpeta'), { type: 'reveal' });
  thumb.append(quick);

  const text = el('div', 'card-text');
  const date = savedDate(clip);
  text.append(el('div', 'card-title', state.game === null ? clip.game : stem(clip.path)),
    el('div', 'card-sub', `${state.sort === 'largest' || state.sort === 'longest' ? `${dayLabel(date)} · ` : ''}${clockLabel(date)} · ${fmtSize(clip.bytes)}`));
  card.append(text);

  // With a selection going, or Ctrl or Shift held, a click picks instead of opening.
  card.addEventListener('click', e => {
    if (selected.size > 0 || e.ctrlKey || e.shiftKey) togglePick(clip.path, e.shiftKey);
    else openClip(clip.path);
  });
  card.addEventListener('keydown', e => {
    if (e.target !== card) return;
    if (e.key === ' ') {
      e.preventDefault();
      togglePick(clip.path, e.shiftKey);
    } else if (e.key === 'Enter') {
      if (selected.size > 0) togglePick(clip.path, e.shiftKey);
      else openClip(clip.path);
    }
  });
  if (!clip.image) {
    card.addEventListener('pointerenter', e => { if (e.pointerType === 'mouse') previewSoon(card, clip); });
    card.addEventListener('pointerleave', stopPreview);
    applyMeta(card, clip.path);
    if (!state.meta.has(clip.path)) observer.observe(card);
  }
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

// The card under the pointer plays its clip, muted. There is one video, moved from card to card, so only one clip is
// ever being decoded. A replay is saved right after the moment it's for, so what plays is the end of the clip.
const PREVIEW_DELAY_MS = 350;
const PREVIEW_SECONDS = 15;
const preview = document.createElement('video');
preview.className = 'preview';
preview.muted = true;
preview.playsInline = true;
preview.preload = 'metadata';
preview.disablePictureInPicture = true;
preview.setAttribute('aria-hidden', 'true');
let previewTimer = 0;
let previewFrom = 0;

function previewSoon(card, clip) {
  stopPreview();
  // Never behind a game: the window has to be the active one, and passing over cards on the way somewhere starts nothing.
  if (!document.hasFocus() || selected.size > 0) return;
  if (matchMedia('(prefers-reduced-motion: reduce)').matches) return;
  previewTimer = setTimeout(() => {
    previewTimer = 0;
    card.querySelector('.thumb').prepend(preview);
    preview.src = `${clip.url}?v=${encodeURIComponent(clip.version)}`;
  }, PREVIEW_DELAY_MS);
}

// The video holds the file open, and Windows can't rename or delete an open file: every way of reaching those stops this.
function stopPreview() {
  clearTimeout(previewTimer);
  previewTimer = 0;
  if (!preview.isConnected) return;
  preview.classList.remove('on');
  preview.pause();
  preview.removeAttribute('src');
  preview.load();
  preview.remove();
}

preview.addEventListener('loadedmetadata', () => {
  previewFrom = Number.isFinite(preview.duration) ? Math.max(0, preview.duration - PREVIEW_SECONDS) : 0;
  preview.currentTime = previewFrom;
  preview.play().catch(() => {});
});
preview.addEventListener('ended', () => {
  preview.currentTime = previewFrom;
  preview.play().catch(() => {});
});
// The thumbnail stays until there is a picture to replace it.
preview.addEventListener('playing', () => preview.classList.add('on'));
// A codec this window can't play: the thumbnail is still there, and the clip says why when opened.
preview.addEventListener('error', stopPreview);
window.addEventListener('blur', stopPreview);
document.addEventListener('visibilitychange', stopPreview);

// Selection: acting on several clips at once. Only what is on screen stays selected, so an action never
// touches a clip hidden by the search or another game.

const selected = new Set();
let selectAnchor = null;
let binAsking = false;
let binAskedAt = 0;

function togglePick(path, range) {
  // The order on screen: sorting by length can be about to change it.
  const paths = [...document.querySelectorAll('#grid .card')].map(c => c.dataset.path);
  const from = paths.indexOf(selectAnchor), to = paths.indexOf(path);
  if (range && from >= 0 && to >= 0) {
    for (const p of paths.slice(Math.min(from, to), Math.max(from, to) + 1)) selected.add(p);
  } else {
    selectAnchor = selected.delete(path) ? null : path;
    if (selectAnchor) selected.add(path);
  }
  binAsking = false;
  showSelection();
}

function clearSelection() {
  selected.clear();
  selectAnchor = null;
  binAsking = false;
  showSelection();
}

function selectedClips() { return state.clips.filter(c => selected.has(c.path)); }

// The cards stay as they are: a redraw would lose the keyboard's place in the grid.
function showSelection() {
  if (selected.size > 0) stopPreview();
  document.body.classList.toggle('selecting', selected.size > 0);
  for (const card of document.querySelectorAll('#grid .card')) {
    const on = selected.has(card.dataset.path);
    card.classList.toggle('selected', on);
    card.querySelector('.pick').setAttribute('aria-checked', String(on));
  }
  const bar = $('selbar');
  bar.hidden = selected.size === 0;
  bar.classList.toggle('warn', binAsking);
  if (selected.size === 0) return;
  const clips = selectedClips();
  const favorites = clips.filter(c => c.favorite).length;
  const count = selected.size;
  const items = L(`${count} ${count === 1 ? 'item' : 'items'}`, `${count} ${count === 1 ? 'item' : 'itens'}`, `${count} ${count === 1 ? 'elemento' : 'elementos'}`);
  const also = favorites === 0 ? '' : L(` ${favorites} of them ${favorites === 1 ? 'is a favorite' : 'are favorites'}.`,
    ` ${favorites === 1 ? '1 é favorito' : `${favorites} são favoritos`}.`, ` ${favorites} de ellos ${favorites === 1 ? 'es favorito' : 'son favoritos'}.`);
  $('selbar-text').textContent = binAsking
    ? L(`Send ${items} to the Recycle Bin?${also}`, `Mandar ${items} para a Lixeira?${also}`, `¿Enviar ${items} a la Papelera de reciclaje?${also}`)
    : L(`${items} selected`, `${items} ${count === 1 ? 'selecionado' : 'selecionados'}`, `Seleccionados: ${items}`);
  $('sel-all').hidden = binAsking;
  $('sel-join').hidden = binAsking || count < 2 || clips.some(c => c.image);
  $('sel-favorite').hidden = binAsking;
  $('sel-favorite').textContent = favorites === clips.length ? L('Remove from favorites', 'Tirar dos favoritos', 'Quitar de favoritos') : L('Add to favorites', 'Adicionar aos favoritos', 'Añadir a favoritos');
  $('sel-bin').textContent = binAsking ? L('Send to Recycle Bin', 'Mandar para a Lixeira', 'Enviar a la Papelera') : L('Recycle Bin', 'Lixeira', 'Papelera de reciclaje');
  $('sel-cancel').textContent = binAsking ? L('Keep them', 'Manter', 'Conservarlos') : L('Cancel', 'Cancelar', 'Cancelar');
}

$('sel-all').addEventListener('click', () => {
  for (const c of visibleClips()) selected.add(c.path);
  showSelection();
});
$('sel-join').addEventListener('click', () => {
  toast(L('Joining the clips…', 'Juntando os clipes…', 'Uniendo los clips…'));
  post({ type: 'joinClips', paths: selectedClips().map(c => c.path) });
  clearSelection();
});
$('sel-favorite').addEventListener('click', () => {
  const clips = selectedClips();
  post({ type: 'favoriteMany', paths: clips.map(c => c.path), favorite: clips.some(c => !c.favorite) });
  clearSelection();
});
$('sel-bin').addEventListener('click', () => {
  if (!binAsking) {
    binAsking = true;
    binAskedAt = performance.now();
    showSelection();
    // The answer is under the pointer: a double click can't send them unread, and a key held down lands on Keep.
    $('sel-cancel').focus();
    return;
  }
  if (performance.now() - binAskedAt < 600) return;
  post({ type: 'deleteMany', paths: [...selected] });
  clearSelection();
  document.querySelector('#grid .card')?.focus({ preventScroll: true });
});
$('sel-cancel').addEventListener('click', () => {
  if (binAsking) {
    binAsking = false;
    showSelection();
    $('sel-bin').focus();
  } else {
    clearSelection();
  }
});
// From the keyboard the bar is past every card: Delete goes straight to its question, Ctrl+A picks what's on screen.
document.addEventListener('keydown', e => {
  if ($('library').hidden || e.target === $('search')) return;
  if (e.key.toLowerCase() === 'a' && e.ctrlKey && !e.altKey) {
    e.preventDefault();
    // Not under the question: it asked about what was selected then.
    if (!binAsking) $('sel-all').click();
    return;
  }
  if (selected.size === 0) return;
  if (e.key === 'Delete' && !binAsking) {
    e.preventDefault();
    $('sel-bin').click();
  } else if (e.key === 'Escape') {
    if (binAsking) $('sel-cancel').click();
    else clearSelection();
  }
});

function renderGrid() {
  stopPreview();
  const grid = $('grid');
  grid.replaceChildren();
  const clips = visibleClips();
  const shown = new Set(clips.map(c => c.path));
  for (const path of selected) if (!shown.has(path)) selected.delete(path);
  // The list changed under the question: it is asked again about what is there now.
  binAsking = false;
  $('empty').hidden = state.clips.length > 0;
  $('empty-hint').textContent = L(`Press ${state.saveKey} in a game to save a clip.`, `Aperte ${state.saveKey} num jogo para salvar um clipe.`, `Pulsa ${state.saveKey} en un juego para guardar un clip.`);
  $('no-match').hidden = state.clips.length === 0 || clips.length > 0;
  $('count').textContent = state.clips.length
    ? `${clips.length} ${clips.length === 1 ? L('CLIP', 'CLIPE', 'CLIP') : L('CLIPS', 'CLIPES', 'CLIPS')}` : '';

  // Grouped by day only while the days are in order. By size or length a day would come up again and again.
  const byDay = state.sort === 'newest' || state.sort === 'oldest';
  let label = null;
  let cards = null;
  for (const clip of clips) {
    const day = byDay ? dayLabel(savedDate(clip)) : '';
    if (cards === null || day !== label) {
      label = day;
      if (byDay) grid.append(el('div', 'day', day));
      cards = el('div', 'cards');
      grid.append(cards);
    }
    cards.append(renderCard(clip));
  }
  askNextLengths();
  showSelection();
}

// Clip view

const video = $('video');
const canvas = $('wave');
let dragging = null;
let frame = 0;

function showView(name) {
  stopPreview();
  $('library').hidden = name !== 'library';
  $('clip').hidden = name !== 'clip';
  $('settings').hidden = name !== 'settings';
  if (name === 'library') setTimeout(askNextLengths);
  if (name !== 'settings') Settings.stopListening();
  binAsking = false;
  showSelection();
  // The sidebar marks where you are: a game, or Settings.
  document.body.classList.toggle('settings-open', name === 'settings');
  $('nav-settings').classList.toggle('on', name === 'settings');
}

// How fast the player goes. It changes what you see, never the file, so every clip starts at normal speed.
const SPEEDS = [0.25, 0.5, 1, 2];

function setSpeed(speed) {
  // The default too: a video that loads again, after a delete that failed, keeps this speed.
  video.defaultPlaybackRate = speed;
  video.playbackRate = speed;
  $('speed').textContent = `${speed}x`;
  $('speed').classList.toggle('changed', speed !== 1);
}

function stepSpeed(by, wrap) {
  const at = SPEEDS.indexOf(video.playbackRate) + by;
  const next = wrap ? (at + SPEEDS.length) % SPEEDS.length : Math.min(SPEEDS.length - 1, Math.max(0, at));
  setSpeed(SPEEDS[next]);
}

$('speed').addEventListener('click', () => stepSpeed(1, true));

function openClip(path) {
  const clip = state.clips.find(c => c.path.toLowerCase() === path.toLowerCase());
  if (!clip) {
    toast(L('That clip is no longer in your clips folder.', 'Esse clipe não está mais na sua pasta de clipes.', 'Ese clip ya no está en tu carpeta de clips.'), true);
    return;
  }
  state.current = clip;
  setSpeed(1);
  tracksOff.clear();
  trackVolumes.clear();
  tracksDenoised.clear();
  $('a-audio').hidden = false;
  $('tracks-row').hidden = true;
  $('confirm').hidden = true;
  $('rename').hidden = true;
  $('send-row').hidden = true;
  closeFrame();
  $('clip-game').textContent = `${clip.game} · ${stem(clip.path)}`;
  $('no-video').hidden = true;
  // The list leaves image out for videos: undefined would make toggle flip the class instead of removing it.
  $('clip').classList.toggle('is-image', !!clip.image);
  showFavorite();
  // The version keeps WebView2 from serving a cached copy of a file that was replaced under the same name.
  const url = `${clip.url}?v=${encodeURIComponent(clip.version)}`;
  const image = $('image');
  image.hidden = !clip.image;
  video.hidden = !!clip.image;
  if (clip.image) {
    releaseVideo();
    image.src = url;
  } else {
    image.removeAttribute('src');
    video.src = url;
  }
  state.start = 0;
  state.end = 0;
  showView('clip');
  if (clip.image) {
    $('clip-info').textContent = fmtSize(clip.bytes);
    return;
  }
  requestMeta(clip.path);
  setupTimeline();
}

function showFavorite() {
  $('a-marks').hidden = !(state.current && state.current.bookmarks && state.current.bookmarks.length);
  const on = !!(state.current && state.current.favorite);
  $('a-favorite').setAttribute('aria-pressed', String(on));
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

// Tracks left out of the copies for Discord and Shorts. Each clip starts with all of them on.
const tracksOff = new Set();
const trackVolumes = new Map(); // track -> percent; a track not here is at 100
const tracksDenoised = new Set();

function showExportTracks(meta) {
  const names = trackNames(meta);
  $('a-audio').hidden = names.length === 0;
  $('tracks-row').hidden = names.length < 2;
  const list = $('track-list');
  list.replaceChildren();
  names.forEach((name, i) => {
    const b = el('button', 'act', name);
    b.type = 'button';
    b.setAttribute('aria-pressed', String(!tracksOff.has(i)));
    const slider = document.createElement('input');
    slider.type = 'range';
    slider.min = '0';
    slider.max = '200';
    slider.step = '5';
    slider.value = String(trackVolumes.get(i) ?? 100);
    slider.disabled = tracksOff.has(i);
    slider.setAttribute('aria-label', L(`Volume of ${name}`, `Volume de ${name}`, `Volumen de ${name}`));
    const percent = el('span', 'mono muted', `${slider.value}%`);
    slider.addEventListener('input', () => {
      trackVolumes.set(i, Number(slider.value));
      percent.textContent = `${slider.value}%`;
    });
    b.addEventListener('click', () => {
      if (!tracksOff.delete(i)) tracksOff.add(i);
      b.setAttribute('aria-pressed', String(!tracksOff.has(i)));
      slider.disabled = tracksOff.has(i);
    });
    const track = el('span', 'track');
    track.append(b, slider, percent);
    // Only for the microphone: on a game's sound, this filter would eat what it takes for noise.
    if (meta.audioTitles && meta.audioTitles[i] === 'Microphone') {
      const clean = el('button', 'act denoise', L('REDUCE NOISE', 'REDUZIR RUÍDO', 'REDUCIR RUIDO'));
      clean.type = 'button';
      clean.title = L('Takes hiss and fan noise out of this track, in the copies only. The clip stays as it is.',
        'Tira chiado e ruído de ventilador desta faixa, só nas cópias. O clipe fica como está.', 'Quita el siseo y el ruido de ventilador de esta pista, solo en las copias. El clip no cambia.');
      clean.setAttribute('aria-pressed', String(tracksDenoised.has(i)));
      clean.disabled = tracksOff.has(i);
      clean.addEventListener('click', () => {
        if (!tracksDenoised.delete(i)) tracksDenoised.add(i);
        clean.setAttribute('aria-pressed', String(tracksDenoised.has(i)));
      });
      b.addEventListener('click', () => { clean.disabled = tracksOff.has(i); });
      track.append(clean);
    }
    list.append(track);
  });
}

// Nothing when every track stays as recorded, and the app takes them all. A track turned down to nothing is left out,
// so a copy with no sound at all gets the room the sound would have taken.
function exportTracks() {
  const meta = currentMeta();
  if (!meta) return {};
  const all = trackNames(meta).map((_, i) => i);
  const volume = i => trackVolumes.get(i) ?? 100;
  const kept = all.filter(i => !tracksOff.has(i) && volume(i) > 0);
  return {
    ...(kept.length < all.length ? { tracks: kept } : {}),
    ...(all.some(i => volume(i) !== 100) ? { volumes: all.map(i => volume(i) / 100) } : {}),
    ...(kept.some(i => tracksDenoised.has(i)) ? { denoise: kept.filter(i => tracksDenoised.has(i)) } : {}),
  };
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
    tracksLabel(meta), fmtSize(clip.bytes)].join(' · ');
  showExportTracks(meta);
  $('t-end').textContent = fmtTime(meta.duration);
  placeBookmarks();
  layoutRange();
}

// A session's bookmarks on the timeline; a click jumps there.
function placeBookmarks() {
  for (const old of document.querySelectorAll('.bookmark')) old.remove();
  const meta = currentMeta();
  const clip = state.current;
  if (!meta || !clip || !clip.bookmarks) return;
  for (const [i, at] of clip.bookmarks.entries()) {
    if (at > meta.duration) continue;
    const mark = el('div', 'bookmark');
    mark.style.left = `${(at / meta.duration) * 100}%`;
    mark.title = L(`Bookmark ${i + 1} · ${fmtTime(at)}`, `Marcador ${i + 1} · ${fmtTime(at)}`, `Marcador ${i + 1} · ${fmtTime(at)}`);
    mark.addEventListener('pointerdown', e => {
      e.stopPropagation();
      video.currentTime = at;
      placePlayhead();
    });
    $('timeline').append(mark);
  }
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
  const hint = dragging === 'start' ? L(' · STARTS ON A KEYFRAME', ' · COMEÇA NUM KEYFRAME', ' · EMPIEZA EN UN KEYFRAME') : '';
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
  if (!state.current || state.current.image) return;
  if (video.paused) {
    if (video.currentTime < state.start || video.currentTime >= state.end) video.currentTime = state.start;
    video.play().catch(err => {
      if (err && err.name === 'AbortError') return;
      toast(L("This clip can't be played here.", 'Este clipe não pode ser reproduzido aqui.', 'Este clip no se puede reproducir aquí.'), true);
    });
  } else {
    video.pause();
  }
}

video.addEventListener('play', () => {
  document.body.classList.add('playing');
  cancelAnimationFrame(frame);
  frame = requestAnimationFrame(tick);
});
video.addEventListener('ended', () => {
  if (!state.current || state.end <= 0) return;
  video.currentTime = state.start;
  video.play().catch(() => {});
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
  if (!state.current || !currentMeta() || $('a-trim').classList.contains('busy')) return;
  busy($('a-trim'), true);
  toast(L('Saving trim…', 'Salvando corte…', 'Guardando recorte…'));
  post({ type: 'trim', path: state.current.path, start: state.start, end: state.end });
});
$('a-discord').addEventListener('click', () => {
  if (!state.current || $('a-discord').classList.contains('busy')) return;
  busy($('a-discord'), true);
  toast(L('Compressing on the GPU…', 'Comprimindo na GPU…', 'Comprimiendo en la GPU…'));
  post({ type: 'compress', path: state.current.path, ...exportTracks() });
});
// The part of the picture a vertical copy keeps. cropAt is where the frame sits across the room it has, as the app wants it.
let cropAt = 0.5;

// Where the picture is inside the stage, and how wide the 9:16 frame is on it.
function pictureBox() {
  const stage = document.querySelector('.stage');
  const scale = Math.min(stage.clientWidth / video.videoWidth, stage.clientHeight / video.videoHeight);
  const width = video.videoWidth * scale, height = video.videoHeight * scale;
  const origin = stage.getBoundingClientRect().left + stage.clientLeft;
  return { origin, width, height, left: (stage.clientWidth - width) / 2, top: (stage.clientHeight - height) / 2, frame: height * 9 / 16 };
}

function placeFrame() {
  const box = pictureBox();
  const frame = $('vframe');
  frame.style.top = `${box.top}px`;
  frame.style.height = `${box.height}px`;
  frame.style.width = `${box.frame}px`;
  frame.style.left = `${box.left + (box.width - box.frame) * cropAt}px`;
  frame.setAttribute('aria-valuenow', String(Math.round(cropAt * 100)));
}

function closeFrame() {
  $('vframe').hidden = true;
  $('vertical-row').hidden = true;
}

function makeVertical() {
  closeFrame();
  busy($('a-vertical'), true);
  toast(L('Making the vertical copy on the GPU…', 'Fazendo a cópia vertical na GPU…', 'Haciendo la copia vertical en la GPU…'));
  post({ type: 'vertical', path: state.current.path, cropAt, ...exportTracks() });
}

$('a-vertical').addEventListener('click', () => {
  if (!state.current || $('a-vertical').classList.contains('busy')) return;
  if (!$('vframe').hidden) { closeFrame(); return; }
  cropAt = 0.5;
  // A clip that can't be played here, or one already narrower than 9:16, leaves nothing to choose.
  if (!video.videoWidth || video.videoWidth / video.videoHeight <= 9 / 16) { makeVertical(); return; }
  $('rename').hidden = true;
  $('confirm').hidden = true;
  $('send-row').hidden = true;
  $('vframe').hidden = false;
  $('vertical-row').hidden = false;
  placeFrame();
  $('vframe').focus();
});
$('v-yes').addEventListener('click', () => { if (state.current) makeVertical(); });
$('v-no').addEventListener('click', closeFrame);
// The window, or a row appearing below the picture, can change its size while the frame is up.
new ResizeObserver(() => { if (!$('vframe').hidden) placeFrame(); }).observe(document.querySelector('.stage'));

{
  const frame = $('vframe');
  let grab = null;
  frame.addEventListener('pointerdown', e => {
    frame.setPointerCapture(e.pointerId);
    grab = e.clientX - frame.getBoundingClientRect().left;
  });
  frame.addEventListener('pointermove', e => {
    if (grab === null) return;
    const box = pictureBox();
    const room = box.width - box.frame;
    if (room <= 0) return;
    cropAt = Math.min(1, Math.max(0, (e.clientX - grab - box.origin - box.left) / room));
    placeFrame();
  });
  const release = () => { grab = null; };
  frame.addEventListener('pointerup', release);
  frame.addEventListener('pointercancel', release);
  // Over the stage a click plays and pauses, and the arrows seek. Here they move the frame.
  frame.addEventListener('click', e => e.stopPropagation());
  frame.addEventListener('keydown', e => {
    if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return;
    e.preventDefault();
    e.stopPropagation();
    cropAt = Math.min(1, Math.max(0, cropAt + (e.key === 'ArrowLeft' ? -1 : 1) * (e.shiftKey ? 0.01 : 0.05)));
    placeFrame();
  });
}
$('a-gif').addEventListener('click', () => {
  if (!state.current || !currentMeta() || $('a-gif').classList.contains('busy')) return;
  busy($('a-gif'), true);
  toast(L('Making the GIF…', 'Fazendo o GIF…', 'Haciendo el GIF…'));
  post({ type: 'gif', path: state.current.path, start: state.start, end: state.end });
});
$('a-marks').addEventListener('click', () => {
  if (!state.current || $('a-marks').classList.contains('busy')) return;
  busy($('a-marks'), true);
  toast(L('Cutting around the bookmarks…', 'Cortando em volta dos marcadores…', 'Cortando alrededor de los marcadores…'));
  post({ type: 'bookmarkClips', path: state.current.path });
});
$('a-audio').addEventListener('click', () => {
  if (!state.current || !currentMeta() || $('a-audio').classList.contains('busy')) return;
  busy($('a-audio'), true);
  post({ type: 'audio', path: state.current.path, start: state.start, end: state.end, ...exportTracks() });
});
$('a-frame').addEventListener('click', () => {
  if (!state.current || $('a-frame').classList.contains('busy')) return;
  // Stays on the frame being saved, which is the one the result will show.
  video.pause();
  busy($('a-frame'), true);
  post({ type: 'frame', path: state.current.path, start: video.currentTime });
});
// Nothing leaves without saying so first.
$('a-send').addEventListener('click', () => {
  if (!state.current || $('a-send').classList.contains('busy')) return;
  closeFrame();
  $('rename').hidden = true;
  $('confirm').hidden = true;
  $('send-row').hidden = false;
});
$('s-no').addEventListener('click', () => { $('send-row').hidden = true; });
$('s-yes').addEventListener('click', () => {
  $('send-row').hidden = true;
  if (!state.current) return;
  busy($('a-send'), true);
  toast(L('Sending to Discord…', 'Enviando ao Discord…', 'Enviando a Discord…'));
  post({ type: 'sendToDiscord', path: state.current.path, ...(state.current.image ? {} : exportTracks()) });
});
$('a-copy').addEventListener('click', () => { if (state.current) post({ type: 'copy', path: state.current.path }); });
$('a-folder').addEventListener('click', () => { if (state.current) post({ type: 'reveal', path: state.current.path }); });
$('a-favorite').addEventListener('click', () => {
  if (!state.current) return;
  // Shown at once; the list that follows confirms it.
  state.current.favorite = !state.current.favorite;
  showFavorite();
  post({ type: 'favorite', path: state.current.path, favorite: state.current.favorite });
});
$('a-rename').addEventListener('click', () => {
  if (!state.current) return;
  $('confirm').hidden = true;
  $('rename').hidden = false;
  $('rename-ext').textContent = extension(state.current.path);
  const input = $('rename-name');
  input.value = stem(state.current.path);
  input.focus();
  input.select();
});
$('r-no').addEventListener('click', () => { $('rename').hidden = true; });
$('rename-name').addEventListener('keydown', e => { if (e.key === 'Escape') $('rename').hidden = true; });
$('rename').addEventListener('submit', e => {
  e.preventDefault();
  if (!state.current) return;
  const name = $('rename-name').value.trim();
  if (!name || name === stem(state.current.path)) { $('rename').hidden = true; return; }
  $('rename').hidden = true;
  // The player holds the file open, and Windows can't rename an open file.
  releaseVideo();
  post({ type: 'rename', path: state.current.path, name });
});
$('a-delete').addEventListener('click', () => { closeFrame(); $('rename').hidden = true; $('confirm').hidden = false; });
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
state.sort = readChoice('sort', ['newest', 'oldest', 'largest', 'longest']);
$('sort').value = state.sort;
$('kind').value = state.kind;
$('sort').addEventListener('change', e => { state.sort = e.target.value; keepChoice('sort', state.sort); renderGrid(); });
// Not remembered, unlike the sort: opening the library to a clip just saved that a filter hides would look like losing it.
$('kind').addEventListener('change', e => { state.kind = e.target.value; renderGrid(); });

// Sorting by length needs every clip's details, not only the cards on screen. Asked for two at a time and only while
// the list is showing, so a clip opened meanwhile doesn't wait behind the whole library.
let lengthsAsked = 0;
const lengthsFailed = new Set();
function askNextLengths() {
  if (state.sort !== 'longest' || $('library').hidden) return;
  for (const clip of visibleClips()) {
    if (lengthsAsked >= 2) return;
    if (clip.image || state.requested.has(clip.path) || lengthsFailed.has(clip.path)) continue;
    lengthsAsked++;
    requestMeta(clip.path);
  }
}

function lengthAnswered() {
  lengthsAsked = Math.max(0, lengthsAsked - 1);
  askNextLengths();
  resortSoon();
}

// Clips move as their lengths arrive, every so often. The same cards are moved, not redrawn: a selection, a question
// and the keyboard's place all stay, and the thumbnails don't fade in again.
let resortTimer = 0;
function resortSoon() {
  if (state.sort !== 'longest' || resortTimer) return;
  resortTimer = setTimeout(() => {
    resortTimer = 0;
    if (state.sort !== 'longest') return;
    const box = $('grid').querySelector('.cards');
    if (!box) return;
    const focused = document.activeElement;
    const cards = new Map([...box.children].map(c => [c.dataset.path, c]));
    for (const clip of visibleClips()) {
      const card = cards.get(clip.path);
      if (!card) continue;
      card.querySelector('.thumb').classList.remove('loaded');
      box.append(card);
    }
    if (focused && box.contains(focused)) focused.focus();
  }, 1500);
}

document.addEventListener('keydown', e => {
  if (e.target instanceof HTMLInputElement) return;
  if (e.key === 'Escape' && state.current) closeClip();
  else if (e.key === 'Escape' && Settings.isOpen()) showView('library');
  if (e.key === ' ' && state.current && !(e.target instanceof HTMLButtonElement)) {
    e.preventDefault();
    togglePlay();
  }
  playerKey(e);
});

// Arrows seek (Shift for a finer step), comma and period go frame by frame, I and O mark the trim.
function playerKey(e) {
  const meta = currentMeta();
  if (!meta || state.current.image || $('clip').hidden || e.ctrlKey || e.altKey || e.metaKey) return;
  const seek = t => {
    video.currentTime = Math.min(meta.duration, Math.max(0, t));
    placePlayhead();
  };
  const key = e.key.toLowerCase();
  if (key === 'arrowleft' || key === 'arrowright') {
    // A trim handle with focus moves itself with the arrows.
    if (e.target.classList && e.target.classList.contains('handle')) return;
    e.preventDefault();
    seek(video.currentTime + (key === 'arrowleft' ? -1 : 1) * (e.shiftKey ? 1 : 5));
  } else if (key === ',' || key === '.') {
    e.preventDefault();
    video.pause();
    seek(video.currentTime + (key === ',' ? -1 : 1) / (meta.fps || 60));
  } else if (key === 'i' || key === 'o') {
    e.preventDefault();
    moveHandle(key === 'i' ? 'start' : 'end', video.currentTime);
  } else if (key === '<' || key === '>') {
    e.preventDefault();
    stepSpeed(key === '<' ? -1 : 1, false);
  }
}

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
      toast(L('Trim saved', 'Corte salvo', 'Recorte guardado'), false, open);
      break;
    case 'compress':
      busy($('a-discord'), false);
      toast(L(`Compressed to ${message.message}`, `Comprimido para ${message.message}`, `Comprimido a ${message.message}`), false, open);
      break;
    case 'vertical':
      busy($('a-vertical'), false);
      toast(L('Vertical copy saved', 'Cópia vertical salva', 'Copia vertical guardada'), false, open);
      break;
    case 'sendToDiscord':
      busy($('a-send'), false);
      toast(L('Sent to Discord', 'Enviado ao Discord', 'Enviado a Discord'), false, open);
      break;
    case 'bookmarkClips':
      busy($('a-marks'), false);
      toast(message.message === '1'
        ? L('1 clip saved', '1 clipe salvo', '1 clip guardado')
        : L(`${message.message} clips saved`, `${message.message} clipes salvos`, `${message.message} clips guardados`), false, open);
      break;
    case 'audio':
      busy($('a-audio'), false);
      // Not a clip the library lists: "Show" goes to it in its folder.
      toast(L('Audio saved', 'Áudio salvo', 'Audio guardado'), false, () => post({ type: 'reveal', path: message.output }));
      break;
    case 'frame':
      busy($('a-frame'), false);
      toast(L('Frame saved', 'Quadro salvo', 'Fotograma guardado'), false, open);
      break;
    case 'gif':
      busy($('a-gif'), false);
      toast(L(`GIF saved: ${message.message}`, `GIF salvo: ${message.message}`, `GIF guardado: ${message.message}`), false, open);
      break;
    case 'diagnostics':
      toast(L('Diagnostics copied. Paste them in the chat.', 'Diagnóstico copiado. Cole na conversa.', 'Diagnóstico copiado. Pégalo en el chat.'));
      break;
    case 'copy':
      toast(L('Copied. Paste it in Discord.', 'Copiado. Cole no Discord.', 'Copiado. Pégalo en Discord.'));
      break;
    case 'joinClips':
      toast(L('Joined clip saved', 'Clipe unido salvo', 'Clip unido guardado'), false, open);
      break;
    case 'deleteMany':
      if (message.kept > 0) {
        toast(L(`Moved: ${message.moved}. Not moved: ${message.kept}, open in another program or kept by you.`,
          `Movidos: ${message.moved}. Não movidos: ${message.kept}, abertos em outro programa ou mantidos por você.`, `Movidos: ${message.moved}. No movidos: ${message.kept}, abiertos en otro programa o conservados por ti.`), true);
      } else {
        toast(L(`Moved ${message.moved} to the Recycle Bin`, message.moved === 1 ? '1 item movido para a Lixeira' : `${message.moved} itens movidos para a Lixeira`,
          message.moved === 1 ? '1 elemento movido a la Papelera' : `${message.moved} elementos movidos a la Papelera`));
      }
      break;
    case 'delete':
      toast(L('Moved to the Recycle Bin', 'Movido para a Lixeira', 'Movido a la Papelera de reciclaje'));
      if (state.current && state.current.path === message.path) {
        state.current = null;
        showView('library');
      }
      break;
    case 'rename':
      toast(L('Renamed', 'Renomeado', 'Renombrado'));
      // The list with the new name follows; the clip opens again under it.
      if (state.current && state.current.path === message.path) state.pendingOpen = message.output;
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

function showSend(can) {
  $('a-send').hidden = !can;
  if (!can) $('send-row').hidden = true;
}

function onMessage(message) {
  // Saved settings say whether a webhook is there now; the list says it to begin with.
  if (message.type === 'done' && message.config) showSend(!!message.config.discordWebhook);
  if (Settings.onMessage(message)) return;
  switch (message.type) {
    case 'clips': {
      showSend(!!message.canSendToDiscord);
      state.clips = message.clips || [];
      state.listed = true;
      if (message.hotkeyLabels && message.hotkeyLabels.hotkey) state.saveKey = message.hotkeyLabels.hotkey;
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
      // The open clip keeps its place but takes the new favorite and bookmarks.
      if (state.current) {
        state.current = state.clips.find(c => c.path === state.current.path) || state.current;
        showFavorite();
      }
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
      if (state.sort === 'longest') lengthAnswered();
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
      if (message.op === 'vertical') busy($('a-vertical'), false);
      if (message.op === 'gif') busy($('a-gif'), false);
      if (message.op === 'frame') busy($('a-frame'), false);
      if (message.op === 'audio') busy($('a-audio'), false);
      if (message.op === 'bookmarkClips') busy($('a-marks'), false);
      if (message.op === 'sendToDiscord') busy($('a-send'), false);
      // The star was lit before the app answered: a fresh list puts it back as it really is.
      if (message.op === 'favorite' || message.op === 'favoriteMany') post({ type: 'list' });
      // Not deleted or not renamed: the player had let go of the file, so it takes it back.
      if ((message.op === 'delete' || message.op === 'rename') && state.current && state.current.path === message.path && !state.current.image) {
        video.src = `${state.current.url}?v=${encodeURIComponent(state.current.version)}`;
      }
      // A clip whose details can't be read keeps its plain tile and is asked for again when the list next
      // changes (a clip still being copied in, say); only say so when it's open.
      if (message.op === 'meta') state.requested.delete(message.path);
      if (message.op === 'meta' && state.sort === 'longest') {
        // Not asked for again by this sort: it would keep failing, one try after another.
        lengthsFailed.add(message.path);
        lengthAnswered();
      }
      if (message.op === 'meta' && !(state.current && state.current.path === message.path)) break;
      toast(message.message || L('Something went wrong.', 'Algo deu errado.', 'Algo salió mal.'), true);
      break;
    case 'changed':
      post({ type: 'list' });
      break;
    case 'language':
      setLanguage(message.language);
      break;
  }
}

function start() {
  translatePage();
  Settings.translate();
  if (!host) return;
  host.addEventListener('message', e => onMessage(JSON.parse(e.data)));
  post({ type: 'list' });
}
