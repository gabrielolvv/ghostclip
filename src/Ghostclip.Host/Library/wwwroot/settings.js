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
    status: undefined, // recorder status once the app sent it; null while the tray isn't running
  };

  // Built on every render, in the page's language.
  const sections = () => [
    { title: L('GENERAL', 'GERAL'), fields: [
      // Each language is named in itself, so it can be found whichever one the page is in.
      { key: 'language', label: L('Language', 'Idioma'), hint: L('Changes when you save, without touching recording.', 'Muda ao salvar, sem mexer na gravação.'), type: 'select',
        options: [['Auto', L('Same as Windows', 'Igual ao Windows')], ['English', 'English'], ['Portuguese', 'Português']] },
    ] },
    { title: L('CLIPS', 'CLIPES'), fields: [
      { key: 'hotkey', label: L('Save a clip', 'Salvar um clipe'), hint: L('Click, then press the keys.', 'Clique e aperte as teclas.'), type: 'hotkey' },
      { key: 'clipSeconds', label: L('Clip length', 'Duração do clipe'), hint: L('Seconds before the hotkey that go into the clip.', 'Segundos antes da hotkey que entram no clipe.'),
        type: 'number', unit: 'S', min: 10, max: 600 },
      { key: 'clipsFolder', label: L('Clips folder', 'Pasta de clipes'), hint: L('A folder per game goes inside.', 'Dentro dela fica uma pasta por jogo.'),
        type: 'path', pick: 'pickFolder', empty: () => s.saved && s.saved.resolvedClipsFolder, reset: L('Default', 'Padrão') },
      { key: 'confirmationSound', label: L('Sound when a clip is saved', 'Som ao salvar um clipe'), type: 'switch' },
      { key: 'discordLimitMB', label: L('Discord size limit', 'Limite de tamanho do Discord'), hint: L('10 MB on a free account.', '10 MB numa conta gratuita.'),
        type: 'number', unit: 'MB', min: 1, max: 4096 },
    ] },
    { title: L('VIDEO', 'VÍDEO'), note: L('Changing these restarts recording and clears the replay buffer.', 'Mudar estas opções reinicia a gravação e limpa o buffer de replay.'), fields: [
      { key: 'fps', label: L('Frame rate', 'Taxa de quadros'), hint: L('Capturing below the monitor’s refresh rate costs less.', 'Capturar abaixo da taxa de atualização do monitor custa menos.'),
        type: 'number', unit: 'FPS', min: 24, max: 144 },
      { key: 'bitrateMbps', label: 'Bitrate', hint: L('20 Mbps at 1080p60 is about 150 MB a minute.', '20 Mbps em 1080p60 dá uns 150 MB por minuto.'),
        type: 'number', unit: 'MBPS', min: 2, max: 150 },
      { key: 'codec', label: 'Codec', hint: L('H.264 plays everywhere. HEVC looks better at the same size.', 'H.264 toca em qualquer lugar. HEVC fica melhor no mesmo tamanho.'),
        type: 'segmented', options: [['H264', 'H.264'], ['Hevc', 'HEVC']] },
      { key: 'encoder', label: 'Encoder', hint: L('Always the GPU; never a CPU encoder.', 'Sempre a GPU; nunca um encoder de CPU.'), type: 'select', detected: true,
        options: [['Auto', 'Auto'], ['Nvenc', 'NVIDIA NVENC'], ['Amf', 'AMD AMF'], ['Qsv', 'Intel QuickSync']] },
      { key: 'outputHeight', label: L('Resolution', 'Resolução'), hint: L('Scaling down happens on the GPU.', 'A redução de escala acontece na GPU.'), type: 'select', number: true,
        options: [[0, 'Monitor'], [2160, '2160p'], [1440, '1440p'], [1080, '1080p'], [720, '720p']] },
      { key: 'monitor', label: 'Monitor', hint: L('0 is the first monitor of the GPU.', '0 é o primeiro monitor da GPU.'), type: 'number', min: 0, max: 16 },
    ], more: [
      { key: 'input', label: L('Color conversion', 'Conversão de cor'),
        hint: L('Auto uses what was measured to cost least. Through memory is a last resort for drivers where neither works; NVIDIA and AMD only.',
          'Auto usa o que foi medido como mais barato. Pela memória é o último recurso para drivers em que nenhum dos dois funciona; só NVIDIA e AMD.'), type: 'select',
        options: [['Auto', 'Auto'], ['Direct', L('In the encoder', 'No encoder')], ['Nv12', L('On the GPU first', 'Na GPU antes')], ['Memory', L('Through memory', 'Pela memória')]] },
      { key: 'qsvAsyncDepth', label: L('QuickSync queue', 'Fila do QuickSync'), hint: L('1 uses the least memory. Raise it if clips drop frames.', '1 usa menos memória. Aumente se os clipes perderem quadros.'),
        type: 'number', min: 1, max: 8 },
    ] },
    { title: L('AUDIO', 'ÁUDIO'), fields: [
      { key: 'systemAudio', label: L('Record sound', 'Gravar som'), hint: L('Turning it off restarts recording.', 'Desligar reinicia a gravação.'), type: 'switch' },
      { key: 'audioSource', label: L('Sound to record', 'Som a gravar'),
        hint: L('Game only leaves Discord and music out. Switches without a restart.', 'Só o jogo deixa Discord e música de fora. Troca sem reiniciar.'), type: 'segmented',
        options: [['System', L('Everything', 'Tudo')], ['GameOnly', L('Game only', 'Só o jogo')]] },
      { key: 'microphone', label: L('Microphone', 'Microfone'), hint: L('On its own track. Turning it on or off restarts recording.', 'Numa faixa separada. Ligar ou desligar reinicia a gravação.'),
        type: 'switch' },
      { key: 'audioOffsetMs', label: L('Audio offset', 'Atraso do áudio'), hint: L('Positive delays the sound. For a constant lip-sync error.', 'Positivo atrasa o som. Para um erro constante de sincronia labial.'),
        type: 'number', unit: 'MS', min: -1000, max: 1000 },
    ] },
    { title: L('GAME PROFILES', 'PERFIS POR JOGO'), profiles: true },
    { title: L('ADVANCED', 'AVANÇADO'), fields: [
      { key: 'startWithWindows', label: L('Start with Windows', 'Iniciar com o Windows'), hint: L('Installed copies only.', 'Só em cópias instaladas.'), type: 'switch' },
      { key: 'ffmpegPath', label: 'FFmpeg', type: 'path', pick: 'pickFfmpeg', empty: () => L('Bundled', 'Incluído'), reset: L('Use bundled', 'Usar o incluído') },
      { key: 'logLevel', label: L('Log detail', 'Detalhe do log'), type: 'select',
        options: [['Error', L('Errors', 'Erros')], ['Warning', L('Warnings', 'Avisos')], ['Information', 'Normal'], ['Debug', L('Everything', 'Tudo')]] },
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
    $('savebar-text').textContent = s.confirming ? s.confirmText : L('Unsaved changes', 'Alterações não salvas');
    $('s-save').textContent = s.confirming ? L('Save and restart', 'Salvar e reiniciar') : L('Save', 'Salvar');
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
          const o = el('option', '', detected ? `${text} · ${L('detected', 'detectado')}` : text);
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
        const change = el('button', 'act small', L('Change', 'Alterar'));
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
        // The saved hotkey as the app names it on this keyboard; a new one as you pressed it.
        const shown = value === (s.saved && s.saved[field.key]) ? (s.hotkeyLabel || value) : (s.recordedLabel || value);
        const b = el('button', 'act hotkey', shown);
        b.type = 'button';
        b.setAttribute('aria-label', `${field.label}: ${shown}`);
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

  // The hotkey is registered by virtual-key code, which follows the keyboard layout: in WebView2,
  // keyCode is that code, so a key records as the key Windows will match on any layout (AZERTY,
  // QWERTZ, ABNT2). Punctuation and numpad codes get the names of Windows' key list (WinForms Keys).
  const VK_NAMES = { 106: 'Multiply', 107: 'Add', 108: 'Separator', 109: 'Subtract', 110: 'Decimal', 111: 'Divide',
    186: 'OemSemicolon', 187: 'Oemplus', 188: 'Oemcomma', 189: 'OemMinus', 190: 'OemPeriod', 191: 'OemQuestion',
    192: 'Oemtilde', 219: 'OemOpenBrackets', 220: 'OemPipe', 221: 'OemCloseBrackets', 222: 'OemQuotes', 226: 'OemBackslash' };

  function keyName(e) {
    if (/^F([1-9]|1[0-9]|2[0-4])$/.test(e.key)) return e.key;
    const vk = e.keyCode;
    if (vk >= 65 && vk <= 90) return String.fromCharCode(vk);
    if (vk >= 48 && vk <= 57) return String.fromCharCode(vk);
    if (vk >= 96 && vk <= 105) return 'NumPad' + (vk - 96);
    return VK_NAMES[vk] || KEY_NAMES[e.key] || null;
  }

  function listen(button, key) {
    stopListening();
    s.listening = { button, key, before: button.textContent };
    button.classList.add('listening');
    button.textContent = L('Press keys…', 'Aperte as teclas…');
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
      s.listening.button.textContent = L('Add Ctrl, Alt or Shift', 'Adicione Ctrl, Alt ou Shift');
      return;
    }
    const mods = [e.ctrlKey && 'Ctrl', e.altKey && 'Alt', e.shiftKey && 'Shift', e.metaKey && 'Win'].filter(Boolean);
    const combo = [...mods, main].join('+');
    // What the key prints ("´", "ç"), for people; the stored name is for Windows.
    const printed = e.key.length === 1 && e.key !== ' '
      ? (e.code.startsWith('Numpad') ? `Num ${e.key}` : e.key.toUpperCase())
      : main;
    s.recordedLabel = [...mods, printed].join('+');
    s.draft[s.listening.key] = combo;
    stopListening(s.recordedLabel);
    changed();
  }, true);

  // Profiles

  function renderProfiles(card) {
    const box = el('div', 'profiles');
    const list = s.draft.profiles || (s.draft.profiles = []);
    if (list.length === 0) {
      box.append(el('div', 'empty-row', L('No game profiles. A profile changes clip length, sound or the microphone for one game.',
        'Nenhum perfil de jogo. Um perfil muda a duração do clipe, o som ou o microfone de um jogo.')));
    } else {
      const head = el('div', 'profile profile-head');
      head.append(el('span', '', L('GAME', 'JOGO')), el('span', '', L('LENGTH (S)', 'DURAÇÃO (S)')), el('span', '', L('SOUND', 'SOM')),
        el('span', '', L('MICROPHONE', 'MICROFONE')), el('span'));
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
      game.placeholder = L('Game name', 'Nome do jogo');
      game.setAttribute('list', 'games-list');
      game.setAttribute('aria-label', L('Game', 'Jogo'));
      game.addEventListener('input', () => { p.game = game.value; changed(); });

      const length = el('input', 'field');
      length.type = 'number';
      length.min = 10;
      length.max = 600;
      length.placeholder = String(s.draft.clipSeconds);
      length.value = p.clipSeconds ?? '';
      length.setAttribute('aria-label', L('Clip length', 'Duração do clipe'));
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
      sound.setAttribute('aria-label', L('Sound', 'Som'));
      options(sound, [['', L('Default', 'Padrão')], ['System', L('Everything', 'Tudo')], ['GameOnly', L('Game only', 'Só o jogo')]], p.audioSource || '');
      sound.addEventListener('change', () => {
        if (sound.value === '') delete p.audioSource; else p.audioSource = sound.value;
        changed();
      });

      const mic = el('select', 'field');
      mic.setAttribute('aria-label', L('Microphone', 'Microfone'));
      options(mic, [['', L('Default', 'Padrão')], ['true', L('Muted', 'Mudo')], ['false', L('Not muted', 'Ativo')]], p.muteMicrophone === undefined || p.muteMicrophone === null ? '' : String(p.muteMicrophone));
      mic.addEventListener('change', () => {
        if (mic.value === '') delete p.muteMicrophone; else p.muteMicrophone = mic.value === 'true';
        changed();
      });

      const remove = el('button', 'icon');
      remove.type = 'button';
      remove.setAttribute('aria-label', L(`Remove ${p.game || 'profile'}`, `Remover ${p.game || 'perfil'}`));
      remove.append(icon('<path d="M6 6l12 12M18 6L6 18"/>'));
      remove.addEventListener('click', () => { list.splice(i, 1); changed(); render(); });

      line.append(game, length, sound, mic, remove);
      box.append(line);
    });
    const add = el('button', 'act small', L('Add a game', 'Adicionar um jogo'));
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
      form.append(el('p', 'muted', s.unreadable || L('Loading…', 'Carregando…')));
      $('savebar').hidden = true;
      return;
    }
    for (const section of sections()) {
      const card = el('section', 'card-s');
      card.append(el('h2', '', section.title));
      if (section.note) card.append(el('p', 'note', section.note));
      if (section.profiles) renderProfiles(card);
      for (const field of section.fields || []) card.append(row(field, control(field)));
      if (section.more) {
        const more = el('details', 'more');
        more.append(el('summary', '', L('Encoder options', 'Opções do encoder')));
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
    s.status = status || null;
    const box = $('badge');
    let text = L('NOT RUNNING', 'DESLIGADO');
    let kind = '';
    if (status) {
      switch (status.state) {
        case 'Recording':
          text = ['REC', status.encoder, status.fps ? `${status.fps} FPS` : null].filter(Boolean).join(' · ');
          kind = 'rec';
          break;
        case 'WaitingForDesktop': text = L('PAUSED · SCREEN LOCKED', 'PAUSADO · TELA BLOQUEADA'); kind = 'wait'; break;
        case 'Probing': text = L('DETECTING ENCODER', 'DETECTANDO ENCODER'); kind = 'wait'; break;
        case 'Stopped': text = L('NOT RECORDING', 'SEM GRAVAR'); break;
        default: text = L('STARTING', 'INICIANDO'); kind = 'wait'; break;
      }
    }
    box.className = `badge mono ${kind}`;
    $('badge-text').textContent = text;
  }

  // The page's language changed (or the page just opened): the badge and the form are built again,
  // keeping any edits in progress.
  function translate() {
    if (s.status === undefined) $('badge-text').textContent = L('CHECKING', 'VERIFICANDO');
    else badge(s.status);
    render();
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
      if (!name) return L('Give each game profile a name, or remove it.', 'Dê um nome a cada perfil de jogo ou remova-o.');
      if (seen.has(name)) return L(`There are two profiles for ${p.game.trim()}. Keep one.`, `Há dois perfis para ${p.game.trim()}. Mantenha um.`);
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
        s.hotkeyLabel = message.hotkeyLabel;
        s.recordedLabel = null;
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
        s.hotkeyLabel = message.hotkeyLabel;
        s.recordedLabel = null;
        s.confirming = false;
        render();
        toast(L('Settings saved', 'Configurações salvas'));
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
        toast(message.message || L('Settings not saved.', 'Configurações não salvas.'), true);
        return true;
    }
    return false;
  }

  return { open, onMessage, translate, isOpen: () => !$('settings').hidden, stopListening };
})();
