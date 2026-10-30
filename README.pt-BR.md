# Ghostclip

[English](README.md)

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
- **Corte** de clipe sem reencode.
- **Comprimir para o Discord:** reencoda na GPU para caber no limite de upload
  (10 MB por padrão), juntando jogo e microfone numa faixa só.

## Estado

**Fase 3.** Funcionam gravação, save, roteamento de áudio, perfis, corte e
compressão. Ainda falta: números de benchmark em jogo, instalador e um GIF para
esta página. O plano completo está em [docs/spec.md](docs/spec.md) (em inglês).

## Como funciona

```
Ghostclip.exe (bandeja)           Ghostclip.Worker.exe (sem UI)          ffmpeg.exe
  hotkey, config, detecção     ⇄    supervisiona o gravador,        →     ddagrab → encoder da GPU
  do jogo, save, corte,             áudio do jogo/sistema ── pipe →       → segmentos MPEG-TS de 2 s
  compressão                        microfone             ── pipe →       %LOCALAPPDATA%\Ghostclip\Buffer
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

## Compilar e rodar

Requer Windows 10 2004+ e o SDK do .NET 10.

```powershell
.\tools\get-tools.ps1        # FFmpeg LGPL e PresentMon em tools\
dotnet build
.\artifacts\bin\Debug\Ghostclip.exe
```

Testes: `dotnet test --project tests/Ghostclip.Core.Tests`

## Uso

- **Alt+F10** salva em `Vídeos\Ghostclip\<Jogo>\<Jogo>_AAAA-MM-DD_HH-MM-SS.mp4`.
- O menu da bandeja mostra o que está sendo gravado e tem corte, compressão,
  configurações e pastas. O ponto do ícone fica vermelho enquanto grava.
- As configurações ficam em `%LOCALAPPDATA%\Ghostclip\config.json` (também
  editáveis pela bandeja) e os logs em `%LOCALAPPDATA%\Ghostclip\Logs`.
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

## Limitações conhecidas

- Só o QuickSync foi testado. NVENC e AMF estão implementados, mas ainda não
  rodaram em hardware real.
- O corte por stream copy começa num keyframe, então pode começar até 2 s antes
  do início pedido.
- Detecção do jogo para o áudio só do jogo: um programa conta como jogo quando
  roda em tela cheia ou sem borda, ou quando tem perfil. Jogo em janela precisa
  de perfil.
- Um clipe que atravessa um crash do worker perde o segundo e pouco que o
  reinício levou.
- Mudar uma configuração de vídeo ou ligar/desligar o microfone reinicia a
  gravação e limpa o buffer, porque segmentos em dois formatos não se juntam
  sem reencode. A fonte de áudio e as configurações por jogo trocam ao vivo.
