'use strict';

// The settings page and the recorder badge. Edits stay in a draft until Save;
// the app writes config.json and the tray applies it from there.

const Settings = (() => {
  const s = {
    saved: null,      // config as the app last sent it
    draft: null,      // what the page shows, edited in place
    encoders: [],     // encoder kinds the recorder measured working here
    monitors: null,   // the screens Desktop Duplication can record; null when the app couldn't list them
    games: [],        // names for profile suggestions
    confirming: false,
    listening: null,  // hotkey button waiting for a key combination
    status: undefined, // recorder status once the app sent it; null while the tray isn't running
  };

  // Built on every render, in the page's language.
  const sections = () => [
    { title: L('GENERAL', 'GERAL', 'GENERAL'), fields: [
      // Each language is named in itself, so it can be found whichever one the page is in.
      { key: 'language', label: L('Language', 'Idioma', 'Idioma'), hint: L('Changes when you save, without touching recording.', 'Muda ao salvar, sem mexer na gravação.', 'Cambia al guardar, sin afectar la grabación.'), type: 'select',
        options: [['Auto', L('Same as Windows', 'Igual ao Windows', 'Igual que Windows')], ['English', 'English'], ['Portuguese', 'Português'], ['Spanish', 'Español']] },
    ] },
    { title: L('CLIPS', 'CLIPES', 'CLIPS'), fields: [
      { key: 'hotkey', label: L('Save a clip', 'Salvar um clipe', 'Guardar un clip'), hint: L('Click, then press the keys. During a session it also adds a bookmark.', 'Clique e aperte as teclas. Durante uma sessão, também marca o momento.', 'Haz clic y pulsa las teclas. Durante una sesión, también añade un marcador.'), type: 'hotkey' },
      { key: 'screenshotHotkey', label: L('Take a screenshot', 'Tirar print', 'Hacer una captura'), hint: L('Saved as a JPEG next to the clips.', 'Salvo em JPEG junto dos clipes.', 'Se guarda como JPEG junto a los clips.'), type: 'hotkey' },
      { key: 'sessionHotkey', label: L('Record a session', 'Gravar sessão', 'Grabar sesión'),
        hint: L('Press to start and again to stop. Keeps everything in between: about 9 GB an hour at 20 Mbps.', 'Aperte para começar e de novo para parar. Guarda tudo entre os dois: uns 9 GB por hora a 20 Mbps.', 'Pulsa para empezar y otra vez para parar. Guarda todo lo que hay en medio: unos 9 GB por hora a 20 Mbps.'), type: 'hotkey' },
      { key: 'pauseHotkey', label: L('Pause recording', 'Pausar a gravação', 'Pausar la grabación'),
        hint: L('Optional: stops recording and deletes the replay buffer until you press it again, for a bank’s page or a password. The tray menu does it too. Ghostclip records again when it restarts.',
          'Opcional: para a gravação e apaga o buffer de replay até você apertar de novo, para a página do banco ou uma senha. O menu da bandeja também faz isso. O Ghostclip volta a gravar quando reinicia.', 'Opcional: detiene la grabación y borra el búfer de repetición hasta que la pulses de nuevo, para la página del banco o una contraseña. El menú de la bandeja también lo hace. Ghostclip vuelve a grabar cuando se reinicia.'), type: 'hotkey', optional: true },
      { key: 'privateApps', label: L('Pause for these programs', 'Pausar para estes programas', 'Pausar para estos programas'),
        hint: L('While one is the active window, recording pauses by itself and the replay buffer is deleted. Beside another active window it can still be recorded. Names as in the Details tab of Task Manager, separated by commas. Programs only: not a website in a browser, and Store apps may not be recognized.',
          'Enquanto um deles for a janela ativa, a gravação pausa sozinha e o buffer de replay é apagado. Ao lado de outra janela ativa, ele ainda pode ser gravado. Nomes como na aba Detalhes do Gerenciador de Tarefas, separados por vírgula. Só programas: não um site no navegador, e apps da Store podem não ser reconhecidos.', 'Mientras uno de ellos sea la ventana activa, la grabación se pausa sola y el búfer de repetición se borra. Junto a otra ventana activa, todavía se puede grabar. Nombres como en la pestaña Detalles del Administrador de tareas, separados por comas. Solo programas: no un sitio web en un navegador, y puede que las apps de la Store no se reconozcan.'),
        type: 'names', placeholder: 'KeePass, Bitwarden' },
      { key: 'clipSeconds', label: L('Clip length', 'Duração do clipe', 'Duración del clip'), hint: L('Seconds before the hotkey that go into the clip.', 'Segundos antes da hotkey que entram no clipe.', 'Segundos antes de la tecla que entran en el clip.'),
        type: 'number', unit: 'S', min: 10, max: 600 },
      { key: 'shortClipHotkey', label: L('Save a short clip', 'Salvar um clipe curto', 'Guardar un clip corto'),
        hint: L('Optional: a second key that saves a clip of its own length. No key is taken until you choose one.',
          'Opcional: uma segunda tecla que salva um clipe com duração própria. Nenhuma tecla é ocupada até você escolher uma.', 'Opcional: una segunda tecla que guarda un clip con su propia duración. No se ocupa ninguna tecla hasta que elijas una.'), type: 'hotkey', optional: true },
      { key: 'shortClipSeconds', label: L('Short clip length', 'Duração do clipe curto', 'Duración del clip corto'),
        hint: L('The same in every game. With a key chosen, if it is longer than the clip length, the replay buffer keeps that much too.',
          'A mesma em todos os jogos. Com uma tecla escolhida, se for maior que a duração do clipe, o buffer de replay também guarda esse tanto.', 'La misma en todos los juegos. Con una tecla elegida, si es mayor que la duración del clip, el búfer de repetición también guarda ese tiempo.'),
        type: 'number', unit: 'S', min: 10, max: 600 },
      { key: 'clipsFolder', label: L('Clips folder', 'Pasta de clipes', 'Carpeta de clips'), hint: L('A folder per game goes inside.', 'Dentro dela fica uma pasta por jogo.', 'Dentro va una carpeta por juego.'),
        type: 'path', pick: 'pickFolder', empty: () => s.saved && s.saved.resolvedClipsFolder, reset: L('Default', 'Padrão', 'Predeterminada') },
      { key: 'confirmationSound', label: L('Confirmation sounds', 'Sons de confirmação', 'Sonidos de confirmación'),
        hint: L('When a clip, screenshot or session is saved. Muting the microphone always sounds.', 'Ao salvar um clipe, print ou sessão. Mutar o microfone sempre toca.', 'Al guardar un clip, una captura o una sesión. Silenciar el micrófono siempre suena.'), type: 'switch' },
      { key: 'discordLimitMB', label: L('Discord size limit', 'Limite de tamanho do Discord', 'Límite de tamaño de Discord'), hint: L('10 MB on a free account.', '10 MB numa conta gratuita.', '10 MB en una cuenta gratuita.'),
        type: 'number', unit: 'MB', min: 1, max: 4096 },
      { key: 'discordWebhook', label: L('Discord webhook', 'Webhook do Discord', 'Webhook de Discord'),
        hint: L('To send clips to a channel from the player. In Discord: channel settings > Integrations > Webhooks. Anyone with this address can post there, so keep it private. A clip only leaves when you send it.',
          'Para enviar clipes a um canal pelo player. No Discord: configurações do canal > Integrações > Webhooks. Quem tem este endereço pode postar lá, então não o compartilhe. Um clipe só sai quando você envia.',
          'Para enviar clips a un canal desde el reproductor. En Discord: ajustes del canal > Integraciones > Webhooks. Quien tenga esta dirección puede publicar allí, así que no la compartas. Un clip solo sale cuando lo envías.'),
        type: 'secret' },
      { key: 'storageLimitGB', label: L('Space for clips', 'Espaço para clipes', 'Espacio para clips'),
        hint: L('0 keeps everything. Past the limit the oldest are deleted for good; favorites and clips you renamed never are.',
          '0 guarda tudo. Passando do limite, os mais antigos são apagados de vez; favoritos e clipes renomeados nunca.', '0 guarda todo. Al pasar el límite, los más antiguos se eliminan definitivamente; los favoritos y los clips renombrados nunca.')
          + (s.storageBytes != null ? L(` Using ${fmtSize(s.storageBytes)} now.`, ` Usando ${fmtSize(s.storageBytes)} agora.`, ` Usando ${fmtSize(s.storageBytes)} ahora.`) : ''),
        type: 'number', unit: 'GB', min: 0, max: 100000 },
      { key: 'maxClipAgeDays', label: L('Delete clips older than', 'Apagar clipes com mais de', 'Eliminar clips de más de'),
        hint: L('0 keeps them. Past this age they go to the Recycle Bin when no game is running; favorites and clips you renamed never do. To keep one you bring back from the bin, star it.',
          '0 guarda. Passando dessa idade, vão para a Lixeira quando nenhum jogo está aberto; favoritos e clipes renomeados nunca. Para manter um que você recuperar da Lixeira, marque como favorito.',
          '0 los guarda. Pasada esta antigüedad van a la Papelera cuando no hay ningún juego abierto; los favoritos y los clips renombrados nunca. Para conservar uno que recuperes de la Papelera, márcalo como favorito.'),
        type: 'number', unit: L('DAYS', 'DIAS', 'DÍAS'), min: 0, max: 36500 },
    ] },
    { title: L('VIDEO', 'VÍDEO', 'VIDEO'), note: L('Changing these restarts recording and clears the replay buffer.', 'Mudar estas opções reinicia a gravação e limpa o buffer de replay.', 'Cambiar estas opciones reinicia la grabación y limpia el búfer de repetición.'), fields: [
      { key: 'fps', label: L('Frame rate', 'Taxa de quadros', 'Fotogramas por segundo'), hint: L('Capturing below the monitor’s refresh rate costs less.', 'Capturar abaixo da taxa de atualização do monitor custa menos.', 'Capturar por debajo de la frecuencia de actualización del monitor cuesta menos.'),
        type: 'number', unit: 'FPS', min: 24, max: 144 },
      { key: 'bitrateMbps', label: 'Bitrate', hint: L('20 Mbps at 1080p60 is about 150 MB a minute.', '20 Mbps em 1080p60 dá uns 150 MB por minuto.', '20 Mbps en 1080p60 son unos 150 MB por minuto.'),
        type: 'number', unit: 'MBPS', min: 2, max: 150 },
      { key: 'codec', label: 'Codec', hint: L('H.264 plays everywhere. HEVC looks better at the same size.', 'H.264 toca em qualquer lugar. HEVC fica melhor no mesmo tamanho.', 'H.264 se reproduce en cualquier lado. HEVC se ve mejor con el mismo tamaño.'),
        type: 'segmented', options: [['H264', 'H.264'], ['Hevc', 'HEVC']] },
      { key: 'encoder', label: 'Encoder', hint: L('Always the GPU; never a CPU encoder.', 'Sempre a GPU; nunca um encoder de CPU.', 'Siempre la GPU; nunca un encoder de CPU.'), type: 'select', detected: true,
        options: [['Auto', 'Auto'], ['Nvenc', 'NVIDIA NVENC'], ['Amf', 'AMD AMF'], ['Qsv', 'Intel QuickSync']] },
      { key: 'outputHeight', label: L('Resolution', 'Resolução', 'Resolución'), hint: L('Scaling down happens on the GPU.', 'A redução de escala acontece na GPU.', 'La reducción de escala se hace en la GPU.'), type: 'select', number: true,
        options: [[0, 'Monitor'], [2160, '2160p'], [1440, '1440p'], [1080, '1080p'], [720, '720p']] },
      { key: 'monitor', label: 'Monitor', hint: L('The screen to record, with either capture method.', 'A tela a gravar, com qualquer um dos métodos de captura.', 'La pantalla que se graba, con cualquiera de los dos métodos de captura.'), type: 'monitor', min: 0, max: 16 },
      { key: 'bufferFolder', label: L('Replay buffer folder', 'Pasta do buffer de replay', 'Carpeta del búfer de repetición'),
        hint: L('The buffer writes to its drive all the time while recording. Another drive spares the system’s SSD. Changing it clears the buffer.',
          'O buffer escreve no disco o tempo todo enquanto grava. Outro disco poupa o SSD do sistema. Mudar a pasta limpa o buffer.', 'El búfer escribe en su disco todo el tiempo mientras graba. Otro disco cuida el SSD del sistema. Cambiar la carpeta limpia el búfer.'),
        type: 'path', pick: 'pickBufferFolder', empty: () => L('With Ghostclip’s data, on the system drive', 'Com os dados do Ghostclip, no disco do sistema', 'Con los datos de Ghostclip, en el disco del sistema'), reset: L('Default', 'Padrão', 'Predeterminada') },
      { key: 'capture', label: L('Capture', 'Captura', 'Captura'),
        hint: L('Try Windows Graphics Capture if games lose FPS while recording. On Windows 10 it may show a yellow border.',
          'Experimente o Windows Graphics Capture se os jogos perderem FPS gravando. No Windows 10 pode aparecer uma borda amarela.', 'Prueba Windows Graphics Capture si los juegos pierden FPS al grabar. En Windows 10 puede aparecer un borde amarillo.'),
        type: 'select', options: [['DesktopDuplication', 'Desktop Duplication'], ['GraphicsCapture', 'Windows Graphics Capture']] },
    ], more: [
      { key: 'input', label: L('Color conversion', 'Conversão de cor', 'Conversión de color'),
        hint: L('Auto uses what was measured to cost least. Through memory is a last resort for drivers where neither works; NVIDIA and AMD only.',
          'Auto usa o que foi medido como mais barato. Pela memória é o último recurso para drivers em que nenhum dos dois funciona; só NVIDIA e AMD.', 'Auto usa lo que se midió como más barato. Por memoria es el último recurso para drivers donde ninguno de los dos funciona; solo NVIDIA y AMD.'), type: 'select',
        options: [['Auto', 'Auto'], ['Direct', L('In the encoder', 'No encoder', 'En el encoder')], ['Nv12', L('On the GPU first', 'Na GPU antes', 'Primero en la GPU')], ['Memory', L('Through memory', 'Pela memória', 'Por memoria')]] },
      { key: 'qsvAsyncDepth', label: L('QuickSync queue', 'Fila do QuickSync', 'Cola de QuickSync'), hint: L('1 uses the least memory. Raise it if clips drop frames.', '1 usa menos memória. Aumente se os clipes perderem quadros.', '1 usa menos memoria. Súbela si los clips pierden fotogramas.'),
        type: 'number', min: 1, max: 8 },
    ] },
    { title: L('AUDIO', 'ÁUDIO', 'AUDIO'), fields: [
      { key: 'systemAudio', label: L('Record sound', 'Gravar som', 'Grabar sonido'), hint: L('Turning it off restarts recording.', 'Desligar reinicia a gravação.', 'Desactivarlo reinicia la grabación.'), type: 'switch' },
      { key: 'audioSource', label: L('Sound to record', 'Som a gravar', 'Sonido a grabar'),
        hint: L('Game only leaves Discord and music out. Switches without a restart.', 'Só o jogo deixa Discord e música de fora. Troca sem reiniciar.', 'Solo el juego deja fuera Discord y la música. Cambia sin reiniciar.'), type: 'segmented',
        options: [['System', L('Everything', 'Tudo', 'Todo')], ['GameOnly', L('Game only', 'Só o jogo', 'Solo el juego')]] },
      { key: 'discordTrack', label: L('Discord on its own track', 'Discord em faixa própria', 'Discord en su propia pista'),
        hint: L('The Discord app goes to a separate track and out of the main one, so a call can be muted later. Turning it on or off restarts recording.',
          'O app do Discord vai para uma faixa separada e sai da principal, para dar para silenciar a call depois. Ligar ou desligar reinicia a gravação.', 'La app de Discord va a una pista separada y sale de la principal, para poder silenciar la llamada después. Activarlo o desactivarlo reinicia la grabación.'), type: 'switch' },
      { key: 'microphone', label: L('Microphone', 'Microfone', 'Micrófono'), hint: L('On its own track. Turning it on or off restarts recording.', 'Numa faixa separada. Ligar ou desligar reinicia a gravação.', 'En su propia pista. Activarlo o desactivarlo reinicia la grabación.'),
        type: 'switch' },
      { key: 'micMuteHotkey', label: L('Mute the microphone', 'Mutar o microfone', 'Silenciar el micrófono'),
        hint: L('Press to mute the microphone track and again to unmute it, without restarting recording. Works while the microphone is on.',
          'Aperte para mutar a faixa do microfone e de novo para desmutar, sem reiniciar a gravação. Funciona com o microfone ligado.', 'Pulsa para silenciar la pista del micrófono y otra vez para activarla, sin reiniciar la grabación. Funciona con el micrófono activado.'), type: 'hotkey' },
      { key: 'audioOffsetMs', label: L('Audio offset', 'Atraso do áudio', 'Desfase del audio'), hint: L('Positive delays the sound. For a constant lip-sync error.', 'Positivo atrasa o som. Para um erro constante de sincronia labial.', 'Positivo retrasa el sonido. Para un error constante de sincronía labial.'),
        type: 'number', unit: 'MS', min: -1000, max: 1000 },
    ] },
    { title: L('GAME PROFILES', 'PERFIS POR JOGO', 'PERFILES POR JUEGO'), profiles: true },
    { title: L('ADVANCED', 'AVANÇADO', 'AVANZADO'), fields: [
      { key: 'gamePriority', label: L('Games first', 'Prioridade para os jogos', 'Prioridad a los juegos'),
        hint: L('Recording yields the CPU and GPU to the game. Turn it off if clips stutter.', 'A gravação cede a CPU e a GPU para o jogo. Desligue se os clipes travarem.', 'La grabación cede la CPU y la GPU al juego. Desactívalo si los clips se traban.'), type: 'switch' },
      { key: 'clearBufferOnExit', label: L('Delete the replay buffer on exit', 'Apagar o buffer de replay ao sair', 'Borrar el búfer de repetición al salir'),
        hint: L('The buffer is your last moments of screen. Off, it stays on disk until Ghostclip next starts, and clips can reach back across a quick restart.',
          'O buffer são os últimos momentos da sua tela. Desligado, ele fica no disco até o Ghostclip abrir de novo, e os clipes alcançam o que veio antes de um reinício rápido.', 'El búfer son los últimos momentos de tu pantalla. Desactivado, se queda en el disco hasta que Ghostclip vuelva a abrirse, y los clips alcanzan lo que hubo antes de un reinicio rápido.'), type: 'switch' },
      { key: 'startWithWindows', label: L('Start with Windows', 'Iniciar com o Windows', 'Iniciar con Windows'), hint: L('Installed copies only.', 'Só em cópias instaladas.', 'Solo en copias instaladas.'), type: 'switch' },
      { key: 'ffmpegPath', label: 'FFmpeg', type: 'path', pick: 'pickFfmpeg', empty: () => L('Bundled', 'Incluído', 'Incluido'), reset: L('Use bundled', 'Usar o incluído', 'Usar el incluido') },
      { key: 'logLevel', label: L('Log detail', 'Detalhe do log', 'Detalle del registro'), type: 'select',
        options: [['Error', L('Errors', 'Erros', 'Errores')], ['Warning', L('Warnings', 'Avisos', 'Advertencias')], ['Information', 'Normal'], ['Debug', L('Everything', 'Tudo', 'Todo')]] },
      { label: L('Diagnostics', 'Diagnóstico', 'Diagnóstico'),
        hint: L('Copies your GPU, Windows and recording details, to paste in a bug report or an FPS test.',
          'Copia sua placa de vídeo, o Windows e os detalhes da gravação, para colar num relato de problema ou teste de FPS.', 'Copia tu tarjeta gráfica, Windows y los detalles de la grabación, para pegarlos en un reporte de problema o una prueba de FPS.'),
        type: 'action', text: L('Copy diagnostics', 'Copiar diagnóstico', 'Copiar diagnóstico'), action: 'diagnostics' },
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
    $('savebar-text').textContent = s.confirming ? s.confirmText : L('Unsaved changes', 'Alterações não salvas', 'Cambios sin guardar');
    // The bar's text says what saving costs (a restart, deleted clips); the button only confirms it.
    $('s-save').textContent = s.confirming ? L('Save anyway', 'Salvar mesmo assim', 'Guardar igualmente') : L('Save', 'Salvar', 'Guardar');
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

  // "LG ULTRAGEAR · 2560×1440 · main". Two monitors with the same name are told apart by where they sit;
  // the saved one stays listed when it is unplugged, so saving never moves recording to another screen.
  // A monitor on another GPU is recorded by Windows Graphics Capture only.
  function monitorOptions(value) {
    const list = s.monitors.slice().sort((a, b) => a.index - b.index);
    const named = m => m.name || (m.builtIn ? L('Built-in screen', 'Tela integrada', 'Pantalla integrada') : `Monitor ${m.index + 1}`);
    const options = list.map(m => {
      const twins = list.filter(o => named(o) === named(m));
      const parts = [named(m)];
      if (twins.length > 1) parts.push(place(m, twins));
      parts.push(`${m.width}×${m.height}`);
      if (m.primary) parts.push(L('main', 'principal', 'principal'));
      if (m.otherGpu) parts.push(L('Windows Graphics Capture only', 'só Windows Graphics Capture', 'solo Windows Graphics Capture'));
      return [m.index, parts.join(' · ')];
    });
    if (!list.some(m => m.index === value)) options.push([value, L(`Monitor ${value + 1} (not connected)`, `Monitor ${value + 1} (desconectado)`, `Monitor ${value + 1} (desconectado)`)]);
    return options;
  }

  function place(m, twins) {
    const lefts = twins.map(t => t.left), tops = twins.map(t => t.top);
    if (twins.length === 2 && lefts[0] !== lefts[1]) return m.left === Math.min(...lefts) ? L('on the left', 'à esquerda', 'a la izquierda') : L('on the right', 'à direita', 'a la derecha');
    if (twins.length === 2 && tops[0] !== tops[1]) return m.top === Math.min(...tops) ? L('on top', 'em cima', 'arriba') : L('below', 'embaixo', 'abajo');
    // Three or more: numbered by where they sit, top to bottom, left to right.
    const order = twins.slice().sort((a, b) => a.top - b.top || a.left - b.left);
    return `#${order.indexOf(m) + 1}`;
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
      case 'monitor':
        // Without the app's list, the output number as before.
        return s.monitors && s.monitors.length
          ? control({ ...field, type: 'select', number: true, options: monitorOptions(value) })
          : control({ ...field, type: 'number' });
      case 'select': {
        const select = el('select', 'field');
        select.setAttribute('aria-label', field.label);
        const options = field.options.slice();
        if (!options.some(([v]) => v === value)) options.push([value, String(value)]);
        for (const [v, text] of options) {
          const detected = field.detected && v !== 'Auto' && s.encoders.includes(v);
          const o = el('option', '', detected ? `${text} · ${L('detected', 'detectado', 'detectado')}` : text);
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
      case 'secret': {
        const input = el('input', 'field');
        input.type = 'password';
        input.autocomplete = 'off';
        input.spellcheck = false;
        input.value = value || '';
        input.setAttribute('aria-label', field.label);
        input.addEventListener('input', () => {
          s.draft[field.key] = input.value.trim() || null;
          changed();
        });
        return input;
      }
      case 'path': {
        const shown = el('span', 'path' + (value ? '' : ' placeholder'), value || field.empty() || '');
        shown.title = shown.textContent;
        const change = el('button', 'act small', L('Change', 'Alterar', 'Cambiar'));
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
      case 'names': {
        const input = el('input', 'field names');
        input.value = (value || []).join(', ');
        input.placeholder = field.placeholder;
        input.spellcheck = false;
        input.setAttribute('aria-label', field.label);
        input.addEventListener('input', () => {
          s.draft[field.key] = input.value.split(/[,;]/).map(n => n.trim()).filter(Boolean);
          changed();
        });
        return input;
      }
      case 'action': {
        // Not a setting: a button that asks the app for something.
        const b = el('button', 'act small', field.text);
        b.type = 'button';
        b.addEventListener('click', () => post({ type: field.action }));
        return b;
      }
      case 'hotkey': {
        // The saved hotkey as the app names it on this keyboard; a new one as you pressed it.
        const shown = !value ? L('Choose a key', 'Escolher tecla', 'Elegir tecla')
          : value === (s.saved && s.saved[field.key])
            ? ((s.hotkeyLabels || {})[field.key] || value)
            : ((s.recordedLabels || {})[field.key] || value);
        const b = el('button', 'act hotkey', shown);
        b.type = 'button';
        b.setAttribute('aria-label', `${field.label}: ${shown}`);
        b.addEventListener('click', () => listen(b, field));
        // Listening ends when focus leaves the field, so a key pressed elsewhere never becomes the hotkey.
        b.addEventListener('blur', () => stopListening());
        // The form was just rebuilt under the keyboard: its place stays on this field.
        if (s.refocus === field.key) {
          s.refocus = null;
          queueMicrotask(() => b.focus());
        }
        if (!field.optional || !value) return b;
        // An optional hotkey can go back to taking no key at all.
        const remove = el('button', 'act small', L('Remove', 'Remover', 'Quitar'));
        remove.type = 'button';
        remove.setAttribute('aria-label', `${L('Remove', 'Remover', 'Quitar')}: ${field.label}`);
        remove.addEventListener('click', () => { s.draft[field.key] = ''; s.refocus = field.key; changed(); render(); });
        return [b, remove];
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

  // Mirrors HotkeyRules.TakesCommonShortcut: as a hotkey, Ctrl+C would stop copying in every program.
  const COMMON = new Set(['Ctrl+A', 'Ctrl+C', 'Ctrl+V', 'Ctrl+X', 'Ctrl+Z', 'Ctrl+Y', 'Ctrl+S', 'Ctrl+F', 'Ctrl+P',
    'Ctrl+N', 'Ctrl+T', 'Ctrl+W', 'Ctrl+R', 'Ctrl+Shift+Z', 'Ctrl+Shift+T', 'Alt+F4', 'Alt+Tab']);

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

  function listen(button, field) {
    stopListening();
    s.listening = { button, key: field.key, label: field.label, before: button.textContent };
    button.classList.add('listening');
    button.textContent = L('Press keys…', 'Aperte as teclas…', 'Pulsa las teclas…');
    // The tray lets go of its hotkeys meanwhile: one it holds would run its action and never get here.
    post({ type: 'hotkeyCaptureStart' });
  }

  function stopListening(text) {
    if (!s.listening) return;
    const { button, label, before } = s.listening;
    button.classList.remove('listening');
    button.textContent = text || before;
    button.setAttribute('aria-label', `${label}: ${button.textContent}`);
    s.listening = null;
    post({ type: 'hotkeyCaptureEnd' });
  }

  document.addEventListener('keydown', e => {
    if (!s.listening) return;
    e.preventDefault();
    e.stopPropagation();
    if (e.key === 'Escape') { stopListening(); return; }
    const main = keyName(e);
    if (!main) return; // only modifiers so far
    if (!e.ctrlKey && !e.altKey && !e.shiftKey && !e.metaKey && !ALONE.test(main)) {
      s.listening.button.textContent = L('Add Ctrl, Alt or Shift', 'Adicione Ctrl, Alt ou Shift', 'Añade Ctrl, Alt o Shift');
      return;
    }
    const mods = [e.ctrlKey && 'Ctrl', e.altKey && 'Alt', e.shiftKey && 'Shift', e.metaKey && 'Win'].filter(Boolean);
    const combo = [...mods, main].join('+');
    if (COMMON.has(combo)) {
      s.listening.button.textContent = L('Programs use that one: pick another', 'Os programas usam essa: escolha outra', 'Los programas usan esa: elige otra');
      return;
    }
    // What the key prints ("´", "ç"), for people; the stored name is for Windows.
    const printed = e.key.length === 1 && e.key !== ' '
      ? (e.code.startsWith('Numpad') ? `Num ${e.key}` : e.key.toUpperCase())
      : main;
    const label = [...mods, printed].join('+');
    s.recordedLabels = { ...s.recordedLabels, [s.listening.key]: label };
    // An optional hotkey that had no key gains its Remove button.
    const first = !s.draft[s.listening.key];
    if (first) s.refocus = s.listening.key;
    s.draft[s.listening.key] = combo;
    stopListening(label);
    changed();
    if (first) render();
  }, true);

  // Profiles

  function renderProfiles(card) {
    const box = el('div', 'profiles');
    const list = s.draft.profiles || (s.draft.profiles = []);
    if (list.length === 0) {
      box.append(el('div', 'empty-row', L('No game profiles. A profile changes clip length, sound or the microphone for one game.',
        'Nenhum perfil de jogo. Um perfil muda a duração do clipe, o som ou o microfone de um jogo.', 'Sin perfiles de juego. Un perfil cambia la duración del clip, el sonido o el micrófono para un juego.')));
    } else {
      const head = el('div', 'profile profile-head');
      head.append(el('span', '', L('GAME', 'JOGO', 'JUEGO')), el('span', '', L('LENGTH (S)', 'DURAÇÃO (S)', 'DURACIÓN (S)')), el('span', '', L('SOUND', 'SOM', 'SONIDO')),
        el('span', '', L('MICROPHONE', 'MICROFONE', 'MICRÓFONO')), el('span'));
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
      game.placeholder = L('Game name', 'Nome do jogo', 'Nombre del juego');
      game.setAttribute('list', 'games-list');
      game.setAttribute('aria-label', L('Game', 'Jogo', 'Juego'));
      game.addEventListener('input', () => { p.game = game.value; changed(); });

      const length = el('input', 'field');
      length.type = 'number';
      length.min = 10;
      length.max = 600;
      length.placeholder = String(s.draft.clipSeconds);
      length.value = p.clipSeconds ?? '';
      length.setAttribute('aria-label', L('Clip length', 'Duração do clipe', 'Duración del clip'));
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
      sound.setAttribute('aria-label', L('Sound', 'Som', 'Sonido'));
      options(sound, [['', L('Default', 'Padrão', 'Predeterminada')], ['System', L('Everything', 'Tudo', 'Todo')], ['GameOnly', L('Game only', 'Só o jogo', 'Solo el juego')]], p.audioSource || '');
      sound.addEventListener('change', () => {
        if (sound.value === '') delete p.audioSource; else p.audioSource = sound.value;
        changed();
      });

      const mic = el('select', 'field');
      mic.setAttribute('aria-label', L('Microphone', 'Microfone', 'Micrófono'));
      options(mic, [['', L('Default', 'Padrão', 'Predeterminada')], ['true', L('Muted', 'Mudo', 'Silenciado')], ['false', L('Not muted', 'Ativo', 'Activo')]], p.muteMicrophone === undefined || p.muteMicrophone === null ? '' : String(p.muteMicrophone));
      mic.addEventListener('change', () => {
        if (mic.value === '') delete p.muteMicrophone; else p.muteMicrophone = mic.value === 'true';
        changed();
      });

      const remove = el('button', 'icon');
      remove.type = 'button';
      remove.setAttribute('aria-label', L(`Remove ${p.game || 'profile'}`, `Remover ${p.game || 'perfil'}`, `Quitar ${p.game || 'perfil'}`));
      remove.append(icon('<path d="M6 6l12 12M18 6L6 18"/>'));
      remove.addEventListener('click', () => { list.splice(i, 1); changed(); render(); });

      line.append(game, length, sound, mic, remove);
      box.append(line);
    });
    const add = el('button', 'act small', L('Add a game', 'Adicionar um jogo', 'Añadir un juego'));
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
      form.append(el('p', 'muted', s.unreadable || L('Loading…', 'Carregando…', 'Cargando…')));
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
        more.append(el('summary', '', L('Encoder options', 'Opções do encoder', 'Opciones del encoder')));
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
    let text = L('NOT RUNNING', 'DESLIGADO', 'APAGADO');
    let kind = '';
    if (status) {
      switch (status.state) {
        case 'Recording':
          text = ['REC', status.encoder, status.fps ? `${status.fps} FPS` : null].filter(Boolean).join(' · ');
          kind = 'rec';
          break;
        case 'WaitingForDesktop': text = L('PAUSED · SCREEN LOCKED', 'PAUSADO · TELA BLOQUEADA', 'PAUSADO · PANTALLA BLOQUEADA'); kind = 'wait'; break;
        case 'Paused': text = L('PAUSED', 'PAUSADO', 'PAUSADO'); kind = 'wait'; break;
        case 'Probing': text = L('DETECTING ENCODER', 'DETECTANDO ENCODER', 'DETECTANDO ENCODER'); kind = 'wait'; break;
        case 'Stopped': text = L('NOT RECORDING', 'SEM GRAVAR', 'SIN GRABAR'); break;
        default: text = L('STARTING', 'INICIANDO', 'INICIANDO'); kind = 'wait'; break;
      }
    }
    box.className = `badge mono ${kind}`;
    $('badge-text').textContent = text;
    // Why it isn't recording, where people look: the tray's balloon about it is long gone.
    const problem = status && status.state === 'Stopped' && status.problem;
    $('problem').hidden = !problem;
    $('problem-text').textContent = problem || '';
    // A newer Ghostclip the tray has downloaded: offered here with a button, not only in its menu.
    const update = status && status.updateVersion;
    $('update').hidden = !update;
    $('update-text').textContent = update
      ? L(`Ghostclip ${update} is ready to install. It restarts in a few seconds; recording picks up again.`,
        `O Ghostclip ${update} está pronto para instalar. Ele reinicia em alguns segundos e volta a gravar.`, `Ghostclip ${update} está listo para instalarse. Se reinicia en unos segundos y vuelve a grabar.`)
      : '';
  }

  $('update-now').addEventListener('click', () => {
    const b = $('update-now');
    if (b.classList.contains('busy')) return;
    b.classList.add('busy');
    b.textContent = L('Updating…', 'Atualizando…', 'Actualizando…');
    post({ type: 'update' });
  });
  $('update-notes').addEventListener('click', () => {
    if (s.status && s.status.updateVersion) post({ type: 'releaseNotes', name: s.status.updateVersion });
  });

  // The page's language changed (or the page just opened): the badge and the form are built again,
  // keeping any edits in progress.
  function translate() {
    if (s.status === undefined) $('badge-text').textContent = L('CHECKING', 'VERIFICANDO', 'VERIFICANDO');
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
      if (!name) return L('Give each game profile a name, or remove it.', 'Dê um nome a cada perfil de jogo ou remova-o.', 'Dale un nombre a cada perfil de juego o quítalo.');
      if (seen.has(name)) return L(`There are two profiles for ${p.game.trim()}. Keep one.`, `Há dois perfis para ${p.game.trim()}. Mantenha um.`, `Hay dos perfiles para ${p.game.trim()}. Conserva uno.`);
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
        s.monitors = message.monitors || null;
        s.storageBytes = message.storageBytes;
        s.games = message.games || [];
        s.unreadable = null;
        badge(message.status);
        // Edits in progress win over a refresh that arrives late.
        if (dirty()) return true;
        s.saved = message.config;
        s.draft = clone(message.config);
        s.hotkeyLabels = message.hotkeyLabels || {};
        s.recordedLabels = {};
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
        if (message.hotkeyLabels && message.hotkeyLabels.hotkey) state.saveKey = message.hotkeyLabels.hotkey;
        s.saved = message.config;
        s.draft = clone(message.config);
        s.hotkeyLabels = message.hotkeyLabels || {};
        s.recordedLabels = {};
        s.confirming = false;
        render();
        toast(L('Settings saved', 'Configurações salvas', 'Configuración guardada'));
        return true;
      case 'error':
        if (message.op === 'update') {
          // The page's toast says why; the button can be tried again.
          $('update-now').classList.remove('busy');
          $('update-now').textContent = L('Update now', 'Atualizar agora', 'Actualizar ahora');
          return false;
        }
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
        toast(message.message || L('Settings not saved.', 'Configurações não salvas.', 'Configuración no guardada.'), true);
        return true;
    }
    return false;
  }

  return { open, onMessage, translate, isOpen: () => !$('settings').hidden, stopListening };
})();
