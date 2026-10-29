# Ghostclip

[English](README.md)

Gravador de "instant replay" open-source para Windows 10/11. Fica na bandeja,
guarda os últimos segundos da tela e salva um clipe quando você aperta a hotkey.
O objetivo nº 1 é tirar o mínimo possível de FPS do jogo.

Nada roda dentro do jogo: sem injeção de DLL, sem hook de renderização, sem
leitura de memória e sem hook global de teclado.

## Estado

**Fase 0, spike de viabilidade.** Scripts PowerShell que medem se o FFmpeg
(ddagrab + encoder de hardware) cumpre as metas de custo. Veja
[spike/README.md](spike/README.md) (em inglês). A especificação completa está
em [docs/spec.md](docs/spec.md) (em inglês).

## Metas

| Medida (1080p60, 20 Mbps, buffer de 60 s) | Meta |
|---|---|
| Queda de FPS médio | ≤ 5% |
| Queda de 1% low | ≤ 2% |
| CPU do processo de captura | < 1% |
| RAM do processo de captura | < 150 MB |
| Tempo para salvar o clipe | < 2 s, sem recodificar |

A tabela de benchmark entra aqui quando houver medições.
