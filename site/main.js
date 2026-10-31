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
  'img.library': 'A biblioteca do Ghostclip: clipes agrupados por jogo, com miniaturas, durações e o selo de gravação na barra lateral.',
  'img.clip': 'Um clipe aberto na biblioteca: player de vídeo, linha do tempo sobre a forma de onda do áudio com o trecho do corte em ciano, e botões para salvar o corte, comprimir para o Discord, copiar e apagar.',
  'features.label': 'O QUE ELE FAZ',
  'f1.title': 'O último minuto a uma tecla de distância',
  'f1.text': 'O Ghostclip mantém um buffer contínuo da tela e do som. A hotkey salva esse trecho como MP4 numa pasta por jogo: seguro em milissegundos, gravado em menos de dois segundos, sem recodificar.',
  'f2.title': 'Só o som do jogo',
  'f2.text': 'Grave tudo o que você ouve ou só o jogo: sem a chamada do Discord, sem música. Ele segue o jogo que você está jogando, não a janela que estiver na frente.',
  'f3.title': 'Seu microfone em faixa separada',
  'f3.text': 'O microfone vai numa faixa de áudio própria, para você abaixar a sua voz depois. Desligue-o por jogo com um perfil.',
  'f4.title': 'Uma biblioteca para achar a melhor parte',
  'f4.text': 'Todos os clipes por jogo, com miniaturas e player. Corte arrastando sobre a forma de onda do áudio: o corte começa num keyframe, então o que você vê é o que sai.',
  'f5.title': 'Pronto para o Discord',
  'f5.text': 'Comprima qualquer clipe para caber no limite de upload do Discord, na GPU, com jogo e microfone mixados. Ou copie o arquivo e cole direto no chat.',
  'f6.title': 'Some quando você não precisa',
  'f6.text': 'A biblioteca é um processo à parte e só existe enquanto a janela está aberta. Em segundo plano ficam só o ícone na bandeja e o gravador.',
  'points.label': 'FEITO PARA NÃO CUSTAR NADA AO SEU JOGO',
  'points.1': '<strong>Sem injeção.</strong> Nenhuma DLL no jogo, nenhum hook de renderização, nenhuma leitura de memória, nenhum hook global de teclado. A captura usa o Desktop Duplication do próprio Windows.',
  'points.2': '<strong>Só GPU.</strong> NVENC, AMF ou QuickSync fazem a codificação. Não existe encoder de CPU como alternativa.',
  'points.3': '<strong>Pequeno de propósito.</strong> Um build mínimo e LGPL do FFmpeg, só com as partes que o Ghostclip usa: 10 MB em vez de 180.',
  'points.4': '<strong>Atualizações que esperam você.</strong> Versões novas baixam em silêncio e instalam quando o Ghostclip fecha ou o PC reinicia, nunca no meio do jogo.',
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
  'faq.4q': 'Para onde vão os clipes?',
  'faq.4a': 'Para <code>Vídeos\\Ghostclip\\&lt;Jogo&gt;</code>, uma pasta por jogo. Dá para escolher outra pasta nas Configurações.',
  'faq.5q': 'Ele manda dados para algum lugar?',
  'faq.5a': 'O gravador não usa a rede. O app da bandeja consulta o GitHub Releases atrás de atualizações, e só. Não tem conta, telemetria nem anúncios.',
  'faq.6q': 'Como eu desinstalo?',
  'faq.6a': 'Configurações &gt; Aplicativos &gt; Aplicativos instalados &gt; Ghostclip &gt; Desinstalar. Isso remove o app, as configurações dele e o buffer; seus clipes continuam na pasta Vídeos.',
  'bottom.title': 'Pegue o próximo grande momento.',
  'bottom.releases': 'Todas as versões e notas',
  'footer.license': 'O Ghostclip é software livre sob a <a href="https://github.com/gabrielolvv/ghostclip/blob/main/LICENSE">licença MIT</a>. Ele inclui um build mínimo do <a href="https://ffmpeg.org">FFmpeg</a> sob a LGPL, feito por <a href="https://github.com/gabrielolvv/ghostclip/tree/main/build/ffmpeg">build/ffmpeg</a>.',
  'footer.links': '<a href="https://github.com/gabrielolvv/ghostclip">GitHub</a> · <a href="https://github.com/gabrielolvv/ghostclip/issues">Relatar um bug</a> · <a href="https://github.com/gabrielolvv/ghostclip/blob/main/README.pt-BR.md">README em português</a>',
};

const releaseText = {
  en: { version: 'Version', latest: 'Latest release', windows: 'Windows 10 2004+', gpu: 'NVIDIA, AMD or Intel GPU' },
  pt: { version: 'Versão', latest: 'Última versão', windows: 'Windows 10 2004+', gpu: 'GPU NVIDIA, AMD ou Intel' },
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

function setLanguage(next) {
  lang = next === 'pt' ? 'pt' : 'en';
  document.documentElement.lang = lang === 'pt' ? 'pt-BR' : 'en';
  for (const el of document.querySelectorAll('[data-i18n]')) {
    const key = el.dataset.i18n;
    if (!(key in en)) en[key] = el.innerHTML;
    el.innerHTML = lang === 'pt' ? pt[key] ?? en[key] : en[key];
  }
  for (const el of document.querySelectorAll('[data-i18n-alt]')) {
    const key = el.dataset.i18nAlt;
    if (!(key in en)) en[key] = el.alt;
    el.alt = lang === 'pt' ? pt[key] ?? en[key] : en[key];
  }
  for (const el of document.querySelectorAll('[data-i18n-label]')) {
    el.setAttribute('aria-label', lang === 'pt' ? pt[el.dataset.i18nLabel] : 'Star on GitHub');
  }
  if (!('title' in en)) {
    en.title = document.title;
    en.description = document.querySelector('meta[name="description"]').content;
  }
  document.title = lang === 'pt' ? pt.title : en.title;
  document.querySelector('meta[name="description"]').content = lang === 'pt' ? pt.description : en.description;
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
    if (lang === 'pt') url.searchParams.set('lang', 'pt'); else url.searchParams.delete('lang');
    history.replaceState(null, '', url);
  });
}
setLanguage(asked === 'pt' || asked === 'en' ? asked : store('lang'));

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
