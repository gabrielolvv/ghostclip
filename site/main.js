'use strict';

const repo = 'gabrielolvv/ghostclip';

// English is in the HTML, so the page reads fine without this script.
// Portuguese replaces each [data-i18n] element's content and each [data-i18n-alt] image's alt text.
const pt = {
  'title': 'Ghostclip · Replay instantâneo para Windows que não atrapalha o seu jogo',
  'description': 'Replay instantâneo grátis e de código aberto para Windows 10 e 11. Uma tecla salva o último minuto do jogo, codificado na GPU, sem nada injetado no jogo.',
  'nav.features': 'Recursos',
  'nav.numbers': 'Números',
  'nav.faq': 'Perguntas',
  'nav.star': 'Estrela no GitHub',
  'hero.kicker': 'GRÁTIS · CÓDIGO ABERTO · WINDOWS 10 / 11',
  'hero.title': 'Replay instantâneo que não <br>atrapalha o seu jogo.',
  'hero.lead': 'Aperte <kbd>Alt</kbd>+<kbd>F10</kbd> e o último minuto do jogo vira um clipe. Capturado e codificado na sua GPU, sem nada injetado no jogo, sem conta e sem anúncios.',
  'hero.notWindows': 'O Ghostclip roda no Windows. Abra esta página no PC em que você joga para baixar.',
  'cta.download': 'Baixar para Windows',
  'cta.source': 'Código no GitHub',
  'img.library': 'A biblioteca do Ghostclip: clipes e capturas de tela por dia, com miniaturas, durações, favoritos marcados com estrela e uma sessão com três marcadores; a barra lateral lista os jogos, os favoritos e o selo de gravação.',
  'img.clip': 'Uma sessão aberta na biblioteca: player de vídeo, linha do tempo sobre a forma de onda do áudio com três marcadores, e botões para salvar um corte, comprimir para o Discord, copiar, favoritar, renomear, mostrar na pasta e apagar.',
  'features.label': 'O QUE ELE FAZ',
  'f1.title': 'O último minuto a uma tecla de distância',
  'f1.text': 'O Ghostclip mantém um buffer contínuo da tela e do som. A hotkey salva esse trecho como MP4 numa pasta por jogo: seguro em milissegundos, gravado em menos de dois segundos, sem recodificar.',
  'f2.title': 'Só o som do jogo',
  'f2.text': 'Grave tudo o que você ouve ou só o jogo: sem a chamada do Discord, sem música. Ele segue o jogo que você está jogando, não a janela que estiver na frente.',
  'f3.title': 'Microfone e Discord em faixas separadas',
  'f3.text': 'O microfone vai numa faixa de áudio própria, para você abaixar a sua voz depois ou desligá-lo por jogo com um perfil. O som do app do Discord também pode ganhar uma faixa só dele, para silenciar a chamada depois.',
  'f4.title': 'Uma biblioteca para achar a melhor parte',
  'f4.text': 'Todos os clipes e capturas de tela por jogo, com miniaturas, player, favoritos, renomear e ações em vários de uma vez. Corte arrastando sobre a forma de onda do áudio: o corte começa num keyframe, então o que você vê é o que sai.',
  'f5.title': 'Pronto para o Discord',
  'f5.text': 'Comprima qualquer clipe para caber no limite de upload do Discord, na GPU, com jogo e microfone mixados. Ou copie o arquivo e cole direto no chat.',
  'f6.title': 'Some quando você não precisa',
  'f6.text': 'A biblioteca é um processo à parte e só existe enquanto a janela está aberta. Em segundo plano ficam só o ícone na bandeja e o gravador.',
  'f7.title': 'Sessões inteiras e capturas de tela',
  'f7.text': '<kbd>Alt</kbd>+<kbd>F9</kbd> grava do início ao fim, e a tecla de clipe deixa marcadores na linha do tempo. Ele aproveita o buffer do replay em vez de rodar um segundo encoder, então o jogo não paga nada a mais. <kbd>Alt</kbd>+<kbd>F11</kbd> salva uma captura de tela junto dos clipes.',
  'f8.title': 'Uma pasta que não enche o disco',
  'f8.text': 'Defina um limite de espaço e, passando dele, os clipes e capturas mais antigos são apagados depois de cada gravação. Favoritos, clipes que você renomeou e arquivos que não são do Ghostclip nunca são tocados. Vem desligado.',
  'f10.title': 'Pronto para Shorts, TikTok e Reels',
  'f10.text': 'Faça uma cópia vertical do meio da imagem em 1080x1920, codificada na GPU. Ou junte vários clipes num vídeo só para um compilado, sem recodificar.',
  'f11.title': 'Uma pausa para o que é privado',
  'f11.text': 'Vai abrir o banco ou digitar uma senha? Pause pela bandeja ou por uma tecla que você escolher: o buffer de replay é apagado, e nada é gravado até você retomar. Uma sessão em andamento é salva antes, e o Ghostclip volta a gravar na próxima vez que abrir.',
  'f12.title': 'Do seu jeito',
  'f12.text': 'Uma segunda tecla para um clipe mais curto, <kbd>Alt</kbd>+<kbd>F8</kbd> para mutar o microfone enquanto joga, comandos para Stream Deck e macros, e o buffer de replay no disco que você escolher, para poupar o SSD do sistema.',
  'f9.title': 'Em inglês, português ou espanhol',
  'f9.text': 'A bandeja, as notificações, a biblioteca e as configurações seguem o idioma do Windows, ou o que você escolher, e trocam assim que você salva, sem reiniciar a gravação.',
  'points.label': 'FEITO PARA NÃO CUSTAR NADA AO SEU JOGO',
  'points.1': '<strong>Sem injeção.</strong> Nenhuma DLL no jogo, nenhum hook de renderização, nenhuma leitura de memória, nenhum hook global de teclado. A captura usa o Desktop Duplication ou o Windows Graphics Capture do próprio Windows.',
  'points.2': '<strong>Só GPU.</strong> NVENC, AMF ou QuickSync fazem a codificação. Não existe encoder de CPU como alternativa.',
  'points.3': '<strong>Pequeno de propósito.</strong> Um build mínimo e LGPL do FFmpeg, só com as partes que o Ghostclip usa: 10 MB em vez de 180.',
  'points.4': '<strong>Atualizações que esperam você.</strong> Versões novas baixam em silêncio e instalam quando você sai do Ghostclip ou na próxima vez que ele abre, nunca no meio do jogo.',
  'numbers.label': 'MEDIDO',
  'numbers.1': 'até o clipe estar seguro depois da hotkey',
  'numbers.2v': '~1,3 s',
  'numbers.2': 'para gravar o MP4, sem recodificar',
  'numbers.3v': '~1,3 %',
  'numbers.3': 'de CPU gravando em 1080p60',
  'numbers.4': 'processos sobrando quando a biblioteca fecha',
  'numbers.fine': 'Num notebook i7-13620H com gráficos Intel UHD, 1080p60 a 20 Mbps, área de trabalho parada. Benchmarks em jogos, com FPS e 1% low, estão a caminho; o script que gera esses números está no repositório.',
  'faq.label': 'PERGUNTAS',
  'faq.1q': 'O Windows diz "O Windows protegeu o computador". É seguro?',
  'faq.1a': 'O Ghostclip ainda não tem assinatura de código, então o SmartScreen não o conhece. Clique em <em>Mais informações</em> e depois em <em>Executar assim mesmo</em>. O instalador é gerado em público pelo GitHub Actions a partir do código que você pode ler, e a assinatura está a caminho.',
  'faq.2q': 'O anti-cheat vai implicar com ele?',
  'faq.2a': 'O Ghostclip nunca toca no processo do jogo: sem injeção, sem hooks, sem acesso à memória. Ele grava a área de trabalho do mesmo jeito que a captura de tela do próprio Windows. É o tipo de gravador que os anti-cheats deixam em paz, mas nenhuma ferramenta pode garantir o que um jogo específico vai fazer.',
  'faq.3q': 'Do que eu preciso?',
  'faq.3a': 'Windows 10 2004 ou mais novo, ou Windows 11, e uma GPU com encoder de hardware: NVIDIA (NVENC), AMD (AMF) ou Intel (QuickSync). O instalador traz todo o resto, inclusive o .NET e o FFmpeg, e não pede permissão de administrador.',
  'faq.7q': 'Meu jogo perde FPS enquanto grava. O que eu faço?',
  'faq.7a': 'Tente, um de cada vez: deixe a <em>Prioridade para os jogos</em> ligada (Configurações &gt; Avançado), troque a <em>Captura</em> para Windows Graphics Capture, grave a 30 FPS ou grave em 1080p ou 720p (Configurações &gt; Vídeo). Depois aperte <em>Copiar diagnóstico</em> e cole num <a href="https://github.com/gabrielolvv/ghostclip/issues/new?template=fps_report.yml">relato de FPS</a> com o seu FPS com o Ghostclip desligado e ligado. <a href="https://github.com/gabrielolvv/ghostclip/blob/main/README.pt-BR.md#se-o-jogo-perder-fps">Mais no README</a>.',
  'faq.4q': 'Para onde vão os clipes e as capturas de tela?',
  'faq.4a': 'Para <code>Vídeos\\Ghostclip\\&lt;Jogo&gt;</code>, uma pasta por jogo, com as capturas junto dos clipes. Dá para escolher outra pasta nas Configurações e definir ali um limite de espaço, para que os clipes mais antigos abram lugar para os novos.',
  'faq.5q': 'Ele manda dados para algum lugar?',
  'faq.5a': 'O gravador não usa a rede. O app da bandeja consulta o GitHub Releases atrás de atualizações, e só. Não tem conta, telemetria nem anúncios.',
  'faq.6q': 'Como eu desinstalo?',
  'faq.6a': 'Configurações &gt; Aplicativos &gt; Aplicativos instalados &gt; Ghostclip &gt; Desinstalar. Isso remove o app, as configurações dele e o buffer; seus clipes continuam na pasta Vídeos.',
  'bottom.title': 'Pegue o próximo grande momento.',
  'bottom.releases': 'Todas as versões e notas',
  'footer.license': 'O Ghostclip é software livre sob a <a href="https://github.com/gabrielolvv/ghostclip/blob/main/LICENSE">licença MIT</a>. Ele inclui um build mínimo do <a href="https://ffmpeg.org">FFmpeg</a> sob a LGPL, feito por <a href="https://github.com/gabrielolvv/ghostclip/tree/main/build/ffmpeg">build/ffmpeg</a>.',
  'footer.links': '<a href="https://github.com/gabrielolvv/ghostclip">GitHub</a> · <a href="https://github.com/gabrielolvv/ghostclip/issues">Relatar um bug</a> · <a href="https://github.com/gabrielolvv/ghostclip/blob/main/README.pt-BR.md">README em português</a>',
};

