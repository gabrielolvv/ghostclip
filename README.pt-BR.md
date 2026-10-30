# Ghostclip

[![CI](https://github.com/gabrielolvv/ghostclip/actions/workflows/ci.yml/badge.svg)](https://github.com/gabrielolvv/ghostclip/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

[English](README.md) · [Site](https://gabrielolvv.github.io/ghostclip/) · [Baixar para Windows](https://github.com/gabrielolvv/ghostclip/releases/latest/download/Ghostclip-win-Setup.exe)

Gravador de "instant replay" open-source para Windows 10/11. Fica na bandeja,
guarda o último minuto da tela, do áudio do jogo e do microfone, e salva um
clipe quando você aperta a hotkey. O objetivo nº 1 é tirar o mínimo possível
de FPS do jogo.

Nada roda dentro do jogo: sem injeção de DLL, sem hook de renderização, sem
leitura de memória e sem hook global de teclado. A captura usa o Desktop
Duplication, a hotkey usa `RegisterHotKey`, o áudio só do jogo usa o process
loopback do próprio Windows e a codificação roda no encoder de hardware da GPU
(NVENC, AMF ou QuickSync). Não existe fallback para encoder de CPU.

## Recursos

- **Instant replay** dos últimos 60 s (configurável) no Alt+F10, salvo em MP4
  numa pasta por jogo, sem reencode.
- **Áudio só do jogo.** Opcionalmente grava só o jogo: sem a chamada do
  Discord, sem música. Ele segue o jogo que você está jogando, não qualquer
  janela em primeiro plano.
- **Microfone em faixa separada,** para silenciar ou rebalancear depois.
- **Perfis por jogo** para duração do clipe, fonte de áudio e microfone mudo.
- **Biblioteca de clipes.** Todos os clipes por jogo, com miniaturas, player
  e linha do tempo sobre a forma de onda do áudio. Só roda enquanto a janela
  está aberta.
- **Corte** arrastando duas alças, sem reencode. O início encaixa num
  keyframe, então o resultado começa exatamente onde a linha do tempo mostra.
- **Comprimir para o Discord:** reencoda na GPU para caber no limite de upload
  (10 MB por padrão), juntando jogo e microfone numa faixa só.

## Instalar

Baixe o `Ghostclip-win-Setup.exe` do
[último release](https://github.com/gabrielolvv/ghostclip/releases/latest) e
execute. Ele instala só para o seu usuário, sem pedir admin, abre na bandeja e
se atualiza pelo GitHub Releases: quando uma versão nova é baixada, aparece o
item "Restart to update", sem reiniciar no meio de um jogo. Requer Windows 10
2004 ou mais novo e uma GPU com NVENC, AMF ou QuickSync. Todo o resto, inclusive
o .NET e o FFmpeg, vem junto.

## Estado

**0.5.** Funcionam gravação, save, roteamento de áudio, perfis, a biblioteca de
clipes com corte e compressão, a página de configurações e o instalador com
atualizações. Ainda falta: números de benchmark
em jogo, assinatura de código (veja [docs/releasing.md](docs/releasing.md), em
inglês) e um GIF para esta página (`assets\make-gif.ps1` gera um a partir de
um clipe). O plano completo está em [docs/spec.md](docs/spec.md) (em inglês).

## Como funciona

```
Ghostclip.exe (bandeja)           Ghostclip.Worker.exe (sem UI)          ffmpeg.exe
  hotkey, config, detecção     ⇄    supervisiona o gravador,        →     ddagrab → encoder da GPU
  do jogo, save, corte,             áudio do jogo/sistema ── pipe →       → segmentos MPEG-TS de 2 s
  compressão                        microfone             ── pipe →       %LOCALAPPDATA%\GhostclipData\Buffer
        named pipe (JSON)           rotação do buffer
```

- **Dois processos.** O worker cuida da captura e do áudio, sem UI e sem rede.
  Se ele travar ou parar de responder, o host o reinicia. O buffer está em
  disco, então nada se perde; um job object garante que o FFmpeg morra junto.
- **Os frames ficam na GPU.** Capturados na taxa do clipe (60 fps), não na do
  monitor, e convertidos e codificados na GPU.
- **Buffer.** Segmentos de 2 s, cada um começando num keyframe, numerados e
  rotacionados para manter em disco só o equivalente a um clipe.
- **Salvar.** A hotkey cria hard links dos segmentos numa pasta pendente
  (dezenas de milissegundos, seja qual for o disco) e o som toca: a partir daí
  o clipe está garantido. Depois eles viram um MP4 por stream copy, sem
  reencode, com prioridade abaixo do normal.
- **Áudio.** Cada faixa é uma captura WASAPI enviada ao FFmpeg por named pipe e
  completada com silêncio pelo relógio do QueryPerformanceCounter quando nada
  toca. O modo "só o jogo" troca a fonte (a mixagem inteira ou a árvore de um
  processo) no mesmo pipe, então trocar de jogo nunca reinicia a gravação. O
  FFmpeg começa o relógio do áudio uns 150 ms antes do primeiro frame de vídeo;
  o worker mede essa diferença a cada sessão e o save desloca o áudio de volta.

## Compilar do código-fonte

Requer Windows 10 2004+ e o SDK do .NET 10.

```powershell
.\tools\get-tools.ps1        # FFmpeg LGPL e PresentMon em tools\
dotnet build
.\artifacts\bin\Debug\Ghostclip.exe
```

Testes: `dotnet test --project tests/Ghostclip.Core.Tests`. Como gerar o
instalador e o FFmpeg mínimo está em [docs/releasing.md](docs/releasing.md).

## Uso

- **Alt+F10** salva em `Vídeos\Ghostclip\<Jogo>\<Jogo>_AAAA-MM-DD_HH-MM-SS.mp4`.
- Clique duas vezes no ícone da bandeja, ou numa notificação "Clip saved",
  para abrir a biblioteca. O menu da bandeja também mostra o que está sendo
  gravado e tem configurações e pastas. O ponto do ícone fica vermelho
  enquanto grava.
- Na biblioteca: copie um clipe e cole direto no Discord, comprima para o
  limite do Discord, mostre na pasta ou mande para a Lixeira.
- As configurações são uma página da biblioteca (a engrenagem na barra
  lateral, ou Settings no menu da bandeja). Elas ficam em
  `%LOCALAPPDATA%\GhostclipData\config.json`; editado à mão, a bandeja aplica
  na hora. Salvar pela página reescreve o arquivo e apaga os comentários que
  você tiver escrito nele. Os logs ficam em `%LOCALAPPDATA%\GhostclipData\Logs`.
- Linha de comando, para scripts, macros ou Stream Deck:

| Comando | Faz |
|---|---|
| `Ghostclip.exe --save` | A instância em execução salva um clipe |
| `Ghostclip.exe --trim <clipe> <início> <fim>` | Gera `<clipe>_trim.mp4` por stream copy |
| `Ghostclip.exe --compress <clipe>` | Gera `<clipe>_discord.mp4` dentro do limite do Discord |

## Medições

Notebook i7-13620H, só Intel UHD (QuickSync), 1080p60, H.264 20 Mbps, área de
trabalho parada. **Ainda não é benchmark em jogo**, então não há números de FPS.

| Medida | Resultado | Meta |
|---|---:|---:|
| Clipe garantido após a hotkey | 15–130 ms | |
| MP4 gravado | ~1,1–1,4 s | < 2 s |
| CPU: ffmpeg + worker (uma amostra de 10 s) | ~1,3% | < 1% |
| RAM: ffmpeg + worker (working set) | ~146–161 MB + ~45 MB | < 150 MB |
| Áudio vs. vídeo depois da compensação | 53–98 ms, incluindo a latência de reprodução do próprio teste | |
| Crash do worker → gravando de novo | ~1,3 s, buffer mantido | |
| Áudio só do jogo: som de outro app na faixa do jogo | 35 dB abaixo do jogo | |
| Cortar 10 s de um clipe de 22 s | stream copy, menos de um segundo | |
| Comprimir 24 s para limite de 10 MB | 8,7 MB, 720p60, 7–9 s | ≤ 10 MB |

A RAM passa da meta nesta máquina. Numa GPU integrada, as superfícies de vídeo
ficam na RAM do sistema e contam na memória do FFmpeg; numa GPU dedicada
ficariam na VRAM. Um spike de captura e codificação nativas (DXGI + Media
Foundation, em [spike/native](spike/native/README.md), em inglês) gastou mais
memória, não menos, porque os dois caminhos usam o mesmo driver de mídia da
Intel. Por isso o Ghostclip continua no FFmpeg.

### Meça no seu PC

O benchmark roda o benchmark embutido de um jogo com o Ghostclip desligado e
ligado, alternando, e gera a tabela acima com FPS e 1% low pelo
[PresentMon](https://github.com/GameTechDev/PresentMon). Ele mede o app de
verdade com as suas configurações: host, worker e FFmpeg juntos, mais um clipe
salvo no meio de cada rodada. No PowerShell como administrador:

```powershell
.\tools\get-tools.ps1                                            # PresentMon
.\bench\run-bench.ps1 -ProcessName "Jogo.exe" -SaveDuringRun     # 3 rodadas desligado, 3 ligado
```

Os resultados ficam em `bench\results\<data>\summary.md`. Números de outras
GPUs, principalmente NVIDIA e AMD, são muito bem-vindos numa issue.

## Limitações conhecidas

- Só o QuickSync foi testado. NVENC e AMF estão implementados, mas ainda não
  rodaram em hardware real.
- O corte por stream copy começa num keyframe, a cada 2 s; a biblioteca
  encaixa o início ali, e o `--trim` corta a partir do keyframe anterior.
- O player da biblioteca precisa das Extensões de Vídeo HEVC da Microsoft
  Store para mostrar clipes HEVC; sem elas, toca só o som. H.264, o padrão,
  sempre toca. A prévia toca a faixa do jogo; o microfone está no arquivo e
  na versão para o Discord.
- Detecção do jogo para o áudio só do jogo: um programa conta como jogo quando
  roda em tela cheia ou sem borda, ou quando tem perfil. Jogo em janela precisa
  de perfil.
- Um clipe que atravessa um crash do worker perde o segundo e pouco que o
  reinício levou.
- Mudar uma configuração de vídeo ou ligar/desligar o microfone reinicia a
  gravação e limpa o buffer, porque segmentos em dois formatos não se juntam
  sem reencode. A fonte de áudio e as configurações por jogo trocam ao vivo.

## Licença

[MIT](LICENSE). As mudanças estão em [CHANGELOG.md](CHANGELOG.md) (em inglês).
O FFmpeg não faz parte deste repositório. Os releases levam uma
build LGPL mínima feita por [build/ffmpeg](build/ffmpeg) (sem componentes GPL
nem nonfree), que o Ghostclip executa como programa separado; para
desenvolvimento, o `tools\get-tools.ps1` baixa uma build LGPL.
