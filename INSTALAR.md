# Instalação do VJesus num computador novo

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

### 1. Baixe o instalador
**[⬇ Baixar o VJesus para Windows (VJesus-Instalador.exe)](https://github.com/DanielScharp/VJesus/releases/latest/download/VJesus-Instalador.exe)**

Esse link sempre baixa a versão mais nova. Se preferir escolher uma versão, as versões ficam na página **Releases** do GitHub (não é uma pasta do código): abra https://github.com/DanielScharp/VJesus, clique em **Releases** na coluna da direita (ou entre direto em https://github.com/DanielScharp/VJesus/releases) e, na versão desejada, abra **Assets** e clique no arquivo.

### 2. Instale
1. Dê dois cliques em **`VJesus-Instalador.exe`** (fica na pasta **Downloads**).
2. Se aparecer *"O Windows protegeu o computador"*, clique em **Mais informações** e depois em **Executar assim mesmo**. O aviso aparece porque o instalador ainda não tem assinatura digital paga; o arquivo é o mesmo publicado no GitHub.
3. Marque **Aceito os termos** e clique em **Instalar**. Não pede senha de administrador: instala só para o seu usuário, em `%LocalAppData%\Programs\VJesus`.
4. Clique em **Abrir** (ou use o atalho **VJesus** na área de trabalho / menu Iniciar).

O instalador já traz tudo (.NET, FFmpeg e o resto). Ele também confere o **Microsoft Edge WebView2**, que desenha as telas do programa: já vem no Windows 11 e quase todo Windows 10; se faltar, ele instala sozinho (nesse caso precisa de internet e pode pedir permissão do Windows). Os arquivos `.vjproj` passam a abrir no VJesus.

- **Atualizar:** baixe de novo pelo mesmo link e instale por cima. A versão antiga é trocada e suas configurações continuam.
- **Desinstalar:** *Configurações → Aplicativos → Aplicativos instalados → VJesus → Desinstalar*. Suas configurações em `%AppData%\VJesus` ficam guardadas (apague a pasta se quiser zerar tudo).
- **Instalação em rede ou silenciosa (TI):** `VJesus-Instalador.exe /quiet` instala sem telas; `/uninstall /quiet` remove. Para quem prefere, cada versão também tem o `.msi` em Assets.
- **Sem instalar (portátil):** **[VJesus-Portatil.zip](https://github.com/DanielScharp/VJesus/releases/latest/download/VJesus-Portatil.zip)**. Antes de extrair, botão direito no zip → Propriedades → **Desbloquear**; extraia numa pasta fixa (ex.: `C:\VJesus`) e execute `VJesus.exe`. No modo portátil o WebView2 não é instalado automaticamente: se o programa avisar que ele falta, clique em OK e instale pelo site da Microsoft (**Evergreen Bootstrapper**).

### 3. Primeira vez
- O **Firewall do Windows** vai perguntar sobre acesso à rede: marque **Redes privadas** e permita. Isso é necessário para o **controle pelo celular** (porta 8080), **OSC** (porta 7000) e **NDI**.
### Projetos e salvamento automático
- **Salvar** grava tudo num arquivo `.vjproj`: grade de clips, efeitos, transições, BPM, mapeamento, **letras** (músicas, cenas, estilos, transições e palavras animadas), **tela de retorno** e **ataques**.
- Os **vídeos e imagens** usados são copiados para a pasta **`<nome do projeto> - mídia`**, ao lado do `.vjproj`. Para levar o show para outro computador, copie o arquivo e essa pasta juntos.
- Com um projeto aberto, o VJesus **salva sozinho a cada minuto** quando algo mudou. Sem projeto aberto, guarda uma cópia em `%AppData%\VJesus\autosave`.

Pronto. O .NET, o FFmpeg (vídeo, HAP, DXV) e o restante já estão dentro da pasta do programa.

### Bíblia
Na aba **Bíblia**, clique em **Versões…** e baixe as que quiser (precisa de internet só nessa hora). As versões ficam em `%AppData%\VJesus\biblia` e funcionam sem internet. A lista vem da Free Use Bible API (bible.helloao.org) e tem só versões livres. ARC, ARA, NAA, NVI, NVT, NTLH e outras com direitos autorais entram por **Versões… → Importar de arquivo**, a partir de um arquivo que você já tenha licenciado (MySword .bbl.mybible, MyBible .SQLite3, Zefania XML, OSIS XML ou JSON).

### Mapeamento e painéis de LED
- **Ímã** (botão 🧲 na aba Mapeamento): ao arrastar, as pontas grudam nas bordas e cantos da tela e nos outros slices e máscaras. Segure **Alt** para inverter na hora.
- **Painel de LED**: escolha um modelo (ex.: 2 laterais verticais + centro), informe os pixels de cada parte como estão na processadora e arraste as partes no mapa do palco. Em **Separados** cada parte recebe uma layer, um grupo de layers ou a composição; em **Unido** uma imagem só atravessa todas as partes conforme o mapa.
- **Grupos de layers**: junte layers (ex.: "Centro" = layers 2 e 3) para mandar a um slice ou parte do LED.

### Fontes
Na aba **Letras**, painel de estilo, clique em **Mais fontes**: digite o nome de uma fonte do Google Fonts (ex.: Poppins) e clique em **Baixar**, ou importe arquivos .ttf, .otf ou o .zip. As fontes ficam em `%AppData%\VJesus\fontes` (não precisa instalar no Windows) e servem para as letras e a Bíblia.

### 4. Opcionais (instale só o que for usar)

| Para quê | O que instalar | Link |
|---|---|---|
| Converter vídeos para **HAP** (mais leve, recomendado) | FFmpeg (linha de comando) | https://www.gyan.dev/ffmpeg/builds/ (baixe *ffmpeg-release-essentials.zip*) ou `winget install Gyan.FFmpeg` |
| Mandar/receber vídeo pela rede via **NDI** | NDI Tools (inclui o NDI Runtime) | https://ndi.video/tools/ |
| **OBS** | OBS Studio | https://obsproject.com/ |
| OBS ↔ VJesus por **Spout** | Plugin OBS Spout2 (Off World Live) | https://github.com/Off-World-Live/obs-spout2-plugin/releases |
| OBS ↔ VJesus por **NDI** | Plugin DistroAV (+ NDI Tools) | https://distroav.org/ |
| Controladora **MIDI** | Driver do fabricante (a maioria é plug-and-play) | site do fabricante |

Converter um vídeo para HAP (no Prompt de Comando, na pasta do vídeo):
```
ffmpeg -i "meu video.mp4" -c:v hap -format hap_q "meu video HAP.mov"
```
(`-format hap` = menor arquivo; `hap_q` = melhor qualidade; `hap_alpha` = com transparência)

### 5. Levar suas configurações de um PC para outro
Tudo o que você configura fica em **`%AppData%\VJesus`** (cole esse caminho na barra do Explorador de Arquivos):

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
> O programa se chamava **VJApp**. Ao abrir o VJesus pela primeira vez, a pasta `%AppData%\VJApp` vira `%AppData%\VJesus` sozinha (nada se perde) e o atalho da área de trabalho é trocado.

Instale o `.msi` novo por cima (ou, na versão portátil, extraia o zip novo **por cima** da pasta antiga com o VJesus fechado). Suas configurações em `%AppData%\VJesus` são mantidas.

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
git clone https://github.com/DanielScharp/VJesus.git
cd VJesus
dotnet run --project src/VJApp
```
A primeira compilação demora alguns minutos (baixa pacotes e compila a interface).

### Gerar o pacote para outros computadores (Caminho A)
```powershell
powershell -ExecutionPolicy Bypass -File scripts\gerar-pacote.ps1
```
Cria `artifacts\VJesus-vX.Y.Z-win-x64.zip` com tudo dentro (não exige .NET nem Node no PC de destino).

---

## Problemas comuns

| Sintoma | Solução |
|---|---|
| Janela do VJesus em branco | Instale o WebView2 Runtime (passo A.1). |
| "Motor de vídeo: ..." / tela preta | Atualize o driver da placa de vídeo; confirme suporte a DirectX 11 (`dxdiag`). |
| Vídeo travando com várias layers | Converta para **HAP** (passo A.4); reduza para 1080p. |
| Celular não abre o controle remoto | Celular e PC na mesma rede Wi-Fi; permita o VJesus no Firewall (Redes privadas). |
| NDI desabilitado | Instale o NDI Tools e reabra o VJesus. |
| Sem som dos clips | O áudio sai no dispositivo padrão do Windows (Configurações → Som). |
| Câmera Virtual do OBS sem imagem | No OBS, clique em **Iniciar câmera virtual**. |
