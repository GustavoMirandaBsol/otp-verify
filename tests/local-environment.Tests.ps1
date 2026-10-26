param(
    [ValidateSet('All', 'Utf8', 'Redaction', 'Reload')]
    [string]$Case = 'All'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$launcherPath = Join-Path $repoRoot 'launch-backend.ps1'
$variableNames = @(
    'TWILIO_ACCOUNT_SID',
    'TWILIO_AUTH_TOKEN',
    'TWILIO_VERIFY_SERVICE_SID',
    'OTP_SUPPORT_PASSWORD'
)

function Assert-Equal([object]$Expected, [object]$Actual, [string]$Message) {
    if ($Expected -ne $Actual) {
        throw "$Message Expected: '$Expected'. Actual: '$Actual'."
    }
}

function Invoke-LauncherFixture {
    param(
        [string[]]$EnvironmentLines,
        [hashtable]$PresetEnvironment = @{},
        [switch]$WithoutEnvironmentFile,
        [switch]$Utf8NoBom
    )

    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $fixtureRoot = [IO.Path]::GetFullPath((Join-Path $tempRoot ("otp-support-env-" + [guid]::NewGuid().ToString('N'))))
    if (-not $fixtureRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "La ruta temporal salió del directorio esperado: $fixtureRoot"
    }

    $backendPath = Join-Path $fixtureRoot 'backend'
    $projectPath = Join-Path $backendPath 'OtpSupport.Api'
    New-Item -ItemType Directory -Force -Path $projectPath | Out-Null
    Copy-Item -LiteralPath $launcherPath -Destination (Join-Path $fixtureRoot 'launch-backend.ps1')
    if (-not $WithoutEnvironmentFile) {
        $environmentPath = Join-Path $backendPath '.env.local'
        if ($Utf8NoBom) {
            [IO.File]::WriteAllLines($environmentPath, $EnvironmentLines, (New-Object Text.UTF8Encoding($false)))
        }
        else {
            Set-Content -LiteralPath $environmentPath -Value $EnvironmentLines -Encoding UTF8
        }
    }

    $setupLines = foreach ($name in $variableNames) {
        "Remove-Item -LiteralPath 'Env:$name' -ErrorAction SilentlyContinue"
    }
    foreach ($entry in $PresetEnvironment.GetEnumerator()) {
        $escapedValue = [string]$entry.Value -replace "'", "''"
        $setupLines += "`$env:$($entry.Key) = '$escapedValue'"
    }

    $fixtureLauncher = (Join-Path $fixtureRoot 'launch-backend.ps1') -replace "'", "''"
    $childScript = @"
`$ErrorActionPreference = 'Stop'
$($setupLines -join [Environment]::NewLine)
function global:dotnet {
    [ordered]@{
        AccountSid = `$env:TWILIO_ACCOUNT_SID
        AuthToken = `$env:TWILIO_AUTH_TOKEN
        VerifyServiceSid = `$env:TWILIO_VERIFY_SERVICE_SID
        Password = `$env:OTP_SUPPORT_PASSWORD
    } | ConvertTo-Json -Compress
}
. '$fixtureLauncher'
"@
    $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($childScript))

    try {
        $previousErrorActionPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand $encodedCommand 2>&1
        $exitCode = $LASTEXITCODE
        $ErrorActionPreference = $previousErrorActionPreference
        return [pscustomobject]@{
            ExitCode = $exitCode
            Output = ($output | Out-String)
        }
    }
    finally {
        $ErrorActionPreference = 'Stop'
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}

function Invoke-ReloadFixture {
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $fixtureRoot = [IO.Path]::GetFullPath((Join-Path $tempRoot ("otp-support-reload-" + [guid]::NewGuid().ToString('N'))))
    if (-not $fixtureRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "La ruta temporal salió del directorio esperado: $fixtureRoot"
    }

    $backendPath = Join-Path $fixtureRoot 'backend'
    $projectPath = Join-Path $backendPath 'OtpSupport.Api'
    $environmentPath = Join-Path $backendPath '.env.local'
    $fixtureLauncher = Join-Path $fixtureRoot 'launch-backend.ps1'
    New-Item -ItemType Directory -Force -Path $projectPath | Out-Null
    Copy-Item -LiteralPath $launcherPath -Destination $fixtureLauncher

    $firstEnvironment = @(
        'TWILIO_ACCOUNT_SID=AC_from_file',
        'TWILIO_AUTH_TOKEN=first-token',
        'TWILIO_VERIFY_SERVICE_SID=VA_first',
        'OTP_SUPPORT_PASSWORD=first-password-1234'
    )
    [IO.File]::WriteAllLines($environmentPath, $firstEnvironment, (New-Object Text.UTF8Encoding($false)))

    $escapedLauncher = $fixtureLauncher -replace "'", "''"
    $escapedEnvironment = $environmentPath -replace "'", "''"
    $childScript = @"
`$ErrorActionPreference = 'Stop'
Remove-Item -LiteralPath 'Env:TWILIO_AUTH_TOKEN' -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'Env:TWILIO_VERIFY_SERVICE_SID' -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'Env:OTP_SUPPORT_PASSWORD' -ErrorAction SilentlyContinue
`$env:TWILIO_ACCOUNT_SID = 'AC_from_terminal'
`$script:observedPasswords = @()
function global:dotnet { `$script:observedPasswords += `$env:OTP_SUPPORT_PASSWORD }
. '$escapedLauncher'
`$secondEnvironment = @(
    'TWILIO_ACCOUNT_SID=AC_from_file',
    'TWILIO_AUTH_TOKEN=second-token',
    'TWILIO_VERIFY_SERVICE_SID=VA_second',
    'OTP_SUPPORT_PASSWORD=second-password-1234'
)
[IO.File]::WriteAllLines('$escapedEnvironment', `$secondEnvironment, (New-Object Text.UTF8Encoding(`$false)))
. '$escapedLauncher'
[ordered]@{
    Passwords = @(`$script:observedPasswords)
    ResidualPassword = `$env:OTP_SUPPORT_PASSWORD
    ResidualAccountSid = `$env:TWILIO_ACCOUNT_SID
} | ConvertTo-Json -Compress
"@
    $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($childScript))

    try {
        $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand $encodedCommand 2>&1
        return [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Output = ($output | Out-String)
        }
    }
    finally {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}

$loaded = Invoke-LauncherFixture -EnvironmentLines @(
    '# comentario',
    '',
    'TWILIO_ACCOUNT_SID=AC_from_file',
    'TWILIO_AUTH_TOKEN="token=with=equals"',
    'TWILIO_VERIFY_SERVICE_SID=VA_from_file',
    "OTP_SUPPORT_PASSWORD='local-password-1234'"
) -PresetEnvironment @{ TWILIO_ACCOUNT_SID = 'AC_from_terminal' }

Assert-Equal 0 $loaded.ExitCode 'El launcher debe cargar el archivo local.'
$jsonLine = ($loaded.Output -split "`r?`n" | Where-Object { $_ -match '^\{' } | Select-Object -Last 1)
if (-not $jsonLine) { throw "El launcher no ejecutó dotnet con el entorno cargado. Salida: $($loaded.Output)" }
$values = $jsonLine | ConvertFrom-Json
Assert-Equal 'AC_from_terminal' $values.AccountSid 'La sesión debe tener prioridad.'
Assert-Equal 'token=with=equals' $values.AuthToken 'El valor debe conservar los signos igual y retirar comillas.'
Assert-Equal 'VA_from_file' $values.VerifyServiceSid 'Debe cargar el Verify Service SID.'
Assert-Equal 'local-password-1234' $values.Password 'Debe cargar la contraseña local y retirar comillas.'

$missingFile = Invoke-LauncherFixture -WithoutEnvironmentFile
Assert-Equal 1 $missingFile.ExitCode 'Debe fallar cuando no existe backend/.env.local.'
if ($missingFile.Output -notmatch '\.env\.local') {
    throw "El error por archivo ausente debe indicar .env.local. Salida: $($missingFile.Output)"
}

$missingVariable = Invoke-LauncherFixture -EnvironmentLines @(
    'TWILIO_ACCOUNT_SID=AC_example',
    'TWILIO_VERIFY_SERVICE_SID=VA_example',
    'OTP_SUPPORT_PASSWORD=local-password-1234'
)
Assert-Equal 1 $missingVariable.ExitCode 'Debe fallar cuando falta una variable requerida.'
if ($missingVariable.Output -notmatch 'TWILIO_AUTH_TOKEN') {
    throw "El error debe nombrar TWILIO_AUTH_TOKEN. Salida: $($missingVariable.Output)"
}

if ($Case -in @('All', 'Utf8')) {
    $utf8Password = 'contraseña-local-1234'
    $utf8 = Invoke-LauncherFixture -Utf8NoBom -EnvironmentLines @(
        'TWILIO_ACCOUNT_SID=AC_example',
        'TWILIO_AUTH_TOKEN=token-example',
        'TWILIO_VERIFY_SERVICE_SID=VA_example',
        "OTP_SUPPORT_PASSWORD=$utf8Password"
    )
    Assert-Equal 0 $utf8.ExitCode 'Debe leer un archivo UTF-8 sin BOM.'
    $utf8Json = ($utf8.Output -split "`r?`n" | Where-Object { $_ -match '^\{' } | Select-Object -Last 1) | ConvertFrom-Json
    Assert-Equal $utf8Password $utf8Json.Password 'Debe conservar caracteres UTF-8 sin BOM.'
}

if ($Case -in @('All', 'Redaction')) {
    $secretThatMustNotLeak = 'secret-value-that-must-not-appear'
    $malformed = Invoke-LauncherFixture -EnvironmentLines @(
        'TWILIO_ACCOUNT_SID=AC_example',
        "TWILIO_AUTH_TOKEN: $secretThatMustNotLeak",
        'TWILIO_VERIFY_SERVICE_SID=VA_example',
        'OTP_SUPPORT_PASSWORD=local-password-1234'
    )
    Assert-Equal 1 $malformed.ExitCode 'Debe rechazar una línea dotenv sin separador igual.'
    if ($malformed.Output -match [regex]::Escape($secretThatMustNotLeak)) {
        throw "El error de formato expuso el valor secreto: $($malformed.Output)"
    }
}

if ($Case -in @('All', 'Reload')) {
    $reload = Invoke-ReloadFixture
    Assert-Equal 0 $reload.ExitCode 'Dos arranques válidos deben completarse en la misma terminal.'
    $reloadJson = ($reload.Output -split "`r?`n" | Where-Object { $_ -match '^\{' } | Select-Object -Last 1) | ConvertFrom-Json
    Assert-Equal 'first-password-1234' $reloadJson.Passwords[0] 'El primer arranque debe usar el primer archivo.'
    Assert-Equal 'second-password-1234' $reloadJson.Passwords[1] 'El segundo arranque debe recargar el archivo editado.'
    Assert-Equal $null $reloadJson.ResidualPassword 'La contraseña importada no debe persistir tras cerrar el backend.'
    Assert-Equal 'AC_from_terminal' $reloadJson.ResidualAccountSid 'Una variable previa de la terminal debe conservarse.'
}

Write-Output 'PASS: carga local del backend verificada.'

