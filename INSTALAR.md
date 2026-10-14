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
1. Abra a página do projeto no GitHub → **Releases** (à direita) → baixe **`VJApp-v1.0.0-win-x64.zip`**.
2. Clique com o botão direito no zip → **Propriedades** → marque **Desbloquear** (se aparecer) → OK.
3. Extraia para uma pasta fixa, por exemplo `C:\VJApp` (evite a pasta Downloads e pastas sincronizadas com OneDrive).

### 3. Abra
- Execute **`VJApp.exe`**. Na primeira vez o Windows pode mostrar *"O Windows protegeu o computador"* → **Mais informações** → **Executar assim mesmo** (o programa não é assinado digitalmente).
- O **Firewall do Windows** vai perguntar sobre acesso à rede: marque **Redes privadas** e permita. Isso é necessário para o **controle pelo celular** (porta 8080), **OSC** (porta 7000) e **NDI**.
- Dica: crie um atalho do `VJApp.exe` na área de trabalho.

Pronto. O .NET, o FFmpeg (vídeo, HAP, DXV) e o restante já estão dentro da pasta do programa.

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

Copie a pasta inteira para o mesmo lugar no outro computador. Os **vídeos** não ficam aí: copie-os também e, se mudarem de pasta, use **Abrir…** num projeto `.vjproj` ou arraste os vídeos de novo para a grade.

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
git clone https://github.com/<usuario>/<repositorio>.git
cd <repositorio>
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
