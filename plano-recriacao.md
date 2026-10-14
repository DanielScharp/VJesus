# Plano de recriação adaptado: VJ / mapping em C# + JavaScript (Windows)

Base: relatório `relatorio-resolume-arena.md`. Suas respostas: uso em VJ ao vivo, projection mapping, palco/teatro, instalação e streaming; só Windows; C# e JavaScript; prioridade em **mapping, grade de clips e efeitos**; uso próprio.

Verifiquei sua máquina: **.NET SDK 10.0.401**, **WebView2 Runtime** instalado, GPU **Intel UHD Graphics 770** (integrada). A GPU integrada pesa nas escolhas abaixo (codec HAP, resolução de trabalho, número de layers).

## 1. Arquitetura proposta

Um único executável .NET com dois mundos que conversam por mensagens, parecido com o que o Resolume faz (motor nativo + API REST/WebSocket + apps web):

```
┌───────────────────────── VJApp.exe (.NET 10, Windows) ─────────────────────────┐
│                                                                               │
│  UI (JavaScript/React + TypeScript)        Motor de vídeo (C#)                 │
│  dentro do WebView2                        Direct3D 11 via Vortice.Windows     │
│  ┌─────────────────────────┐   JSON    ┌──────────────────────────────────┐   │
│  │ grade de clips          │ ◄──────►  │ Composition → Layers → Clips     │   │
│  │ painel de efeitos       │ postMsg   │ decodificação (FFmpeg / HAP)     │   │
│  │ editor de mapping       │           │ efeitos (shaders HLSL)           │   │
│  │ previews (JPEG/WebRTC)  │           │ mixer (blend modes)              │   │
│  └─────────────────────────┘           │ mapping (malha/warp/soft-edge)   │   │
│                                        └───────────┬──────────────────────┘   │
│  Controle: OSC, MIDI, REST/WebSocket ──────────────┤                          │
│                                                    ▼                          │
│                            Saídas: janelas fullscreen por projetor,           │
│                            Spout, NDI (streaming)                             │
└───────────────────────────────────────────────────────────────────────────────┘
```

Por que assim:
* **Direct3D 11 em vez de OpenGL**: é a API nativa do Windows, tem o melhor driver na Intel, e o **Spout** e a decodificação por hardware (D3D11VA) trabalham diretamente com texturas D3D11, sem cópias.
* **UI em JavaScript no WebView2**: você já conhece JS, a UI fica rápida de iterar, e a mesma UI pode virar um controle remoto no celular/tablet via WebSocket (como o "web remote" do Resolume).
* **Motor em C#**: o render loop e o vídeo ficam em código nativo-gerenciado, fora do navegador, sem a latência do Electron.

## 2. Bibliotecas (todas gratuitas para uso próprio)

| Função | Biblioteca .NET / JS |
|---|---|
| Direct3D 11, DXGI, compilação HLSL | **Vortice.Windows** (Vortice.Direct3D11, Vortice.D3DCompiler) |
| Janela host + WebView2 | **WinForms** ou **WPF** + **Microsoft.Web.WebView2** |
| UI | **React + TypeScript + Vite** (dentro do WebView2) |
| Decodificar H.264/HEVC/ProRes | **Sdcb.FFmpeg** ou **FFmpeg.AutoGen** (+ D3D11VA para decode na GPU) |
| HAP (codec ideal para VJ) | decodificar HAP via FFmpeg em .mov + upload direto como textura BC1/BC3 (Snappy com **Snappier**) |
| Spout (enviar/receber) | interop com **SpoutDX** (C++ do Spout2, via P/Invoke) ou compartilhamento D3D11 direto |
| NDI (streaming) | **NDI SDK** (gratuito) via wrapper **NewTek.NDI** / P/Invoke |
| OSC | **CoreOSC** ou **Rug.Osc** |
| MIDI | **DryWetMIDI** (MIDI learn, controladoras) |
| REST/WebSocket | **ASP.NET Core Kestrel** embutido (minimal API) |
| Projeto salvo | **System.Text.Json** |
| Editor de mapping (UI) | Canvas/SVG em React, ou **Konva** para arrastar pontos |

## 3. Como fazer os três recursos prioritários

### Grade de clips
* Modelo: `Composition → Layer[] → Clip[]` (linhas = layers, colunas = clips), igual ao Resolume.
* Cada clip tem um **decoder em thread própria** que entrega quadros num ring buffer; o render thread só pega a textura mais recente (nunca bloqueia).
* Disparo por clique, teclado, MIDI ou OSC (`/composition/layers/1/clips/3/connect`).
* Thumbnails gerados no import com FFmpeg.

### Efeitos
* Cada efeito = um **pixel shader HLSL** + uma lista de parâmetros declarados em JSON (nome, faixa, padrão). A UI monta os sliders automaticamente a partir do JSON.
* Cadeia: textura do clip → efeito 1 → efeito 2 → … usando duas render targets em ping-pong.
* Mixer de layers com **blend modes** (Add, Multiply, Screen, Alpha) e opacidade, também em shader.
* Animação de parâmetros por **BPM/LFO** e por áudio (FFT com NAudio) depois.
* Biblioteca inicial: cor/HSV, blur, kaleidoscope, mirror, pixelate, RGB shift, strobe, feedback, LUT.
* Opcional: portar shaders **ISF** (há centenas abertos) convertendo GLSL → HLSL com SPIRV-Cross numa ferramenta offline.

### Projection mapping
* **Input map**: cada slice recorta um retângulo/polígono da composição.
* **Output map**: cada slice é desenhada no projetor como uma **malha deformável** (4 cantos → grade NxM → Bézier), com interpolação feita no vertex shader.
* **Máscaras** poligonais e **soft-edge/edge blending** com curva gamma para juntar projetores.
* Um **output por monitor/projetor** (janela borderless fullscreen por tela, DXGI para listar saídas).
* Editor visual no WebView2: você arrasta os pontos no preview e o motor atualiza a malha em tempo real; modo "test card" (grade e cores) para alinhar.
* Salvar/carregar presets de mapping por palco.

## 4. Roteiro

| Fase | Entrega | Testa o quê |
|---|---|---|
| 0 | Esqueleto: janela WPF + WebView2 com React, janela de saída D3D11 numa segunda tela, ponte de mensagens | stack funcionando |
| 1 | Tocar um vídeo HAP/H.264 na saída, play/pause/loop | decode e render |
| 2 | Grade de clips com várias layers, opacidade e blend modes, thumbnails | grade de clips |
| 3 | Cadeia de efeitos HLSL com parâmetros na UI, BPM tap | efeitos |
| 4 | Mapping: slices, warp de 4 cantos → malha, máscaras, várias saídas | mapping |
| 5 | Soft-edge, presets, salvar/abrir projeto | palco/instalação |
| 6 | MIDI learn, OSC, controle remoto web | VJ ao vivo |
| 7 | Spout e NDI (streaming, OBS) | streaming |

As fases 0 a 4 cobrem tudo o que você listou como indispensável.

## 5. Cuidados com a Intel UHD 770

* Use **HAP** (ou HAP Q) para os clips: a GPU recebe o quadro já comprimido, quase sem custo de CPU. Converta seus vídeos com `ffmpeg -c:v hap`.
* Trabalhe em **1080p** na composição; 4K com várias layers vai engasgar numa GPU integrada.
* H.264 funciona bem com decodificação por hardware (Quick Sync via D3D11VA), mas trocar de clip é mais lento que com HAP.
* Para shows grandes com vários projetores, uma GPU dedicada (NVIDIA/AMD) muda o jogo.
