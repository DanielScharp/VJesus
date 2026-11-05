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

### Roteiro do culto, prévia, contagem e avisos
Na aba **Culto**, em **Roteiro**, monte a lista em ordem com músicas, versículos, clips, textos, avisos e contagens. A tecla **→** (ou espaço) avança tudo: dentro da música passa o slide e, no fim dela, entra o próximo item. Clicar num item mostra a **prévia** sem mandar ao ar; duplo clique, **▶ Ir** ou **Enter** manda. O roteiro é salvo junto com o projeto.

- **Letras:** ligue **Prévia primeiro** para clicar nos slides sem que eles vão ao ar; o slide entra com **Enter** ou **Enviar ao telão**. Na aba **Bíblia**, o versículo escolhido aparece na prévia.
- **Contagem e avisos:** na aba **Culto**, em **Contagem e avisos**. Eles entram por cima de tudo, sem precisar de clip na grade, e a contagem é o mesmo temporizador da tela de retorno. Em telas com um slice que usa só uma layer ou um grupo, eles não aparecem naquele slice.

### Atualizar pelo programa
Quando há versão nova, aparece um aviso no topo. Clique em **Baixar** e depois em **Instalar agora**: o VJesus fecha, instala e abre de novo. Nada é instalado sem o seu clique. Também dá para conferir em **Menu → Verificar atualizações**. A versão portátil só abre a página de download.

### Apresentações (PowerPoint e PDF)
Na grade, clique numa célula vazia e escolha **Apresentação (PowerPoint ou PDF)…**, ou arraste o arquivo .pptx ou .pdf para a grade. Um PDF é convertido pelo próprio VJesus (não precisa de outro programa), uma página por slide. O VJesus usa o PowerPoint instalado no computador para guardar cada slide como imagem (uma vez só); depois a apresentação funciona sem o PowerPoint. Clique no clip para começar e passe os slides pela barra que aparece acima da grade (setas, Page Up/Page Down ou as miniaturas). No roteiro do culto, um item com a apresentação passa slide a slide antes de ir para o próximo item. Slides com animação viram **etapas, uma por clique**, na mesma ordem do PowerPoint: cada item entra (ou sai) no clique certo, com a transição da layer no lugar do movimento original. Ênfases, trajetórias e vídeos dentro dos slides não são reproduzidos.

### Carrossel de colunas
Na aba **Clips e efeitos**, o botão **⟳ Carrossel** (acima da grade, à direita) faz a aba aberta passar sozinha de coluna em coluna. Escolha o tempo em cada coluna e se vale para **todas as layers** da aba ou só para as marcadas. Cada aba tem o seu carrossel, e ele continua rodando quando você abre outra aba.

### Bíblia
Na aba **Bíblia**, clique em **Versões…** e baixe as que quiser (precisa de internet só nessa hora). As versões ficam em `%AppData%\VJesus\biblia` e funcionam sem internet. A lista vem da Free Use Bible API (bible.helloao.org) e tem só versões livres. ARC, ARA, NAA, NVI, NVT, NTLH e outras com direitos autorais entram por **Versões… → Importar de arquivo**, a partir de um arquivo que você já tenha licenciado (MySword .bbl.mybible, MyBible .SQLite3, Zefania XML, OSIS XML ou JSON).

### Mapeamento e painéis de LED
- **Ímã** (botão 🧲 em Menu → Mapeamento): ao arrastar, as pontas grudam nas bordas e cantos da tela e nos outros slices e máscaras. Segure **Alt** para inverter na hora.
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

## Página da internet e espelho de tela na grade

Botão direito numa célula da grade:

- **Página da internet (link)…**: digite o endereço (site da igreja, placar, contagem, YouTube...). A página é aberta por um navegador escondido do tamanho do telão e atualiza cerca de 15 vezes por segundo (bom para páginas; vídeo dentro da página fica menos fluido que um vídeo da grade). Para fazer login, rolar ou clicar: botão direito na célula → **Mostrar a página para mexer**; feche a janela para escondê-la de novo (o telão continua mostrando).
- **Espelhar uma tela do computador**: escolha o monitor. A imagem vem direto da placa de vídeo (leve, 60 qps); se o monitor estiver em outra placa de vídeo, o VJesus copia pela tela do Windows (~20 qps). Não espelhe o próprio monitor do telão (a imagem entra nela mesma).

---

## Holyrics → VJesus (letras e Bíblia)

Quem já usa o Holyrics pode continuar passando as letras e os versículos nele: o VJesus mostra no telão o slide que o Holyrics está projetando, por cima dos vídeos, com a aparência das Letras (versículo: a da Bíblia).

1. No Holyrics: **Arquivo → Configurações → API Server**, ative o servidor e anote a porta (normalmente 8091).
2. Em **Gerenciar permissões**, crie um token com a permissão **GetCurrentPresentation**.
3. No VJesus: **Menu → Conexões → Holyrics**, coloque o IP do computador do Holyrics (127.0.0.1 se for o mesmo), a porta e o token, e marque **Receber do Holyrics**. Aparece **conectado**.

Tela em branco, papel de parede ou fim da música no Holyrics tiram o texto do telão do VJesus. Se o Holyrics estiver em outro computador, libere a porta dele no Firewall do Windows daquele computador.

