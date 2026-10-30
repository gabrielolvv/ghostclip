'use strict';

// The settings page and the recorder badge. Edits stay in a draft until Save;
// the app writes config.json and the tray applies it from there.

const Settings = (() => {
  const s = {
    saved: null,      // config as the app last sent it
    draft: null,      // what the page shows, edited in place
    encoders: [],     // encoder kinds the recorder measured working here
    games: [],        // names for profile suggestions
    confirming: false,
    listening: null,  // hotkey button waiting for a key combination
  };

  const SECTIONS = [
    { title: 'CLIPS', fields: [
      { key: 'hotkey', label: 'Save a clip', hint: 'Click, then press the keys.', type: 'hotkey' },
      { key: 'clipSeconds', label: 'Clip length', hint: 'Seconds before the hotkey that go into the clip.', type: 'number', unit: 'S', min: 10, max: 600 },
      { key: 'clipsFolder', label: 'Clips folder', hint: 'A folder per game goes inside.', type: 'path', pick: 'pickFolder', empty: () => s.saved && s.saved.resolvedClipsFolder, reset: 'Default' },
      { key: 'confirmationSound', label: 'Sound when a clip is saved', type: 'switch' },
      { key: 'discordLimitMB', label: 'Discord size limit', hint: '10 MB on a free account.', type: 'number', unit: 'MB', min: 1, max: 4096 },
    ] },
    { title: 'VIDEO', note: 'Changing these restarts recording and clears the replay buffer.', fields: [
      { key: 'fps', label: 'Frame rate', hint: 'Capturing below the monitor’s refresh rate costs less.', type: 'number', unit: 'FPS', min: 24, max: 144 },
      { key: 'bitrateMbps', label: 'Bitrate', hint: '20 Mbps at 1080p60 is about 150 MB a minute.', type: 'number', unit: 'MBPS', min: 2, max: 150 },
      { key: 'codec', label: 'Codec', hint: 'H.264 plays everywhere. HEVC looks better at the same size.', type: 'segmented', options: [['H264', 'H.264'], ['Hevc', 'HEVC']] },
      { key: 'encoder', label: 'Encoder', hint: 'Always the GPU; never a CPU encoder.', type: 'select', detected: true,
        options: [['Auto', 'Auto'], ['Nvenc', 'NVIDIA NVENC'], ['Amf', 'AMD AMF'], ['Qsv', 'Intel QuickSync']] },
      { key: 'outputHeight', label: 'Resolution', hint: 'Scaling down happens on the GPU.', type: 'select', number: true,
        options: [[0, 'Monitor'], [2160, '2160p'], [1440, '1440p'], [1080, '1080p'], [720, '720p']] },
      { key: 'monitor', label: 'Monitor', hint: '0 is the first monitor of the GPU.', type: 'number', min: 0, max: 16 },
    ], more: [
      { key: 'input', label: 'Color conversion', hint: 'Auto uses what was measured to cost least.', type: 'select',
        options: [['Auto', 'Auto'], ['Direct', 'In the encoder'], ['Nv12', 'On the GPU first']] },
      { key: 'qsvAsyncDepth', label: 'QuickSync queue', hint: '1 uses the least memory. Raise it if clips drop frames.', type: 'number', min: 1, max: 8 },
    ] },
    { title: 'AUDIO', fields: [
      { key: 'systemAudio', label: 'Record sound', hint: 'Turning it off restarts recording.', type: 'switch' },
      { key: 'audioSource', label: 'Sound to record', hint: 'Game only leaves Discord and music out. Switches without a restart.', type: 'segmented',
        options: [['System', 'Everything'], ['GameOnly', 'Game only']] },
      { key: 'microphone', label: 'Microphone', hint: 'On its own track. Turning it on or off restarts recording.', type: 'switch' },
      { key: 'audioOffsetMs', label: 'Audio offset', hint: 'Positive delays the sound. For a constant lip-sync error.', type: 'number', unit: 'MS', min: -1000, max: 1000 },
    ] },
    { title: 'GAME PROFILES', profiles: true },
    { title: 'ADVANCED', fields: [
      { key: 'startWithWindows', label: 'Start with Windows', hint: 'Installed copies only.', type: 'switch' },
      { key: 'ffmpegPath', label: 'FFmpeg', type: 'path', pick: 'pickFfmpeg', empty: () => 'Bundled', reset: 'Use bundled' },
      { key: 'logLevel', label: 'Log detail', type: 'select',
        options: [['Error', 'Errors'], ['Warning', 'Warnings'], ['Information', 'Normal'], ['Debug', 'Everything']] },
    ] },
  ];

  function clone(value) { return JSON.parse(JSON.stringify(value)); }
  function canonical(value) {
    if (Array.isArray(value)) return value.map(canonical);
    if (value && typeof value === 'object') {
      const out = {};
      for (const key of Object.keys(value).sort()) {
        if (value[key] !== null && value[key] !== undefined) out[key] = canonical(value[key]);
      }
      return out;
    }
    return value;
  }

  function dirty() { return !!s.saved && JSON.stringify(canonical(s.draft)) !== JSON.stringify(canonical(s.saved)); }

  function changed() {
    s.confirming = false;
    updateBar();
  }

  function updateBar(message) {
    if (message) s.confirmText = message;
    const bar = $('savebar');
    bar.hidden = !dirty();
    bar.classList.toggle('warn', s.confirming);
    $('savebar-text').textContent = s.confirming ? s.confirmText : 'Unsaved changes';
    $('s-save').textContent = s.confirming ? 'Save and restart' : 'Save';
  }

  // Controls

  function row(field, control) {
    const r = el('div', 'row');
    const label = el('div', 'row-label');
    label.append(el('div', '', field.label));
    if (field.hint) label.append(el('div', 'hint', field.hint));
    const box = el('div', 'row-control');
    box.append(...[].concat(control));
    r.append(label, box);
    return r;
  }

  function control(field) {
    const value = s.draft[field.key];
    switch (field.type) {
      case 'switch': {
        const b = el('button', 'switch');
        b.type = 'button';
        b.setAttribute('role', 'switch');
        b.setAttribute('aria-label', field.label);
        b.setAttribute('aria-checked', String(!!value));
        b.addEventListener('click', () => {
          s.draft[field.key] = !s.draft[field.key];
          b.setAttribute('aria-checked', String(s.draft[field.key]));
          changed();
        });
        return b;
      }
      case 'number': {
        const input = el('input', 'field');
        input.type = 'number';
        input.min = field.min;
        input.max = field.max;
        input.step = 1;
        input.value = value;
        input.setAttribute('aria-label', field.label);
        input.addEventListener('input', () => {
          const n = Number(input.value);
          if (input.value === '' || !Number.isFinite(n)) return;
          s.draft[field.key] = Math.round(n);
          changed();
        });
        // The app clamps too; showing the clamped value says what will be used.
        input.addEventListener('change', () => {
          const typed = input.value === '' ? NaN : Number(input.value);
          const raw = Number.isFinite(typed) ? typed : s.saved[field.key];
          const n = Math.min(field.max, Math.max(field.min, Math.round(raw)));
          input.value = n;
          s.draft[field.key] = n;
          changed();
        });
        return field.unit ? [input, el('span', 'unit', field.unit)] : input;
      }
      case 'segmented': {
        const group = el('div', 'segmented');
        group.setAttribute('role', 'group');
        group.setAttribute('aria-label', field.label);
        for (const [v, text] of field.options) {
          const b = el('button', '', text);
          b.type = 'button';
          b.setAttribute('aria-pressed', String(value === v));
          b.addEventListener('click', () => {
            s.draft[field.key] = v;
            for (const other of group.children) other.setAttribute('aria-pressed', String(other === b));
            changed();
          });
          group.append(b);
        }
        return group;
      }
      case 'select': {
        const select = el('select', 'field');
        select.setAttribute('aria-label', field.label);
        const options = field.options.slice();
        if (!options.some(([v]) => v === value)) options.push([value, String(value)]);
        for (const [v, text] of options) {
          const detected = field.detected && v !== 'Auto' && s.encoders.includes(v);
          const o = el('option', '', detected ? `${text} · detected` : text);
          o.value = String(v);
          o.selected = v === value;
          select.append(o);
        }
        select.addEventListener('change', () => {
          s.draft[field.key] = field.number ? Number(select.value) : select.value;
          changed();
        });
        return select;
      }
      case 'path': {
        const shown = el('span', 'path' + (value ? '' : ' placeholder'), value || field.empty() || '');
        shown.title = shown.textContent;
        const change = el('button', 'act small', 'Change');
        change.type = 'button';
        change.addEventListener('click', () => post({ type: field.pick }));
        const parts = [shown, change];
        if (value) {
          const reset = el('button', 'act small', field.reset);
          reset.type = 'button';
          reset.addEventListener('click', () => { s.draft[field.key] = null; changed(); render(); });
          parts.push(reset);
        }
        return parts;
      }
      case 'hotkey': {
        const b = el('button', 'act hotkey', value);
        b.type = 'button';
        b.setAttribute('aria-label', `${field.label}: ${value}`);
        b.addEventListener('click', () => listen(b, field.key));
        // Listening ends when focus leaves the field, so a key pressed elsewhere never becomes the hotkey.
        b.addEventListener('blur', () => stopListening());
        return b;
      }
    }
    return el('span');
  }

  // Hotkey capture: the next combination with a main key, Esc to cancel.
  const KEY_NAMES = { ' ': 'Space', ArrowUp: 'Up', ArrowDown: 'Down', ArrowLeft: 'Left', ArrowRight: 'Right',
    PageUp: 'PageUp', PageDown: 'PageDown', Home: 'Home', End: 'End', Insert: 'Insert', Delete: 'Delete',
    Pause: 'Pause', ScrollLock: 'Scroll', PrintScreen: 'PrintScreen', Tab: 'Tab', Enter: 'Enter' };

  // Mirrors HotkeyRules.AllowedWithoutModifier: a key alone would stop working in every other program.
  const ALONE = /^(F([1-9]|1[0-9]|2[0-4])|Pause|Scroll|NumPad[0-9])$/;

  // Punctuation and numpad keys by position, under the names Windows' key list uses (WinForms Keys).
  const CODE_NAMES = { Backquote: 'Oemtilde', Minus: 'OemMinus', Equal: 'Oemplus', BracketLeft: 'OemOpenBrackets',
    BracketRight: 'OemCloseBrackets', Backslash: 'OemPipe', IntlBackslash: 'OemBackslash', Semicolon: 'OemSemicolon',
    Quote: 'OemQuotes', Comma: 'Oemcomma', Period: 'OemPeriod', Slash: 'OemQuestion',
    NumpadAdd: 'Add', NumpadSubtract: 'Subtract', NumpadMultiply: 'Multiply', NumpadDivide: 'Divide', NumpadDecimal: 'Decimal' };

  function keyName(e) {
    if (/^F([1-9]|1[0-9]|2[0-4])$/.test(e.key)) return e.key;
    // A letter as your layout prints it (A on an AZERTY keyboard is A), which is the key Windows registers.
    if (/^[a-z]$/i.test(e.key)) return e.key.toUpperCase();
    if (/^Key[A-Z]$/.test(e.code)) return e.code.slice(3);
    if (/^Digit[0-9]$/.test(e.code)) return e.code.slice(5);
    if (/^Numpad[0-9]$/.test(e.code)) return 'NumPad' + e.code.slice(6);
    return KEY_NAMES[e.key] || CODE_NAMES[e.code] || null;
  }

  function listen(button, key) {
    stopListening();
    s.listening = { button, key, before: button.textContent };
    button.classList.add('listening');
    button.textContent = 'Press keys…';
  }

  function stopListening(text) {
    if (!s.listening) return;
    const { button, before } = s.listening;
    button.classList.remove('listening');
    button.textContent = text || before;
    s.listening = null;
  }

  document.addEventListener('keydown', e => {
    if (!s.listening) return;
    e.preventDefault();
    e.stopPropagation();
    if (e.key === 'Escape') { stopListening(); return; }
    const main = keyName(e);
    if (!main) return; // only modifiers so far
    if (!e.ctrlKey && !e.altKey && !e.shiftKey && !e.metaKey && !ALONE.test(main)) {
      s.listening.button.textContent = 'Add Ctrl, Alt or Shift';
      return;
    }
    const combo = [e.ctrlKey && 'Ctrl', e.altKey && 'Alt', e.shiftKey && 'Shift', e.metaKey && 'Win', main].filter(Boolean).join('+');
    s.draft[s.listening.key] = combo;
    stopListening(combo);
    changed();
  }, true);

  // Profiles

  function renderProfiles(card) {
    const box = el('div', 'profiles');
    const list = s.draft.profiles || (s.draft.profiles = []);
    if (list.length === 0) {
      box.append(el('div', 'empty-row', 'No game profiles. A profile changes clip length, sound or the microphone for one game.'));
    } else {
      const head = el('div', 'profile profile-head');
      head.append(el('span', '', 'GAME'), el('span', '', 'LENGTH (S)'), el('span', '', 'SOUND'), el('span', '', 'MICROPHONE'), el('span'));
      box.append(head);
    }
    const options = (select, pairs, value) => {
      for (const [v, text] of pairs) {
        const o = el('option', '', text);
        o.value = v;
        o.selected = v === value;
        select.append(o);
      }
    };
    list.forEach((p, i) => {
      const line = el('div', 'profile');
      const game = el('input', 'field');
      game.value = p.game || '';
      game.placeholder = 'Game name';
      game.setAttribute('list', 'games-list');
      game.setAttribute('aria-label', 'Game');
      game.addEventListener('input', () => { p.game = game.value; changed(); });

      const length = el('input', 'field');
      length.type = 'number';
      length.min = 10;
      length.max = 600;
      length.placeholder = String(s.draft.clipSeconds);
      length.value = p.clipSeconds ?? '';
      length.setAttribute('aria-label', 'Clip length');
      length.addEventListener('input', () => {
        if (length.value === '') delete p.clipSeconds; else p.clipSeconds = Math.round(Number(length.value));
        changed();
      });
      length.addEventListener('change', () => {
        if (length.value === '' || !Number.isFinite(Number(length.value))) { delete p.clipSeconds; length.value = ''; }
        else { p.clipSeconds = Math.min(600, Math.max(10, Math.round(Number(length.value)))); length.value = p.clipSeconds; }
        changed();
      });

      const sound = el('select', 'field');
      sound.setAttribute('aria-label', 'Sound');
      options(sound, [['', 'Default'], ['System', 'Everything'], ['GameOnly', 'Game only']], p.audioSource || '');
      sound.addEventListener('change', () => {
        if (sound.value === '') delete p.audioSource; else p.audioSource = sound.value;
        changed();
      });

      const mic = el('select', 'field');
      mic.setAttribute('aria-label', 'Microphone');
      options(mic, [['', 'Default'], ['true', 'Muted'], ['false', 'Not muted']], p.muteMicrophone === undefined || p.muteMicrophone === null ? '' : String(p.muteMicrophone));
      mic.addEventListener('change', () => {
        if (mic.value === '') delete p.muteMicrophone; else p.muteMicrophone = mic.value === 'true';
        changed();
      });

      const remove = el('button', 'icon');
      remove.type = 'button';
      remove.setAttribute('aria-label', `Remove ${p.game || 'profile'}`);
      remove.append(icon('<path d="M6 6l12 12M18 6L6 18"/>'));
      remove.addEventListener('click', () => { list.splice(i, 1); changed(); render(); });

      line.append(game, length, sound, mic, remove);
      box.append(line);
    });
    const add = el('button', 'act small', 'Add a game');
    add.type = 'button';
    add.addEventListener('click', () => {
      list.push({ game: '' });
      changed();
      render();
      const rows = document.querySelectorAll('.profile:not(.profile-head)');
      rows[rows.length - 1]?.querySelector('input')?.focus();
    });
    const datalist = el('datalist');
    datalist.id = 'games-list';
    for (const g of s.games) { const o = el('option'); o.value = g; datalist.append(o); }
    box.append(add, datalist);
    card.append(box);
  }

  function render() {
    stopListening();
    const form = $('settings-form');
    const scroll = $('settings').scrollTop;
    form.replaceChildren();
    if (!s.draft) {
      form.append(el('p', 'muted', s.unreadable || 'Loading…'));
      $('savebar').hidden = true;
      return;
    }
    for (const section of SECTIONS) {
      const card = el('section', 'card-s');
      card.append(el('h2', '', section.title));
      if (section.note) card.append(el('p', 'note', section.note));
      if (section.profiles) renderProfiles(card);
      for (const field of section.fields || []) card.append(row(field, control(field)));
      if (section.more) {
        const more = el('details', 'more');
        more.append(el('summary', '', 'Encoder options'));
        for (const field of section.more) more.append(row(field, control(field)));
        card.append(more);
      }
      form.append(card);
    }
    $('settings').scrollTop = scroll;
    updateBar();
  }

  // Badge

  function badge(status) {
    const box = $('badge');
    let text = 'NOT RUNNING';
    let kind = '';
    if (status) {
      switch (status.state) {
        case 'Recording':
          text = ['REC', status.encoder, status.fps ? `${status.fps} FPS` : null].filter(Boolean).join(' · ');
          kind = 'rec';
          break;
        case 'WaitingForDesktop': text = 'PAUSED · SCREEN LOCKED'; kind = 'wait'; break;
        case 'Probing': text = 'DETECTING ENCODER'; kind = 'wait'; break;
        case 'Stopped': text = 'NOT RECORDING'; break;
        default: text = 'STARTING'; kind = 'wait'; break;
      }
    }
    box.className = `badge mono ${kind}`;
    $('badge-text').textContent = text;
  }

  // Wiring

  function open() {
    if (state.current) closeClip();
    showView('settings');
    if (!dirty()) post({ type: 'settings' });
    render();
  }

  $('nav-settings').addEventListener('click', open);
  $('s-discard').addEventListener('click', () => {
    s.draft = clone(s.saved);
    s.confirming = false;
    render();
  });
  // A profile without a name would vanish on save, and a second one for the same game would never apply.
  function profileProblem() {
    const seen = new Set();
    for (const p of s.draft.profiles || []) {
      const name = (p.game || '').trim().toLowerCase();
      if (!name) return 'Give each game profile a name, or remove it.';
      if (seen.has(name)) return `There are two profiles for ${p.game.trim()}. Keep one.`;
      seen.add(name);
    }
    return null;
  }

  $('s-save').addEventListener('click', () => {
    if (!s.draft || $('s-save').classList.contains('busy')) return;
    const problem = profileProblem();
    if (problem) { toast(problem, true); return; }
    $('s-save').classList.add('busy');
    post({ type: 'saveSettings', config: s.draft, confirmed: s.confirming });
  });

  function onMessage(message) {
    switch (message.type) {
      case 'settings':
        s.encoders = message.encoders || [];
        s.games = message.games || [];
        s.unreadable = null;
        badge(message.status);
        // Edits in progress win over a refresh that arrives late.
        if (dirty()) return true;
        s.saved = message.config;
        s.draft = clone(message.config);
        s.confirming = false;
        render();
        return true;
      case 'settingsChanged':
        // config.json changed outside this page (a hand edit, another window): show it unless you're editing.
        if (!$('settings').hidden && !dirty()) post({ type: 'settings' });
        return true;
      case 'confirm':
        $('s-save').classList.remove('busy');
        s.confirming = true;
        updateBar(message.message);
        return true;
      case 'picked':
        s.draft[message.op] = message.path;
        changed();
        render();
        return true;
      case 'status':
        badge(message.status);
        return true;
      case 'showSettings':
        open();
        return true;
      case 'done':
        if (message.op !== 'saveSettings') return false;
        $('s-save').classList.remove('busy');
        s.saved = message.config;
        s.draft = clone(message.config);
        s.confirming = false;
        render();
        toast('Settings saved');
        return true;
      case 'error':
        if (message.op === 'settings') {
          // Nothing is shown or saved from a file that can't be read: saving would replace it with defaults.
          s.saved = null;
          s.draft = null;
          s.unreadable = message.message;
          render();
          return true;
        }
        if (message.op !== 'saveSettings') return false;
        $('s-save').classList.remove('busy');
        s.confirming = false;
        updateBar();
        toast(message.message || 'Settings not saved.', true);
        return true;
    }
    return false;
  }

  return { open, onMessage, isOpen: () => !$('settings').hidden, stopListening };
})();