const es = {
  'title': 'Ghostclip · Repetición instantánea para Windows que no estorba en tu juego',
  'description': 'Repetición instantánea gratis y de código abierto para Windows 10 y 11. Una tecla guarda el último minuto del juego, codificado en la GPU, sin inyectar nada en el juego.',
  'nav.features': 'Funciones',
  'nav.numbers': 'Números',
  'nav.faq': 'Preguntas',
  'nav.star': 'Estrella en GitHub',
  'hero.kicker': 'GRATIS · CÓDIGO ABIERTO · WINDOWS 10 / 11',
  'hero.title': 'Repetición instantánea que no <br>estorba en tu juego.',
  'hero.lead': 'Pulsa <kbd>Alt</kbd>+<kbd>F10</kbd> y el último minuto del juego se vuelve un clip. Capturado y codificado en tu GPU, sin inyectar nada en el juego, sin cuenta y sin anuncios.',
  'hero.notWindows': 'Ghostclip funciona en Windows. Abre esta página en el PC donde juegas para descargarlo.',
  'cta.download': 'Descargar para Windows',
  'cta.source': 'Código en GitHub',
  'img.library': 'La biblioteca de Ghostclip: clips y capturas de pantalla por día, con miniaturas, duraciones, favoritos marcados con una estrella y una sesión con tres marcadores; la barra lateral lista los juegos, los favoritos y el indicador de grabación.',
  'img.clip': 'Una sesión abierta en la biblioteca: reproductor de video, línea de tiempo sobre la forma de onda del audio con tres marcadores, y botones para guardar un recorte, comprimir para Discord, copiar, marcar como favorito, renombrar, mostrar en la carpeta y eliminar.',
  'features.label': 'QUÉ HACE',
  'f1.title': 'El último minuto a una tecla de distancia',
  'f1.text': 'Ghostclip mantiene un búfer continuo de la pantalla y del sonido. La tecla guarda ese tramo como MP4 en una carpeta por juego: a salvo en milisegundos, escrito en menos de dos segundos, sin recodificar.',
  'f2.title': 'Solo el sonido del juego',
  'f2.text': 'Graba todo lo que oyes o solo el juego: sin la llamada de Discord, sin música. Sigue al juego que estás jugando, no a la ventana que esté al frente.',
  'f3.title': 'Micrófono y Discord en pistas separadas',
  'f3.text': 'El micrófono va en una pista de audio propia, para que bajes tu voz después o lo desactives por juego con un perfil. El sonido de la app de Discord también puede tener una pista solo suya, para silenciar la llamada después.',
  'f4.title': 'Una biblioteca para encontrar la mejor parte',
  'f4.text': 'Todos los clips y capturas por juego, con miniaturas, reproductor, favoritos, renombrar y acciones sobre varios a la vez. Recorta arrastrando sobre la forma de onda del audio: el recorte empieza en un keyframe, así que lo que ves es lo que sale.',
  'f5.title': 'Listo para Discord',
  'f5.text': 'Comprime cualquier clip para que quepa en el límite de subida de Discord, en la GPU, con juego y micrófono mezclados. O copia el archivo y pégalo directo en el chat.',
  'f6.title': 'Desaparece cuando no lo necesitas',
  'f6.text': 'La biblioteca es un proceso aparte y solo existe mientras la ventana está abierta. En segundo plano quedan solo el ícono de la bandeja y el grabador.',
  'f7.title': 'Sesiones completas y capturas de pantalla',
  'f7.text': '<kbd>Alt</kbd>+<kbd>F9</kbd> graba de principio a fin, y la tecla de clip deja marcadores en la línea de tiempo. Aprovecha el búfer de repetición en lugar de correr un segundo encoder, así que el juego no paga nada extra. <kbd>Alt</kbd>+<kbd>F11</kbd> guarda una captura de pantalla junto a los clips.',
  'f8.title': 'Una carpeta que no llena el disco',
  'f8.text': 'Define un límite de espacio y, al pasarlo, los clips y capturas más antiguos se eliminan después de cada grabación. Los favoritos, los clips que renombraste y los archivos que no son de Ghostclip nunca se tocan. Viene desactivado.',
  'f10.title': 'Listo para Shorts, TikTok y Reels',
  'f10.text': 'Haz una copia vertical del centro de la imagen en 1080x1920, codificada en la GPU. O une varios clips en un solo video para un compilado, sin recodificar.',
  'f11.title': 'Una pausa para lo privado',
  'f11.text': '¿Vas a abrir el banco o escribir una contraseña? Pausa desde la bandeja o con una tecla que elijas: el búfer de repetición se borra y no se graba nada hasta que reanudes. Una sesión en curso se guarda antes, y Ghostclip vuelve a grabar la próxima vez que se abra.',
  'f12.title': 'A tu manera',
  'f12.text': 'Una segunda tecla para un clip más corto, <kbd>Alt</kbd>+<kbd>F8</kbd> para silenciar el micrófono mientras juegas, comandos para Stream Deck y macros, y el búfer de repetición en el disco que elijas, para cuidar el SSD del sistema.',
  'f9.title': 'En inglés, portugués o español',
  'f9.text': 'La bandeja, las notificaciones, la biblioteca y la configuración siguen el idioma de Windows, o el que elijas, y cambian en cuanto guardas, sin reiniciar la grabación.',
  'points.label': 'HECHO PARA NO COSTARLE NADA A TU JUEGO',
  'points.1': '<strong>Sin inyección.</strong> Ninguna DLL en el juego, ningún hook de renderizado, ninguna lectura de memoria, ningún hook global de teclado. La captura usa el Desktop Duplication o el Windows Graphics Capture del propio Windows.',
  'points.2': '<strong>Solo GPU.</strong> NVENC, AMF o QuickSync hacen la codificación. No existe un encoder de CPU como alternativa.',
  'points.3': '<strong>Pequeño a propósito.</strong> Una compilación mínima y LGPL de FFmpeg, solo con las partes que usa Ghostclip: 10 MB en lugar de 180.',
  'points.4': '<strong>Actualizaciones que te esperan.</strong> Las versiones nuevas se descargan en silencio y se instalan cuando sales de Ghostclip o la próxima vez que se abre, nunca en mitad del juego.',
  'numbers.label': 'MEDIDO',
  'numbers.1': 'hasta que el clip está a salvo después de la tecla',
  'numbers.2v': '~1,3 s',
  'numbers.2': 'para escribir el MP4, sin recodificar',
  'numbers.3v': '~1,3 %',
  'numbers.3': 'de CPU grabando en 1080p60',
  'numbers.4': 'procesos sobrantes cuando se cierra la biblioteca',
  'numbers.fine': 'En una laptop i7-13620H con gráficos Intel UHD, 1080p60 a 20 Mbps, escritorio quieto. Los benchmarks en juegos, con FPS y 1% low, vienen en camino; el script que genera estos números está en el repositorio.',
  'faq.label': 'PREGUNTAS',
  'faq.1q': 'Windows dice "Windows protegió su PC". ¿Es seguro?',
  'faq.1a': 'Ghostclip todavía no tiene firma de código, así que SmartScreen no lo conoce. Haz clic en <em>Más información</em> y luego en <em>Ejecutar de todas formas</em>. El instalador se genera en público con GitHub Actions a partir del código que puedes leer, y la firma está en camino.',
  'faq.2q': '¿El anti-cheat se va a meter con él?',
  'faq.2a': 'Ghostclip nunca toca el proceso del juego: sin inyección, sin hooks, sin acceso a la memoria. Graba el escritorio igual que la captura de pantalla del propio Windows. Es el tipo de grabador que los anti-cheat dejan en paz, pero ninguna herramienta puede garantizar lo que hará un juego en particular.',
  'faq.3q': '¿Qué necesito?',
  'faq.3a': 'Windows 10 2004 o más reciente, o Windows 11, y una GPU con encoder de hardware: NVIDIA (NVENC), AMD (AMF) o Intel (QuickSync). El instalador trae todo lo demás, incluidos .NET y FFmpeg, y no pide permisos de administrador.',
  'faq.7q': 'Mi juego pierde FPS mientras graba. ¿Qué hago?',
  'faq.7a': 'Prueba, de una en una: deja activada la <em>Prioridad a los juegos</em> (Configuración &gt; Avanzado), cambia la <em>Captura</em> a Windows Graphics Capture, graba a 30 FPS o graba en 1080p o 720p (Configuración &gt; Video). Después pulsa <em>Copiar diagnóstico</em> y pégalo en un <a href="https://github.com/gabrielolvv/ghostclip/issues/new?template=fps_report.yml">reporte de FPS</a> con tus FPS con Ghostclip apagado y encendido. <a href="https://github.com/gabrielolvv/ghostclip/blob/main/README.pt-BR.md#se-o-jogo-perder-fps">Más en el README</a>.',
  'faq.4q': '¿A dónde van los clips y las capturas de pantalla?',
  'faq.4a': 'A <code>Videos\\Ghostclip\\&lt;Juego&gt;</code>, una carpeta por juego, con las capturas junto a los clips. Puedes elegir otra carpeta en Configuración y definir ahí un límite de espacio, para que los clips más antiguos dejen lugar a los nuevos.',
  'faq.5q': '¿Envía datos a algún lado?',
  'faq.5a': 'El grabador no usa la red. La app de la bandeja consulta GitHub Releases en busca de actualizaciones, y nada más. No hay cuenta, telemetría ni anuncios.',
  'faq.6q': '¿Cómo lo desinstalo?',
  'faq.6a': 'Configuración &gt; Aplicaciones &gt; Aplicaciones instaladas &gt; Ghostclip &gt; Desinstalar. Eso elimina la app, su configuración y el búfer; tus clips siguen en la carpeta Videos.',
  'bottom.title': 'Atrapa el próximo gran momento.',
  'bottom.releases': 'Todas las versiones y notas',
  'footer.license': 'Ghostclip es software libre bajo la <a href="https://github.com/gabrielolvv/ghostclip/blob/main/LICENSE">licencia MIT</a>. Incluye una compilación mínima de <a href="https://ffmpeg.org">FFmpeg</a> bajo la LGPL, hecha por <a href="https://github.com/gabrielolvv/ghostclip/tree/main/build/ffmpeg">build/ffmpeg</a>.',
  'footer.links': '<a href="https://github.com/gabrielolvv/ghostclip">GitHub</a> · <a href="https://github.com/gabrielolvv/ghostclip/issues">Reportar un bug</a> · <a href="https://github.com/gabrielolvv/ghostclip/blob/main/README.pt-BR.md">README en portugués</a>',
};