---

## Outros computadores da rede (som, transmissão, pastor)

Qualquer computador, tablet ou celular na mesma rede acompanha o culto pelo navegador, sem instalar nada. Os endereços aparecem em **Menu → Conexões** (troque o IP pelo que aparecer lá):

- **`http://IP:8080/telao`**: a imagem do telão ao vivo. Clique duas vezes para tela cheia. Serve de monitor na mesa de som, de telão extra ou de captura no OBS (`/telao?limpo` esconde a dica).
- **`http://IP:8080/roteiro`**: a ordem do culto com o item no ar marcado, o texto que está no telão e o próximo slide. Bom para quem opera o som, a transmissão ou para o pastor.
- **`http://IP:8080/letra`**: só a letra, com fundo transparente, para a fonte **Navegador** do OBS (1920×1080) por cima da câmera. Ajustes no endereço: `posicao=baixo|meio|cima`, `tamanho=6`, `cor=ffffff`, `fundo=1`, `opacidade=0.6`, `fonte=Poppins`, `maiusculas=1`. Abra `/letra?ajuda` para ver as instruções.

Se o outro computador não abrir, libere o VJesus no Firewall do Windows (Redes privadas), como no controle pelo celular. Para levar a imagem em alta qualidade para a transmissão, use o NDI (Menu → Conexões).

---

## Tela de pânico e voltar onde estava

- O botão vermelho **PÂNICO** no topo (ou a tecla **F9**) cobre o telão na hora com o logo da igreja ou uma cor, por cima de vídeos, letra e avisos. Aperte de novo para voltar exatamente ao que estava.
- Na engrenagem ao lado do botão você escolhe o logo, a cor de fundo, o tamanho do logo e o tempo do esmaecer. A tecla muda em **Menu → Atalhos de teclado**.
- Se o VJesus fechar sem querer (queda de energia, travamento), ao abrir de novo ele recoloca no telão os clips que estavam tocando, na mesma posição, e avisa. Fechando pelo X, ele abre normal da próxima vez.
- Erros ficam anotados em `%AppData%\VJesus\erros.log`; se o programa cair por um erro interno, ele se reabre sozinho.

## Checagem antes do culto

Na aba **Culto** (botão **✔ Checar antes do culto**) ou em **Menu → Checar antes do culto**: confere de uma vez o roteiro (músicas e clips que sumiram), os arquivos da grade, os vídeos pesados para este PC, as telas de saída (monitor desligado, saída em cima do monitor do operador), as apresentações, o som, a tela de pânico e o espaço em disco. Arquivos que mudaram de lugar podem ser apontados de novo ali mesmo (**Procurar arquivo…** ou **Procurar todos numa pasta…**), e todos os clips que usavam o arquivo são corrigidos.

## Biblioteca de mídia

Na aba **Clips e efeitos**, embaixo da grade. Clique em **+ Pasta…** para incluir as pastas com os vídeos, imagens e apresentações da igreja (as subpastas entram junto); o que já está na grade aparece sozinho. Busque por nome, pasta ou tag, filtre por tipo, passe o mouse num vídeo para ver a prévia animada e **arraste para uma célula da grade**. O botão **#** marca o arquivo com tags (louvor, ceia, batismo...), salvas em `%AppData%\VJesus\biblioteca.json`.

---

## Desempenho do vídeo (placa de vídeo ou processador)

Em **Menu → Desempenho do vídeo** você escolhe onde os vídeos são decodificados neste computador:

- **Automático (recomendado):** vídeos pesados (maiores que Full HD, HEVC, AV1, VP9) vão para a placa de vídeo; os leves ficam no processador.
- **Placa de vídeo:** todos os vídeos que a placa aceita vão para ela.
- **Processador:** como era antes; use se algum driver der problema.

O botão **Testar este computador** toca os vídeos mais pesados da grade por alguns segundos, sem aparecer no telão, dos dois jeitos, e mostra a fluidez e o uso do processador de cada um com uma sugestão. A escolha fica salva no PC (não no projeto) e vale para os vídeos que começarem a tocar depois. A janela também mostra, para cada layer tocando, se o vídeo está na placa ou no processador.

---

## Problemas comuns

| Sintoma | Solução |
|---|---|
| Janela do VJesus em branco | Instale o WebView2 Runtime (passo A.1). |
| "Motor de vídeo: ..." / tela preta | Atualize o driver da placa de vídeo; confirme suporte a DirectX 11 (`dxdiag`). |
| Vídeo travando com várias layers | Menu → **Desempenho do vídeo** → **Testar este computador** e use a sugestão. Se ainda travar, converta para **HAP** (passo A.4). |
| Imagem estranha ou travada depois de trocar para "Placa de vídeo" | Menu → **Desempenho do vídeo** → **Processador** e atualize o driver da placa. |
| Celular não abre o controle remoto | Celular e PC na mesma rede Wi-Fi; permita o VJesus no Firewall (Redes privadas). |
| NDI desabilitado | Instale o NDI Tools e reabra o VJesus. |
| Sem som dos clips | O áudio sai no dispositivo padrão do Windows (Configurações → Som). |
| Câmera Virtual do OBS sem imagem | No OBS, clique em **Iniciar câmera virtual**. |
