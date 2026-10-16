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
