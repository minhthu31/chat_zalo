param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Can Docker Desktop. Cai va mo Docker Desktop, sau do chay lai.'
}
docker info --format '{{.ServerVersion}}' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Hay mo Docker Desktop, cho Docker san sang va chay lai.' }

if (-not (Test-Path -LiteralPath deploy/.env)) {
    $demoPassword = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    @('CHAT_DOMAIN=localhost', ('POSTGRES_PASSWORD=' + $demoPassword)) |
        Set-Content -Encoding ascii deploy/.env
}
if (-not (Get-Content deploy/.env | Where-Object { $_ -match '^CHAT_DOMAIN=localhost\s*$' })) {
    throw 'Demo nay can CHAT_DOMAIN=localhost. Khong tu ghi de cau hinh server cua ban.'
}

$composeArgs = @('compose', '--env-file', 'deploy/.env', '-f', 'deploy/compose.yml', '-f', 'deploy/compose.demo.yml')
if ($NoBuild) { & docker @composeArgs up -d } else { & docker @composeArgs up -d --build }
if ($LASTEXITCODE -ne 0) { throw 'Khong khoi dong duoc demo. Kiem tra log Docker.' }

for ($attempt = 0; $attempt -lt 40; $attempt++) {
    $tunnelLog = (& docker @composeArgs logs --no-color tunnel 2>&1 | Out-String)
    $urls = [regex]::Matches($tunnelLog, 'https://[a-z0-9-]+\.trycloudflare\.com')
    if ($urls.Count -gt 0) {
        $demoUrl = $urls[$urls.Count - 1].Value
        try {
            $health = Invoke-WebRequest -Uri ($demoUrl + '/health') -UseBasicParsing -TimeoutSec 10
            if ($health.StatusCode -ne 200) { throw 'Demo chua san sang.' }
        }
        catch {
            Start-Sleep -Seconds 2
            continue
        }
        New-Item -ItemType Directory -Force artifacts | Out-Null
        $demoUrl | Set-Content -Encoding ascii artifacts/demo-url.txt
        Write-Output ('LINK DUNG CHUNG: ' + $demoUrl)
        Write-Output 'Gui cung link nay cho ca nhom. Moi nguoi dang ky tai khoan tren link nay.'
        Write-Output 'Giu may va Docker dang bat. Link co the doi khi tunnel khoi dong lai.'
        exit 0
    }
    Start-Sleep -Seconds 2
}
throw 'Chua lay duoc link. Chay docker compose --env-file deploy/.env -f deploy/compose.yml -f deploy/compose.demo.yml logs tunnel de kiem tra.'
