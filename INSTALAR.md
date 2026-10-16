# Instalação do VJApp num computador novo

Há dois caminhos. Para **usar** o programa, siga o **Caminho A** (não precisa instalar ferramentas de programação). O **Caminho B** é para quem vai **mexer no código**.

## Requisitos do computador

| Item | Mínimo | Observação |
|---|---|---|
| Sistema | Windows 10 (21H2+) ou Windows 11, **64 bits** | |
| Placa de vídeo | Compatível com **DirectX 11** (Intel UHD/Iris, NVIDIA, AMD) | Atualize o driver de vídeo. Para vários projetores ou 4K, prefira placa dedicada. |
| Memória | 8 GB (16 GB recomendado) | |
| Disco | 500 MB para o programa + espaço para seus vídeos | |
| Monitores | 1 para o operador + 1 por projetor/telão + 1 para o retorno (opcional) | Configure o Windows em **Estender estas telas**. |

---

## Caminho A — Usar o programa (recomendado)

### 1. Instale o Microsoft Edge WebView2 Runtime
Já vem no Windows 11 e na maioria dos Windows 10 atualizados. Se a janela do VJApp abrir em branco, instale:
- https://developer.microsoft.com/microsoft-edge/webview2/ → **Evergreen Bootstrapper** → baixar e executar.

### 2. Baixe o VJApp
1. Entre em https://github.com/DanielScharp/vjapp/releases/latest (o repositório é **privado**: faça login no GitHub com a conta **DanielScharp**) e baixe o arquivo **`VJApp-vX.Y.Z-win-x64.zip`** da versão mais recente.
2. Clique com o botão direito no zip → **Propriedades** → marque **Desbloquear** (se aparecer) → OK.
3. Extraia para uma pasta fixa, por exemplo `C:\VJApp` (evite a pasta Downloads e pastas sincronizadas com OneDrive).

### 3. Abra
- Execute **`VJApp.exe`**. Na primeira vez o Windows pode mostrar *"O Windows protegeu o computador"* → **Mais informações** → **Executar assim mesmo** (o programa não é assinado digitalmente).
- O **Firewall do Windows** vai perguntar sobre acesso à rede: marque **Redes privadas** e permita. Isso é necessário para o **controle pelo celular** (porta 8080), **OSC** (porta 7000) e **NDI**.
- Na primeira vez, o VJApp cria o atalho **VJApp** na área de trabalho e passa a abrir os arquivos `.vjproj` com duplo clique.

### Projetos e salvamento automático
- **Salvar** grava tudo num arquivo `.vjproj`: grade de clips, efeitos, transições, BPM, mapeamento, **letras** (músicas, cenas, estilos, transições e palavras animadas), **tela de retorno** e **ataques**.
- Os **vídeos e imagens** usados são copiados para a pasta **`<nome do projeto> - mídia`**, ao lado do `.vjproj`. Para levar o show para outro computador, copie o arquivo e essa pasta juntos.
- Com um projeto aberto, o VJApp **salva sozinho a cada minuto** quando algo mudou. Sem projeto aberto, guarda uma cópia em `%AppData%\VJApp\autosave`.

Pronto. O .NET, o FFmpeg (vídeo, HAP, DXV) e o restante já estão dentro da pasta do programa.

### Bíblia
Na aba **Bíblia**, clique em **Versões…** e baixe as que quiser (precisa de internet só nessa hora). As versões ficam em `%AppData%\VJApp\biblia` e funcionam sem internet. A lista vem da Free Use Bible API (bible.helloao.org) e tem só versões livres. ARC, ARA, NAA, NVI, NVT, NTLH e outras com direitos autorais entram por **Versões… → Importar de arquivo**, a partir de um arquivo que você já tenha licenciado (MySword .bbl.mybible, MyBible .SQLite3, Zefania XML, OSIS XML ou JSON).

### Mapeamento e painéis de LED
- **Ímã** (botão 🧲 na aba Mapeamento): ao arrastar, as pontas grudam nas bordas e cantos da tela e nos outros slices e máscaras. Segure **Alt** para inverter na hora.
- **Painel de LED**: escolha um modelo (ex.: 2 laterais verticais + centro), informe os pixels de cada parte como estão na processadora e arraste as partes no mapa do palco. Em **Separados** cada parte recebe uma layer, um grupo de layers ou a composição; em **Unido** uma imagem só atravessa todas as partes conforme o mapa.
- **Grupos de layers**: junte layers (ex.: "Centro" = layers 2 e 3) para mandar a um slice ou parte do LED.

### Fontes
Na aba **Letras**, painel de estilo, clique em **Mais fontes**: digite o nome de uma fonte do Google Fonts (ex.: Poppins) e clique em **Baixar**, ou importe arquivos .ttf, .otf ou o .zip. As fontes ficam em `%AppData%\VJApp\fontes` (não precisa instalar no Windows) e servem para as letras e a Bíblia.

### 4. Opcionais (instale só o que for usar)

