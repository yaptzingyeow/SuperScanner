[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $DestinationDirectory
)

$ErrorActionPreference = 'Stop'
$modelUrl = 'https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx'
$directory = [System.IO.Path]::GetFullPath($DestinationDirectory)
[System.IO.Directory]::CreateDirectory($directory) | Out-Null
$destination = [System.IO.Path]::Combine($directory, 'u2netp.onnx')
if ([System.IO.File]::Exists($destination)) {
    throw "Candidate already exists. Remove it explicitly before downloading again."
}

$temporary = "$destination.download"
try {
    Invoke-WebRequest -Uri $modelUrl -OutFile $temporary
    [System.IO.File]::Move($temporary, $destination)
}
finally {
    if ([System.IO.File]::Exists($temporary)) {
        [System.IO.File]::Delete($temporary)
    }
}

$file = [System.IO.FileInfo]::new($destination)
$digest = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
[pscustomobject]@{
    Path = $file.FullName
    Bytes = $file.Length
    Sha256 = $digest
    Source = $modelUrl
} | Format-List
