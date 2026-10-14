# Gera o pacote pronto para usar (não exige .NET nem Node no computador de destino).
# Uso: powershell -ExecutionPolicy Bypass -File scripts\gerar-pacote.ps1 [-Versao 1.0.0]
param([string]$Versao = '1.0.0')

$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot -Parent
$saida = Join-Path $raiz "artifacts\VJApp"
$zip = Join-Path $raiz "artifacts\VJApp-v$Versao-win-x64.zip"

if (Test-Path $saida) { Get-ChildItem $saida -Recurse | Remove-Item -Recurse -Force }

dotnet publish (Join-Path $raiz 'src\VJApp') -c Release -r win-x64 --self-contained true -o $saida
if ($LASTEXITCODE -ne 0) { throw 'Falha no dotnet publish' }

Compress-Archive -Path "$saida\*" -DestinationPath $zip -CompressionLevel Optimal -Force
Write-Host ("Pacote criado: {0} ({1:N0} MB)" -f $zip, ((Get-Item $zip).Length / 1MB)) -ForegroundColor Green