| Para quê | O que instalar | Link |
|---|---|---|
| Converter vídeos para **HAP** (mais leve, recomendado) | FFmpeg (linha de comando) | https://www.gyan.dev/ffmpeg/builds/ (baixe *ffmpeg-release-essentials.zip*) ou `winget install Gyan.FFmpeg` |
| Mandar/receber vídeo pela rede via **NDI** | NDI Tools (inclui o NDI Runtime) | https://ndi.video/tools/ |
| **OBS** | OBS Studio | https://obsproject.com/ |
| OBS ↔ VJApp por **Spout** | Plugin OBS Spout2 (Off World Live) | https://github.com/Off-World-Live/obs-spout2-plugin/releases |
| OBS ↔ VJApp por **NDI** | Plugin DistroAV (+ NDI Tools) | https://distroav.org/ |
| Controladora **MIDI** | Driver do fabricante (a maioria é plug-and-play) | site do fabricante |

Converter um vídeo para HAP (no Prompt de Comando, na pasta do vídeo):
```
ffmpeg -i "meu video.mp4" -c:v hap -format hap_q "meu video HAP.mov"
```
(`-format hap` = menor arquivo; `hap_q` = melhor qualidade; `hap_alpha` = com transparência)

### 5. Levar suas configurações de um PC para outro
Tudo o que você configura fica em **`%AppData%\VJApp`** (cole esse caminho na barra do Explorador de Arquivos):

| Arquivo | Conteúdo |
|---|---|
| `composicao.json` | grade de clips, efeitos, transições, BPM |
| `mapeamento.json` e `presets\` | telas, slices, máscaras e presets de palco |
| `letras.json` | músicas, cenas e estilo das letras |
| `retorno.json` | layout da tela de retorno |
| `ataques.json` | ataques e teclas de atalho |
| `midi.json` | mapeamentos MIDI |

Copie a pasta inteira para o mesmo lugar no outro computador. Os **vídeos** não ficam aí; o jeito mais simples de levar tudo é **Salvar** um projeto e copiar o `.vjproj` com a pasta `- mídia`.

### Atualizar para uma versão nova
Baixe o zip novo da página **Releases** e extraia **por cima** da pasta antiga (com o VJApp fechado). Suas configurações em `%AppData%\VJApp` são mantidas.

---

## Caminho B — Compilar a partir do código

### Dependências

| Ferramenta | Versão | Link | winget |
|---|---|---|---|
| Git | qualquer recente | https://git-scm.com/download/win | `winget install Git.Git` |
| .NET SDK | **10.0** | https://dotnet.microsoft.com/download/dotnet/10.0 | `winget install Microsoft.DotNet.SDK.10` |
| Node.js | **22 LTS** ou mais novo | https://nodejs.org/ | `winget install OpenJS.NodeJS.LTS` |
| WebView2 Runtime | Evergreen | https://developer.microsoft.com/microsoft-edge/webview2/ | `winget install Microsoft.EdgeWebView2Runtime` |
| (opcional) FFmpeg CLI | — | https://www.gyan.dev/ffmpeg/builds/ | `winget install Gyan.FFmpeg` |
| (opcional) Visual Studio 2026 ou VS Code | — | https://visualstudio.microsoft.com/ | — |

Ou rode o script que instala tudo de uma vez (PowerShell):
```powershell
powershell -ExecutionPolicy Bypass -File scripts\instalar-dependencias.ps1
```
Feche e abra o terminal depois de instalar.

As bibliotecas do programa (Vortice/Direct3D 11, FFmpeg, WebView2, NAudio, DryWetMIDI, Snappier, React, Vite) são baixadas sozinhas pelo `dotnet` (NuGet) e pelo `npm` na primeira compilação.

### Baixar, compilar e rodar
```powershell
git clone https://github.com/DanielScharp/vjapp.git
cd vjapp
dotnet run --project src/VJApp
```
A primeira compilação demora alguns minutos (baixa pacotes e compila a interface).

### Gerar o pacote para outros computadores (Caminho A)
```powershell
powershell -ExecutionPolicy Bypass -File scripts\gerar-pacote.ps1
```
Cria `artifacts\VJApp-vX.Y.Z-win-x64.zip` com tudo dentro (não exige .NET nem Node no PC de destino).

---

## Problemas comuns

| Sintoma | Solução |
|---|---|
| Janela do VJApp em branco | Instale o WebView2 Runtime (passo A.1). |
| "Motor de vídeo: ..." / tela preta | Atualize o driver da placa de vídeo; confirme suporte a DirectX 11 (`dxdiag`). |
| Vídeo travando com várias layers | Converta para **HAP** (passo A.4); reduza para 1080p. |
| Celular não abre o controle remoto | Celular e PC na mesma rede Wi-Fi; permita o VJApp no Firewall (Redes privadas). |
| NDI desabilitado | Instale o NDI Tools e reabra o VJApp. |
| Sem som dos clips | O áudio sai no dispositivo padrão do Windows (Configurações → Som). |
| Câmera Virtual do OBS sem imagem | No OBS, clique em **Iniciar câmera virtual**. |
