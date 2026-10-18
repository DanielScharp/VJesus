# Gera o pacote pronto para usar (não exige .NET nem Node no computador de destino).
# Uso: powershell -ExecutionPolicy Bypass -File scripts\gerar-pacote.ps1 [-Versao 1.0.0]
param([string]$Versao = '1.0.0')

$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot -Parent
$saida = Join-Path $raiz "artifacts\VJesus"
$zip = Join-Path $raiz "artifacts\VJesus-v$Versao-win-x64.zip"

if (Test-Path $saida) { Get-ChildItem $saida -Recurse | Remove-Item -Recurse -Force }

dotnet publish (Join-Path $raiz 'src\VJApp') -c Release -r win-x64 --self-contained true -p:Version=$Versao -o $saida
if ($LASTEXITCODE -ne 0) { throw 'Falha no dotnet publish' }

Compress-Archive -Path "$saida\*" -DestinationPath $zip -CompressionLevel Optimal -Force
Write-Host ("Pacote criado: {0} ({1:N0} MB)" -f $zip, ((Get-Item $zip).Length / 1MB)) -ForegroundColor Green

# instalador (.msi): instala para o usuário, com atalhos, desinstalar pelo Windows e atualização por cima
$msi = Join-Path $raiz "artifacts\VJesus-v$Versao-instalador.msi"
Push-Location $raiz
dotnet tool restore | Out-Null
dotnet wix extension add WixToolset.UI.wixext/5.0.2 WixToolset.Util.wixext/5.0.2 | Out-Null
dotnet wix build installer\VJesus.wxs -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext -culture pt-BR -d Version=$Versao -bindpath App=$saida -o $msi
$ok = $LASTEXITCODE -eq 0
Pop-Location
if (-not $ok) { throw 'Falha ao gerar o instalador' }
Write-Host ("Instalador criado: {0} ({1:N0} MB)" -f $msi, ((Get-Item $msi).Length / 1MB)) -ForegroundColor Green

# instalador (.exe): a tela de instalação mais simples, com termos e botão "Abrir"; é o que vai para o site
$exe = Join-Path $raiz "artifacts\VJesus-v$Versao-instalador.exe"
Push-Location $raiz
dotnet wix extension add WixToolset.Bal.wixext/5.0.2 | Out-Null
# instalador oficial do WebView2 (1,7 MB, da Microsoft): só roda se o computador não tiver o WebView2
$wv2 = Join-Path $raiz 'artifacts\MicrosoftEdgeWebview2Setup.exe'
if (-not (Test-Path $wv2)) { Invoke-WebRequest 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $wv2 -UseBasicParsing }
# a extensão BAL 5.0.2 tem o .dll com outro nome; passar o caminho direto evita o "damaged"
$bal = Get-ChildItem (Join-Path $raiz '.wix\extensions\WixToolset.Bal.wixext') -Recurse -Filter 'WixToolset.*.wixext.dll' | Select-Object -First 1
if (-not $bal) { Pop-Location; throw 'Extensão WixToolset.Bal.wixext não encontrada' }
dotnet wix build installer\Instalador.wxs -ext $bal.FullName -ext WixToolset.Util.wixext -d Version=$Versao -d Msi=$msi -d WebView2=$wv2 -o $exe
$ok = $LASTEXITCODE -eq 0
Pop-Location
if (-not $ok) { throw 'Falha ao gerar o instalador .exe' }
Write-Host ("Instalador .exe criado: {0} ({1:N0} MB)" -f $exe, ((Get-Item $exe).Length / 1MB)) -ForegroundColor Green
# cópias com nome fixo: o link ".../releases/latest/download/VJesus-Instalador.exe" sempre baixa a última versão
Copy-Item $exe (Join-Path $raiz 'artifacts\VJesus-Instalador.exe') -Force
Copy-Item $zip (Join-Path $raiz 'artifacts\VJesus-Portatil.zip') -Force