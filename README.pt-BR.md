# Ghostclip

[English](README.md)

Gravador de "instant replay" open-source para Windows 10/11. Fica na bandeja,
guarda o último minuto da tela e do áudio do sistema, e salva um clipe quando
você aperta a hotkey. O objetivo nº 1 é tirar o mínimo possível de FPS do jogo.

Nada roda dentro do jogo: sem injeção de DLL, sem hook de renderização, sem
leitura de memória e sem hook global de teclado. A captura usa o Desktop
Duplication, a hotkey usa `RegisterHotKey` e a codificação roda no encoder de
hardware da GPU (NVENC, AMF ou QuickSync). Não existe fallback para encoder de
CPU.

## Estado

**Fase 1, MVP.** Funcionam o app de bandeja, a hotkey, a detecção do encoder de
hardware, o áudio do sistema, as pastas por jogo e as configurações em JSON.
Ainda falta: números de benchmark em jogo, instalador, microfone e áudio só do
jogo. O plano completo está em [docs/spec.md](docs/spec.md) (em inglês); o spike
de viabilidade que veio antes está em [spike/README.md](spike/README.md) (em
inglês).

## Como funciona

```
Ghostclip.exe (bandeja)           Ghostclip.Worker.exe (sem UI)          ffmpeg.exe
  hotkey, config, nome do jogo ⇄    supervisiona o gravador,        →     ddagrab → encoder da GPU
  salva clipes, reinicia worker     áudio WASAPI loopback  ── pipe →      → segmentos MPEG-TS de 2 s
        named pipe (JSON)           rotação do buffer                     %LOCALAPPDATA%\Ghostclip\Buffer
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
- **Sincronia de áudio.** O FFmpeg começa o relógio do áudio uns 150 ms antes
  do primeiro frame de vídeo. O worker mede essa diferença no relógio do
  QueryPerformanceCounter a cada sessão, e o save desloca o áudio de volta.

## Compilar e rodar

Requer Windows 10 2004+ e o SDK do .NET 10.

```powershell
.\tools\get-tools.ps1        # FFmpeg LGPL e PresentMon em tools\
dotnet build
.\artifacts\bin\Debug\Ghostclip.exe
```

Testes: `dotnet test --project tests/Ghostclip.Core.Tests`

## Uso

- **Alt+F10** salva os últimos 60 s em `Vídeos\Ghostclip\<Jogo>\<Jogo>_AAAA-MM-DD_HH-MM-SS.mp4`.
  O nome do jogo vem do programa em primeiro plano.
- `Ghostclip.exe --save` faz o mesmo a partir de um script, macro ou Stream Deck.
- O botão direito no ícone da bandeja abre as configurações, a pasta de clipes
  e os logs. O ponto do ícone fica vermelho enquanto grava.
- As configurações ficam em `%LOCALAPPDATA%\Ghostclip\config.json` e os logs em
  `%LOCALAPPDATA%\Ghostclip\Logs`.

## Medições

Notebook i7-13620H, só Intel UHD (QuickSync), 1080p60, H.264 20 Mbps, áudio do
sistema ligado, área de trabalho parada. **Ainda não é benchmark em jogo**,
então não há números de FPS.

| Medida | Resultado | Meta |
|---|---:|---:|
| Clipe garantido após a hotkey | 15–40 ms | |
| MP4 gravado | ~1,1 s | < 2 s |
| CPU: ffmpeg + worker (uma amostra de 10 s) | ~1,3% | < 1% |
| RAM: ffmpeg + worker (working set) | ~146–161 MB + ~45 MB | < 150 MB |
| Áudio vs. vídeo depois da compensação | 53–98 ms, incluindo a latência de reprodução do próprio teste | |
| Crash do worker → gravando de novo | ~1,3 s, buffer mantido | |

A RAM passa da meta nesta máquina. Numa GPU integrada, as superfícies de vídeo
ficam na RAM do sistema e contam na memória do FFmpeg; numa GPU dedicada
ficariam na VRAM. Isso, mais o custo de FPS num jogo de verdade, é o que decide
se o FFmpeg fica ou se a captura vira nativa (Fase 2).

## Limitações conhecidas

- Só o QuickSync foi testado. NVENC e AMF estão implementados, mas ainda não
  rodaram em hardware real.
- Um clipe que atravessa um crash do worker perde o segundo e pouco que o
  reinício levou.
- Mudar uma configuração de vídeo ou áudio reinicia a gravação e limpa o
  buffer, porque segmentos em dois formatos não se juntam sem reencode.
