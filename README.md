# VJesus

App de VJ / projection mapping para Windows, inspirado no Resolume Arena.
Motor em C# (.NET 10 + Direct3D 11 via Vortice.Windows), UI em React/TypeScript dentro do WebView2.
Plano completo em `plano-recriacao.md`; análise do Resolume em `relatorio-resolume-arena.md`.

## Instalação

**Para instalar em outro computador, veja [INSTALAR.md](INSTALAR.md)** (pacote pronto em [Releases](https://github.com/DanielScharp/vjapp/releases/latest), dependências e links).

## Rodar (desenvolvimento)

```powershell
dotnet run --project src/VJApp
```

Clips de teste: `ffmpeg -f lavfi -i testsrc2=size=1920x1080:rate=30 -t 10 -c:v hap media/teste-hap.mov`

O build compila a UI (`ui/`, via `npm run build`) e copia para a pasta `ui` ao lado do exe.

### Desenvolvimento da UI com hot reload

```powershell
cd ui; npm run dev                 # terminal 1
$env:VJ_UI_DEV="1"; dotnet run --project src/VJApp -p:SkipUi=true   # terminal 2
```

## Estrutura

| Caminho | O que é |
|---|---|
| `src/VJApp/MainForm.cs` | janela de controle, hospeda o WebView2 e trata as mensagens da UI |
| `src/VJApp/OutputWindow.cs` | janela de saída (projetor), janela ou tela cheia por monitor |
| `src/VJApp/MainForm.Mapping.cs` | janelas por tela, envio das malhas ao motor, preview JPEG para a UI |
| `src/VJApp/Model/Mapping.cs` | telas, slices, máscaras (salvo em JSON) |
| `src/VJApp/Engine/MappingMesh.cs` | gera os triângulos: homografia (4 cantos), Catmull-Rom (malha), ear clipping (máscaras) |
| `src/VJApp/Engine/RendererOutputs.cs` | swap chain por tela, desenho dos slices/máscaras, preview |
| `ui/src/MappingEditor.tsx` / `mappingMath.ts` | editor de entrada e saída com pontos arrastáveis |
| `src/VJApp/Engine/VideoPlayer.cs` | decodificação FFmpeg numa thread própria, fila de quadros BGRA, loop/seek |
| `src/VJApp/Engine/Renderer.cs` | device D3D11, composição das layers (ping-pong 1920×1080) e saída, numa thread própria |
| `src/VJApp/Engine/Thumbnailer.cs` | miniatura do primeiro quadro de cada clip |
| `src/VJApp/Model/Composition.cs` | grade layers × colunas, clips, opacidade, blend; salva em JSON |
| `ui/src/ClipGrid.tsx` | grade de clips estilo Resolume |
| `src/VJApp/Shaders/Engine.hlsl` | shaders de fonte, mistura (blend modes) e saída |
| `src/VJApp/Shaders/Effects/*.hlsl` | um arquivo por efeito (veja abaixo) |
| `src/VJApp/Engine/EffectLibrary.cs` | lê os efeitos, gera o HLSL final com os parâmetros |
| `ui/src/EffectsPanel.tsx` | painel de efeitos da layer selecionada ou do master |
| `ui/src/bridge.ts` | tipos e envio/recebimento de mensagens UI ↔ motor |
| `ui/src/App.tsx` | tela de controle |

## HAP na GPU, transições, BPM e áudio

* **HAP / HAP Alpha / HAP Q / HAP R** vão direto para a placa de vídeo em blocos DXT/BC (só a compressão Snappy sai na CPU). Medido nesta máquina (Intel UHD 770), 4 layers HAP 1080p60: **15 fps e 32% de CPU → 60 fps e 4% de CPU**. Converter vídeos: `ffmpeg -i entrada.mp4 -c:v hap -format hap_q saida.mov` (ou `hap`, `hap_alpha`).
* **Transições** por layer (clique no nome da layer): Corte seco, Crossfade, Escurecer e Cortina, com duração. Parar a layer também faz a transição de saída. O áudio acompanha (o clip que sai abaixa enquanto o novo sobe).
* **BPM** no topo: digite o valor, use **TAP** (ou a tecla **T**) no ritmo e **⟲** para a batida 1 começar agora. Efeitos podem usar `Beat` e `Bpm` (ex.: *Strobe* com "Seguir o BPM" e *Pulso no BPM*). OSC: `/composition/tempocontroller/tempo`, `/tempotap`, `/resync`.
* **Áudio dos clips**: o som dos vídeos é tocado na saída padrão do Windows, com volume por layer (painel da layer) e volume master. OSC: `/composition/layers/{L}/audio/volume`, `/composition/audio/volume`.

## Letras (estilo Holyrics)

Aba **Letras**: biblioteca de músicas com busca (título, artista ou trecho, sem acento), editor, **importar .txt** (um arquivo por música), slides clicáveis, **tela em branco**, **texto rápido** e aparência (fonte do Windows, tamanho, posição, contorno, sombra, caixa atrás do texto, maiúsculas, fade entre slides).

* Formato: linha em branco separa slides; `[Refrão]` na primeira linha de um bloco dá nome à seção (não vai para o telão).
* A letra aparece no telão por um clip **Letras** na grade (botão "Colocar na grade" ou menu da célula → *Letras*). Assim ela se mistura com os vídeos, recebe efeitos e passa pelo mapeamento como qualquer layer — use a layer de cima.
* Atalhos na aba: **→ / espaço** próximo, **←** anterior, **B / Esc** tela em branco (só o telão; o retorno continua mostrando), **Shift+Esc / Delete** encerra a letra nas duas telas (fim da música). OSC: `/lyrics/next`, `/lyrics/prev`, `/lyrics/blank`, `/lyrics/clear`. O controle pelo celular também tem esses botões.
* **Cenas**: estilos com nome (fonte, tamanho, cor, posição, caixa…). Crie em *Cenas → + Nova cena* e edite no painel Aparência ("Editando: …"). Escolha a **cena da música** para valer em todos os slides.
* **Um slide só**: o ✎ de cada slide abre o editor do slide — cena só daquele slide, ajustes (tamanho, cor, posição, alinhamento, caixa) e barra de formatação de **trechos** (negrito, itálico, sublinhado, cor e tamanho do trecho selecionado).
* No texto isso fica assim (dá para escrever direto no editor da letra):

  ```
  [Refrão]
  {cena: Destaque}
  {estilo: tamanho=60; posicao=baixo; alinhamento=esquerda; caixa=sim}
  Uma <cor #FF5050>palavra</cor> em <b>negrito</b> e um <tam 160%>trecho grande</tam>
  ```

  Ordem de prioridade: estilo padrão → cena da música → cena do slide → ajustes do slide → formatação do trecho.
* **Transição do slide** (no painel Aparência, numa cena ou só num slide pelo ✎): fade, corte, subir, descer, entrar pela direita/esquerda, zoom, aproximar, **palavra por palavra**, **linha por linha** e **máquina de escrever**, com duração. No texto: `{estilo: transicao=palavra; duracao=1200}`.
* **Palavra animada**: selecione o trecho no editor do slide → escolha a animação (balançar, flutuar, pulsar, onda, brilhar, tremer, piscar, girar), força e velocidade → **Animar trecho**. No texto: `<anim balancar 1 0.6>palavra</anim>`. As animações rodam na GPU (a letra é desenhada uma vez e as palavras são movidas a cada quadro), e aparecem animadas também na prévia dos slides.
* Biblioteca, cenas e estilo ficam em `%AppData%\VJesus\letras.json`.

## Tela de retorno (músicos)

Aba **Retorno**: liga uma janela no monitor escolhido (tela cheia ou janela) com **slide atual**, **próximo slide**, **relógio**, **cronômetro** (regressivo ou progressivo; fica vermelho piscando quando estoura), **alerta** (piscando no meio, até limpar) e **mensagem** (texto fixo só para os músicos). Cada item pode ser ligado/desligado, arrastado, redimensionado e ter tamanho, cor e alinhamento próprios; o layout no editor é exatamente o que aparece no monitor. O item **Telão (imagem/vídeo)** só aparece quando há algo para mostrar, em dois casos:
1. **Layer de avisos**: marque "Mostrar no retorno quando tocar" no painel da layer (aparece a etiqueta **RET** na grade). Quando um clip dessa layer toca, o retorno mostra **só as layers marcadas** — sem a letra e sem o fundo da letra; quando para, a imagem some sozinha.
2. **Espelhar telão no retorno** (botão no monitor RETORNO e na aba Retorno, OSC `/stage/mirror`): mostra o telão inteiro até desligar.

A opção "Imagem do telão em tela cheia quando aparecer" faz a imagem ocupar o retorno todo (a letra sai; relógio, cronômetro, alerta e mensagem ficam por cima). Imagem em 960×540 a ~15 fps. Config em `%AppData%\VJesus\retorno.json`.

**Monitores do operador**: no topo de todas as abas ficam o **TELÃO** (a composição que vai para as telas) e o **RETORNO** (a mesma tela dos músicos), com o que está tocando em cada layer, o slide no ar e avisos — por exemplo, quando há letra projetada mas o clip Letras não está tocando. Dá para recolher com o botão "Monitores". OSC: `/stage/timer/start|pause|toggle|reset`, `/stage/alert/clear`.

## Ataques

Barra **Ataques** em todas as abas: efeitos rápidos por cima de tudo (inclusive da letra) que entram, seguram e somem suavemente, voltando ao que estava no telão. Não vão para a tela de retorno.

* Tipos: **Flash de cor** (preto = blackout), **Strobe**, **Soco de zoom**, **Tremor**, **Inverter**, **Onda de cor**, **Varredura**.
* Cada ataque tem cor, **entrada / segura / saída suave** (ms), intensidade, ajuste do tipo, piscadas (strobe), **tecla de atalho** e o modo **segurar enquanto pressionado**.
* Vêm 8 prontos nas teclas **1 a 8**; "Editar ataques" cria, duplica, reordena e muda tudo (botão Testar). Ficam em `%AppData%\VJesus\ataques.json`.
* Também por clique (segurar o botão nos de "segurar"), MIDI learn, celular e OSC `/attack/{n}` (1 = primeiro; valor 0 solta).

## Projetos e presets

* **Novo / Abrir… / Salvar / Salvar como…** no topo: um projeto `.vjproj` guarda a grade de clips, efeitos e o mapeamento.
* O estado atual também é salvo sozinho em `%AppData%\VJesus` (volta ao reabrir o app).
* **Presets de mapeamento** (aba Mapeamento): salve o ajuste de cada palco com um nome e carregue quando voltar a ele.

## Controle externo

| Meio | Como |
|---|---|
| OSC | UDP porta **7000** (como o Resolume), valores 0..1, botões disparam com ≥ 0.5 |
| Celular/tablet | abra `http://<ip-do-pc>:8080/` no navegador (o endereço aparece no painel "Controle externo"; o Windows pode pedir para liberar no firewall) |
| MIDI | botão **MIDI learn**: clique num controle da tela e mexa no knob/pad; mapeamentos ficam em `%AppData%\VJesus\midi.json` |

Endereços (índices começam em 1):

| Endereço | Valor |
|---|---|
| `/composition/layers/{L}/clips/{C}/connect` | dispara o clip |
| `/composition/columns/{C}/connect` | dispara a coluna |
| `/composition/layers/{L}/clear` | para a layer |
| `/composition/clear` | para tudo |
| `/composition/layers/{L}/video/opacity` | 0..1 |
| `/composition/layers/{L}/effects/{E}/{param}` | 0..1 → mín..máx do parâmetro (`bypassed` liga/desliga) |
| `/composition/effects/{E}/{param}` | efeitos do master |
| `/composition/video/brightness` | 0..1 |
| `/composition/speed` | 0..1 (× 4) |
| `/composition/testcard` | liga/desliga o test card |

## Spout e NDI

No painel "Spout e NDI": a composição 1920×1080 vai como sender **Spout** (OBS com plugin Spout, Resolume, TouchDesigner…) e/ou fonte **NDI** a 30 fps (OBS com plugin NDI, vMix, Studio Monitor). O NDI usa o NDI Runtime instalado no Windows (gratuito, ndi.video/tools); para testes dá para apontar `VJ_NDI_DLL` para a DLL.

## OBS

**VJesus → OBS** (escolha um):
1. Sem instalar nada: no OBS, *Fonte → Captura de jogo* (ou *Captura de janela*) e escolha a janela "VJesus - Tela 1".
2. Spout (melhor qualidade, sem depender da janela): instale o plugin gratuito **OBS Spout2** (Off World Live), ligue **Spout** no painel "Spout e NDI" do VJesus e, no OBS, adicione a fonte *Spout2 Capture* → "VJesus".
3. NDI (pela rede, outro PC): NDI Runtime + plugin **DistroAV** no OBS; ligue **NDI** no VJesus.

**OBS → VJesus** (escolha um):
1. Câmera Virtual (já vem no OBS): no OBS clique em **Iniciar câmera virtual**; no VJesus, numa célula vazia, *Entrada ao vivo → Câmera: OBS Virtual Camera*. O clip fica tentando até o OBS começar a mandar imagem.
2. Spout: com o plugin OBS Spout2, ative *Spout Output* no OBS (Ferramentas); no VJesus, *Entrada ao vivo → Spout: <nome>*.

Webcams e placas de captura aparecem na mesma lista de câmeras.

## Criar um efeito

Crie `src/VJApp/Shaders/Effects/meuefeito.hlsl`. Com o app aberto, ele aparece na lista na hora (e recarrega a cada vez que você salva):

```hlsl
// @name Meu efeito
// @param amount "Quantidade" 0 1 0.5        // id, rótulo, mín, máx, padrão [, passo]
// @feedback                                  // opcional: hist(uv) devolve o quadro anterior
float4 Effect(float2 uv)
{
    float4 c = tex(uv);                       // tex(), hist(), mirrorUV(), luma(), Time, Resolution, PI
    return float4(lerp(c.rgb, 1 - c.rgb, amount), c.a);
}
```

Erros de compilação aparecem na faixa vermelha da UI com arquivo e linha.

## Mensagens da ponte

Os tipos de todas as mensagens estão em `ui/src/bridge.ts` (`UiMessage` e `EngineMessage`).

## Testes automáticos

Com `$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS="--remote-debugging-port=9222"` a UI pode ser controlada pelo DevTools Protocol (ex.: `chrome.webview.postMessage({type:'triggerColumn',column:0})`).

## Status

- [x] Fase 0: esqueleto (UI React no WebView2, saída D3D11 a 60 fps, test card e plasma, escolha de monitor/tela cheia)
- [x] Fase 1: tocar vídeo com FFmpeg (DXV, HAP, H.264...), loop, play/pause, seek
- [x] Fase 2: grade de clips 4×8, opacidade e 8 blend modes por layer, disparo por coluna, thumbnails, geradores, arrastar do Explorer, composição salva em `%AppData%\VJesus\composicao.json`
- [x] Fase 3: 12 efeitos HLSL com parâmetros, cadeia por layer e no master, feedback, recarga automática ao editar um .hlsl
- [x] Fase 4: mapping com várias telas de saída, slices (recorte da entrada, warp 4 cantos com perspectiva ou malha até 5×5, soft-edge, opacidade), máscaras poligonais, preview da composição; salvo em `%AppData%\VJesus\mapeamento.json`
- [x] Fase 5: projetos .vjproj e presets de mapeamento
- [x] Fase 6: OSC, MIDI learn e controle remoto pelo celular
- [x] Fase 7: Spout e NDI