const releaseText = {
  en: { version: 'Version', latest: 'Latest release', windows: 'Windows 10 2004+', gpu: 'NVIDIA, AMD or Intel GPU' },
  pt: { version: 'Versão', latest: 'Última versão', windows: 'Windows 10 2004+', gpu: 'GPU NVIDIA, AMD ou Intel' },
  es: { version: 'Versión', latest: 'Última versión', windows: 'Windows 10 2004+', gpu: 'GPU NVIDIA, AMD o Intel' },
};

const en = {};
let lang = 'en';
let release = null;

function store(key, value) {
  try { if (value === undefined) return localStorage.getItem(key); localStorage.setItem(key, value); } catch { return null; }
  return null;
}

function renderRelease() {
  const t = releaseText[lang];
  const parts = release ? [`${t.version} ${release.version}`] : [t.latest];
  if (release && release.size) parts.push(`${Math.round(release.size / 1024 / 1024)} MB`);
  parts.push(t.windows, t.gpu);
  document.getElementById('release').textContent = parts.join(' · ');
}

const translations = { pt, es };

function setLanguage(next) {
  lang = next === 'pt' || next === 'es' ? next : 'en';
  const texts = translations[lang] ?? {};
  document.documentElement.lang = lang === 'pt' ? 'pt-BR' : lang;
  for (const el of document.querySelectorAll('[data-i18n]')) {
    const key = el.dataset.i18n;
    if (!(key in en)) en[key] = el.innerHTML;
    el.innerHTML = texts[key] ?? en[key];
  }
  for (const el of document.querySelectorAll('[data-i18n-alt]')) {
    const key = el.dataset.i18nAlt;
    if (!(key in en)) en[key] = el.alt;
    el.alt = texts[key] ?? en[key];
  }
  for (const el of document.querySelectorAll('[data-i18n-label]')) {
    el.setAttribute('aria-label', texts[el.dataset.i18nLabel] ?? 'Star on GitHub');
  }
  if (!('title' in en)) {
    en.title = document.title;
    en.description = document.querySelector('meta[name="description"]').content;
  }
  document.title = texts.title ?? en.title;
  document.querySelector('meta[name="description"]').content = texts.description ?? en.description;
  for (const button of document.querySelectorAll('#lang button')) {
    button.setAttribute('aria-pressed', String(button.dataset.lang === lang));
  }
  renderRelease();
}

