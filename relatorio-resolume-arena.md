# Resolume Arena 7.28.1: tecnologias, arquitetura e plano de recriação

Análise estática da instalação em `C:\Program Files\Resolume Arena` (14/10/2026). Foram lidos apenas metadados públicos dos binários: tabelas de import das DLLs, strings embutidas, nomes de classes do RTTI, arquivos de licença e arquivos de dados que acompanham o instalador. Nenhum código foi descompilado ou copiado.

> Sobre o REA: a instalação do `rea-agents` via npm foi bloqueada pela política de permissões desta sessão. A mesma inspeção que o REA faria sem Ghidra/IDA (imports, strings, símbolos) foi feita com dois scripts PowerShell somente-leitura. Para pseudocódigo/assembly o REA precisaria de Ghidra, IDA ou Hopper instalados, o que não é necessário para identificar a stack.

## 1. Resumo

| Camada | O que o Resolume usa | Evidência principal |
|---|---|---|
| Linguagem | C++ (MSVC, C++17/20), x64 | `MSVCP140.dll`, `VCRUNTIME140_1.dll`, nomes mangled `?...@resolume@@` |
| Framework de app/UI | **JUCE** (janelas, UI própria desenhada, áudio, MIDI) | 3.343 ocorrências de `juce::`, `juce::String`, `WASAPIAudioIODevice@juce`, `MidiMessage@juce` |
| Rede / async | **Boost** (asio, beast, urls), WebSocket | `boost::asio::any_io_executor`, `boost::beast::http`, `Sec-WebSocket-Key` |
| Renderização | **OpenGL 4.1** (GLSL `#version 410`), interop D3D11/DXGI para compartilhamento | `OPENGL32.dll`, `wglGetProcAddress`, 383× `#version 410`, `D3D11CreateDevice`, `CreateDXGIFactory1` |
| Shaders | glslang + SPIRV-Cross (licenças), MoltenVK no build macOS | `licenses\glslang.md`, `spirv-cross.md`, `moltenvk.md` |
| Vídeo (decode/encode) | **FFmpeg 7.x** (avcodec-61, avformat-61, avutil-59, swscale-8) + Media Foundation | imports `avcodec_send_packet`, `sws_scale`, `MFCreateMediaSession` |
| Codecs GPU | **DXV** (codec próprio, DXT/BC + Snappy/LZ4/LZF/density), **HAP** | `licenses\hap.md`, `snappy.md`, `lz4.md`, `squish.md`, `ycocg.md`, classes `snappy::Source` |
| Encode HW | NVENC via FFmpeg | `h264_nvenc` |
| Captura/SDI | **AJA NTV2 SDK**, **Blackmagic DeckLink** | `CNTV2DriverInterface`, `IDeckLinkDiscovery` |
| Compartilhamento de vídeo | **Spout** (Windows), **Syphon** (macOS), **NDI** | `spoutGL::...`, `SyphonVideoSource`, `Processing.NDI.Lib.x64.dll` (27 funções `NDIlib_*`) |
| Controle | **OSC** (oscpack), **MIDI** (WinMM via JUCE), **Ableton Link**, **SMPTE LTC** | `ReceivedMessage@osc`, `midiInOpen`, `Show Ableton Link`, `libltc.dll` |
| Iluminação | **Art-Net / DMX** (libartnet modificada), "Lumiverses", DMX slices | `artnet.dll` (`artnet_send_poll_reply_resolume`), `DmxSlice`, `Lumiverse` |
| API externa | **REST + WebSocket** (spec OpenAPI/Swagger), apps web React | `rest\docs\swagger.yaml`, `rest\example` (create-react-app) |
| Integração IA | **Servidor MCP em Rust** (tokio, serde_json, rmcp 1.2) | `mcp\resolume_arena_mcp_server.exe`, paths `rmcp-1.2.0\src\...` |
| Efeitos de IA | **ONNX Runtime + DirectML** (profundidade, segmentação) | `onnxruntime.dll`, `DirectML.dll`, modelos `depth_anything_v2`, `midas_v21`, `modnet`, `pphumanseg` |
| Plugins de vídeo | **FFGL** (FreeFrame GL) + **Wire** (patches nodais `.wired`) | `FreeFrame (FFGL) Plugin Directories`, `WireLib.dll`, `WireNodes.dll` |
| Plugins de áudio | VST (efeitos "afx" construídos com JUCE) | `plugins\afx\*.dll` com `Juce VST Host`, `receiveVstEvents` |
| Persistência | XML (rapidxml, tinyxml2), JSON (nlohmann), SQLite | `xml_node@rapidxml` em `readStateDerived@UIManager`, `XMLDocument@tinyxml2`, `SQLite format 3` |
| Imagens/fontes | libjpeg-turbo 2.0.4, libtiff, giflib, lodepng, stb, FreeType | strings de versão + pasta `licenses` |
| Expressões | exprtk (parser matemático, provavelmente para Wire/parâmetros) | `licenses\exprtk.md` |
| Infra | BugSplat (crash report), WinSparkle (auto-update), dongle/licença | `BugSplatRc.dll`, `win_sparkle_*`, `DongleWizard@registration` |

