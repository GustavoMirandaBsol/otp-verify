$environmentFile = Join-Path $PSScriptRoot 'backend/.env.local'

if (-not (Test-Path -LiteralPath $environmentFile -PathType Leaf)) {
  Write-Error "No se encontró backend/.env.local. Copie backend/.env.example como backend/.env.local y complete sus valores."
  exit 1
}

$importedVariables = New-Object 'System.Collections.Generic.List[string]'
$lineNumber = 0

try {
  foreach ($rawLine in Get-Content -LiteralPath $environmentFile -Encoding UTF8) {
    $lineNumber++
    $line = $rawLine.Trim()
    if (-not $line -or $line.StartsWith('#')) { continue }

    $separator = $line.IndexOf('=')
    if ($separator -le 0) {
      Write-Error "Formato inválido en backend/.env.local (línea $lineNumber). Use NOMBRE=valor."
      exit 1
    }

    $name = $line.Substring(0, $separator).Trim()
    $value = $line.Substring($separator + 1).Trim()
    if ($name -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
      Write-Error "Nombre de variable inválido en backend/.env.local (línea $lineNumber). Use NOMBRE=valor."
      exit 1
    }

    if ($value.Length -ge 2) {
      $first = $value[0]
      $last = $value[$value.Length - 1]
      if (($first -eq '"' -and $last -eq '"') -or ($first -eq "'" -and $last -eq "'")) {
        $value = $value.Substring(1, $value.Length - 2)
      }
    }

    if ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($name, 'Process'))) {
      [Environment]::SetEnvironmentVariable($name, $value, 'Process')
      $importedVariables.Add($name)
    }
  }

  $requiredVariables = @(
    'TWILIO_ACCOUNT_SID',
    'TWILIO_AUTH_TOKEN',
    'TWILIO_VERIFY_SERVICE_SID',
    'OTP_SUPPORT_PASSWORD'
  )
  $missingVariables = @($requiredVariables | Where-Object {
    [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_, 'Process'))
  })

  if ($missingVariables.Count -gt 0) {
    Write-Error "Configure estas variables en backend/.env.local o en esta terminal: $($missingVariables -join ', ')."
    exit 1
  }

  Set-Location "$PSScriptRoot/backend/OtpSupport.Api"
  dotnet run
}
finally {
  foreach ($name in $importedVariables) {
    [Environment]::SetEnvironmentVariable($name, $null, 'Process')
  }
}