// English unless the visitor picked Portuguese here before, or the link asks for it (?lang=pt).
const asked = new URLSearchParams(location.search).get('lang');
document.getElementById('lang').hidden = false;
for (const button of document.querySelectorAll('#lang button')) {
  button.addEventListener('click', () => {
    setLanguage(button.dataset.lang);
    store('lang', lang);
    const url = new URL(location.href);
    if (lang === 'en') url.searchParams.delete('lang'); else url.searchParams.set('lang', lang);
    history.replaceState(null, '', url);
  });
}
setLanguage(asked === 'pt' || asked === 'es' || asked === 'en' ? asked : store('lang'));

// The latest version, the installer size and the star count. The download link
// always points at the newest release, so the page works without them.
fetch(`https://api.github.com/repos/${repo}/releases/latest`)
  .then(r => (r.ok ? r.json() : Promise.reject(r.status)))
  .then(data => {
    const setup = (data.assets || []).find(a => a.name === 'Ghostclip-win-Setup.exe');
    release = { version: data.tag_name.replace(/^v/, ''), size: setup ? setup.size : 0 };
    renderRelease();
  })
  .catch(() => {});

fetch(`https://api.github.com/repos/${repo}`)
  .then(r => (r.ok ? r.json() : Promise.reject(r.status)))
  .then(info => {
    if (info.stargazers_count > 0) {
      document.getElementById('stars').textContent = info.stargazers_count.toLocaleString('en-US');
    }
  })
  .catch(() => {});

// Not on Windows (a phone, a Mac): say so instead of offering a file that won't run there.
const platform = (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || '';
if (platform && !/win/i.test(platform)) {
  document.getElementById('not-windows').hidden = false;
}
