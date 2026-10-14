# Instala as ferramentas para compilar o VJApp (Caminho B do INSTALAR.md) usando o winget.
# Uso: powershell -ExecutionPolicy Bypass -File scripts\instalar-dependencias.ps1

$pacotes = @(
    @{ Id = 'Git.Git';                       Nome = 'Git' },
    @{ Id = 'Microsoft.DotNet.SDK.10';       Nome = '.NET 10 SDK' },
    @{ Id = 'OpenJS.NodeJS.LTS';             Nome = 'Node.js LTS' },
    @{ Id = 'Microsoft.EdgeWebView2Runtime'; Nome = 'WebView2 Runtime' },
    @{ Id = 'Gyan.FFmpeg';                   Nome = 'FFmpeg (opcional, para converter vídeos em HAP)' }
)

if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
    Write-Host 'O winget não foi encontrado. Instale o "Instalador de Aplicativo" pela Microsoft Store e rode de novo,' -ForegroundColor Yellow
    Write-Host 'ou instale manualmente pelos links do INSTALAR.md.' -ForegroundColor Yellow
    exit 1
}

foreach ($p in $pacotes) {
    Write-Host "`n==> $($p.Nome)" -ForegroundColor Cyan
    winget install --id $p.Id --exact --silent --accept-package-agreements --accept-source-agreements
}

Write-Host "`nPronto. Feche e abra o terminal e rode:  dotnet run --project src/VJApp" -ForegroundColor Green