## 2. Arquitetura inferida

Os nomes de classes no RTTI mostram a organização interna (só nomes, sem código):

```
Main / ApplicationController
 └─ Composition  (recebe OSC: Composition::…(osc::ReceivedMessage), MIDI: …(juce::MidiMessage))
     ├─ Deck  (CompositionLoader::loadCurrentDeck)
     │   └─ Layer / LayerGroup
     │       └─ Clip  (TriggerStyle, Target, setVideoDuration)  ← fontes: arquivo, NDI, Spout, captura, Wire, FFGL source
     ├─ efeitos e mixers por clip/layer/composição (CompositionInspector::createWireMixer)
     └─ Preview
 UIManager (estado salvo em XML)    CompositionController
 Advanced Output  → Screens → Slices (ScreenLayerSlice, DmxSlice)  → Lumiverses (DMX/Art-Net)
```

Pipeline de quadro (inferido):

1. **Decodificação**: DXV/HAP vão direto para textura comprimida BC/DXT na GPU (decodificação barata na CPU, só descompressão Snappy); H.264/ProRes etc. passam por FFmpeg → `sws_scale` → upload.
2. **Composição na GPU** em OpenGL 4.1: cada clip vira uma textura, recebe cadeia de efeitos (shaders GLSL, FFGL, patches Wire), e as layers são misturadas com blend modes.
3. **Saída**: a composição final é mapeada pelo Advanced Output (mapeamento de slices, warping, soft-edge) para monitores, Spout/NDI/Syphon, placas SDI (AJA/DeckLink) e, via amostragem de pixels, para DMX/Art-Net.
4. **Controle** em paralelo: OSC, MIDI, teclado, REST/WebSocket e o servidor MCP alteram parâmetros endereçados por caminhos como `/composition/layers/1/clips/2/connect` (828 strings `/composition/...` no binário).
5. **Clock**: BPM interno, Ableton Link e SMPTE LTC sincronizam playback e efeitos.

**Wire** é um motor de patches nodais separado (`WireLib.dll` + `WireNodes.dll`), também em C++/OpenGL 4.1, que carrega ONNX Runtime. Os arquivos `.wired` são um cabeçalho binário + JSON aberto descrevendo nós, conexões e parâmetros (ex.: `LUT.wired` tem nós "Texture In" → "LUT" → "Texture Out"). Os efeitos de IA (Depth Blur, Background Removal etc.) são patches Wire.

## 3. Como recriar algo semelhante com open source

A boa notícia: quase toda a stack do Resolume já é composta de bibliotecas abertas. O que é proprietário é o código de integração, a UI e o codec DXV (que tem alternativa aberta: HAP).

### Stack recomendada (C++, mesmo caminho do Resolume)

