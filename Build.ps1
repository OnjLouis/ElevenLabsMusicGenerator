[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputRoot,
    [string]$SigningKeyPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $root 'src'
$portable = Join-Path $OutputRoot 'portable'
$executable = Join-Path $portable 'ElevenLabsMusicGenerator.exe'
$archive = Join-Path $OutputRoot 'ElevenLabsMusicGenerator.zip'
$signature = $archive + '.sig'

if (Test-Path -LiteralPath $OutputRoot) {
    throw "OutputRoot must be a new, empty staging path: $OutputRoot"
}

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Could not find the .NET Framework C# compiler.'
}

New-Item -ItemType Directory -Path $portable | Out-Null
$sources = Get-ChildItem -LiteralPath $source -Filter '*.cs' | Sort-Object Name | ForEach-Object FullName
& $compiler /nologo /target:winexe /optimize+ "/out:$executable" /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll $sources
if ($LASTEXITCODE -ne 0) {
    throw 'Build failed.'
}

Copy-Item -LiteralPath (Join-Path $root 'Manual.html') -Destination $portable
Copy-Item -LiteralPath (Join-Path $root 'LICENSE.txt') -Destination $portable
Get-ChildItem -LiteralPath $portable | Compress-Archive -DestinationPath $archive -CompressionLevel Optimal

if ($SigningKeyPath) {
    if (-not (Test-Path -LiteralPath $SigningKeyPath -PathType Leaf)) {
        throw "Signing key not found: $SigningKeyPath"
    }
    $rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
    try {
        $rsa.FromXmlString([IO.File]::ReadAllText($SigningKeyPath))
        $bytes = [IO.File]::ReadAllBytes($archive)
        $hash = [Security.Cryptography.SHA256]::Create()
        try { $signed = $rsa.SignData($bytes, $hash) } finally { $hash.Dispose() }
        [IO.File]::WriteAllText($signature, [Convert]::ToBase64String($signed), (New-Object Text.UTF8Encoding($false)))
    }
    finally {
        $rsa.PersistKeyInCsp = $false
        $rsa.Dispose()
    }
}

Write-Host "Built $portable"
Write-Host "Packaged $archive"
if (Test-Path -LiteralPath $signature) { Write-Host "Signed $signature" }