| Necessidade | Opção open source |
|---|---|
| App, janelas, UI, áudio, MIDI | **JUCE 8** (GPL/comercial) ou **Dear ImGui** + **SDL3** (MIT/zlib) para uma UI mais simples |
| GPU | **OpenGL 4.1** (mais simples, igual ao Resolume) ou **bgfx / Diligent / WebGPU (Dawn/wgpu)** para um backend moderno |
| Decodificação | **FFmpeg** (LGPL) + **HAP** (snappy + texturas BC) no lugar do DXV |
| Shaders de efeito | GLSL próprio + suporte a **ISF** (Interactive Shader Format) e **FFGL 2.x** (SDK aberto da própria Resolume) |
| Nós/patches | **imnodes** ou **ImNodeFlow** para a UI de nós, grafo próprio em C++ |
| Compartilhamento | **Spout2** (BSD), **Syphon** (macOS), **NDI SDK** (gratuito, não open source) |
| OSC | **oscpack** ou **liblo** |
| DMX/Art-Net/sACN | **libartnet** ou **libe131**; **OLA** (Open Lighting Architecture) |
| Sincronia | **Ableton Link** (GPL), **libltc** |
| API remota | **Boost.Beast** ou **Crow/Drogon** para REST/WebSocket, JSON com **nlohmann** |
| IA | **ONNX Runtime** + DirectML/CUDA com Depth Anything / MODNet |
| Projeto salvo | JSON ou XML (nlohmann / pugixml) |

### Atalhos que evitam escrever tudo do zero

* **openFrameworks** ou **Cinder** (C++, o Resolume chegou a usar Cinder): dão janela, OpenGL, vídeo, OSC e MIDI prontos via addons.
* **TouchDesigner** (não é open source) ou **vvvv gamma**: se o objetivo é usar, não programar.
* Open source já parecidos para estudar: **VPT (Video Projection Tool)**, **MapMap**, **Splash** (projection mapping), **Hydra** e **Nannou** (Rust).
* **Web (Electron/Tauri + WebGPU)**: protótipo rápido, porém com latência e desempenho de vídeo piores para shows grandes.

### Roteiro em fases (MVP primeiro)

1. **Player de clips em grade**: janela + OpenGL, decodificar HAP/H.264 com FFmpeg, grade de clips com disparo por clique.
2. **Layers e mixagem**: N layers com opacidade e blend modes em shader, transições.
3. **Efeitos**: cadeia de efeitos GLSL/ISF com parâmetros animáveis e BPM.
4. **Controle**: OSC e MIDI learn com endereçamento estilo `/composition/layers/1/...`.
5. **Saídas**: fullscreen em múltiplos monitores, Spout/NDI.
6. **Mapping**: slices com warp/mesh e soft-edge (Advanced Output).
7. **Extras conforme necessidade**: DMX/Art-Net, captura SDI, REST/WebSocket, efeitos de IA, editor de nós.

As fases 1 a 5 são um projeto de alguns meses para uma pessoa experiente em C++/GPU; mapping, DMX e editor de nós aumentam bastante o escopo.

## 4. Perguntas para adaptar o plano

1. Qual é o uso principal: VJ ao vivo em festas, projection mapping, palco/teatro, instalação fixa ou streaming?
2. Precisa rodar em quais sistemas: só Windows, ou também macOS/Linux?
3. Qual linguagem você (ou a equipe) domina: C++, Rust, C#/.NET, Python ou JavaScript?
4. Quais recursos são indispensáveis no início (ex.: grade de clips, efeitos, mapping, DMX, NDI, controladora MIDI específica)?
5. Vai usar placas de captura (AJA/Blackmagic) ou só arquivos e câmeras USB?
6. O software será de uso próprio, open source ou comercial? Isso decide entre JUCE (GPL/comercial) e alternativas MIT.

## Anexo: método

* Imports PE de `Arena.exe`, `WireLib.dll`, `WireNodes.dll`, `artnet.dll`, `plugins\afx\EQ-3.dll`, `mcp\resolume_arena_mcp_server.exe` lidos com parser PowerShell próprio.
* Contagem de strings ASCII por palavra-chave nos mesmos binários.
* Pasta `licenses` (36 arquivos), `rest`, `ai-models`, `plugins\wire` listadas; um `.wired` aberto para ver o formato.
* Limitação: contagens de strings curtas (ex.: "Hap", "NDI", "ASIO") incluem falsos positivos; as conclusões se apoiam nas evidências inequívocas citadas na tabela.
