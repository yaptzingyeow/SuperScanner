param([switch]$Download)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$serverFonts = Join-Path $repository 'assets/fonts'
$webFonts = Join-Path $repository 'apps/web/public/assets/fonts'
$licenses = Join-Path $serverFonts 'licenses'
$manifestPath = Join-Path $serverFonts 'manifest.json'
$googleCommit = '23e54b51ddffbc7713c583748e3bd86f62b1fa4a'
$liberationArchive = Join-Path $repository '.task-tools/font-download/liberation-fonts-ttf-2.1.5.tar.gz'
$liberationArchiveHash = '7191c669bf38899f73a2094ed00f7b800553364f90e2637010a69c0e268f25d0'

$families = @(
    @{ Id='carlito'; Name='Carlito'; Folder='carlito'; Category='SansSerif'; Regular='Carlito-Regular.ttf'; Bold='Carlito-Bold.ttf' },
    @{ Id='roboto'; Name='Roboto'; Folder='roboto'; Category='SansSerif'; Regular='Roboto[wdth,wght].ttf' },
    @{ Id='open-sans'; Name='Open Sans'; Folder='opensans'; Category='SansSerif'; Regular='OpenSans[wdth,wght].ttf' },
    @{ Id='lato'; Name='Lato'; Folder='lato'; Category='SansSerif'; Regular='Lato-Regular.ttf'; Bold='Lato-Bold.ttf' },
    @{ Id='montserrat'; Name='Montserrat'; Folder='montserrat'; Category='SansSerif'; Regular='Montserrat[wght].ttf' },
    @{ Id='source-sans-3'; Name='Source Sans 3'; Folder='sourcesans3'; Category='SansSerif'; Regular='SourceSans3[wght].ttf' },
    @{ Id='poppins'; Name='Poppins'; Folder='poppins'; Category='SansSerif'; Regular='Poppins-Regular.ttf'; Bold='Poppins-Bold.ttf' },
    @{ Id='oswald'; Name='Oswald'; Folder='oswald'; Category='SansSerif'; Regular='Oswald[wght].ttf' },
    @{ Id='caladea'; Name='Caladea'; Folder='caladea'; Category='Serif'; Regular='Caladea-Regular.ttf'; Bold='Caladea-Bold.ttf' },
    @{ Id='source-serif-4'; Name='Source Serif 4'; Folder='sourceserif4'; Category='Serif'; Regular='SourceSerif4[opsz,wght].ttf' },
    @{ Id='merriweather'; Name='Merriweather'; Folder='merriweather'; Category='Serif'; Regular='Merriweather[opsz,wdth,wght].ttf' },
    @{ Id='libre-baskerville'; Name='Libre Baskerville'; Folder='librebaskerville'; Category='Serif'; Regular='LibreBaskerville[wght].ttf' },
    @{ Id='noto-sans-mono'; Name='Noto Sans Mono'; Folder='notosansmono'; Category='Monospace'; Regular='NotoSansMono[wdth,wght].ttf' },
    @{ Id='caveat'; Name='Caveat'; Folder='caveat'; Category='Handwriting'; Regular='Caveat[wght].ttf' },
    @{ Id='dancing-script'; Name='Dancing Script'; Folder='dancingscript'; Category='Handwriting'; Regular='DancingScript[wght].ttf' },
    @{ Id='liberation-sans'; Name='Liberation Sans'; Category='SansSerif'; Regular='LiberationSans-Regular.ttf'; Bold='LiberationSans-Bold.ttf'; Liberation=$true },
    @{ Id='liberation-serif'; Name='Liberation Serif'; Category='Serif'; Regular='LiberationSerif-Regular.ttf'; Bold='LiberationSerif-Bold.ttf'; Liberation=$true },
    @{ Id='liberation-mono'; Name='Liberation Mono'; Category='Monospace'; Regular='LiberationMono-Regular.ttf'; Bold='LiberationMono-Bold.ttf'; Liberation=$true }
)

function Copy-VerifiedFont($family, [string]$style, [string]$sourceName, [string]$sourceDirectory) {
    $localStem = $family.Name.Replace(' ', '')
    $localName = "$localStem-$style.ttf"
    $target = Join-Path $serverFonts $localName
    if ($family.Liberation) {
        Copy-Item -LiteralPath (Join-Path $sourceDirectory $sourceName) -Destination $target -Force
    } else {
        $encoded = [Uri]::EscapeDataString($sourceName)
        $url = "https://raw.githubusercontent.com/google/fonts/$googleCommit/ofl/$($family.Folder)/$encoded"
        Invoke-WebRequest -Uri $url -OutFile $target
    }
    Copy-Item -LiteralPath $target -Destination (Join-Path $webFonts $localName) -Force
    $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
    $webHash = (Get-FileHash -LiteralPath (Join-Path $webFonts $localName) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $webHash) { throw "Web/server font hash mismatch: $localName" }
    return @{ File=$localName; Hash=$hash }
}

if ($Download) {
    New-Item -ItemType Directory -Path $licenses -Force | Out-Null
    New-Item -ItemType Directory -Path $webFonts -Force | Out-Null
    if (-not (Test-Path -LiteralPath $liberationArchive)) {
        New-Item -ItemType Directory -Path (Split-Path $liberationArchive -Parent) -Force | Out-Null
        Invoke-WebRequest -Uri 'https://github.com/liberationfonts/liberation-fonts/files/7261482/liberation-fonts-ttf-2.1.5.tar.gz' `
            -OutFile $liberationArchive
    }
    if ((Get-FileHash -LiteralPath $liberationArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $liberationArchiveHash) {
        throw 'The official Liberation 2.1.5 release archive has an unexpected SHA-256 hash.'
    }
    $scratch = Join-Path $repository '.task-tools/font-download'
    & tar -xzf $liberationArchive -C $scratch
    if ($LASTEXITCODE -ne 0) { throw 'Could not extract the Liberation release.' }
    $liberationDirectory = Join-Path $scratch 'liberation-fonts-ttf-2.1.5'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
    foreach ($family in $families) {
        if (@($manifest.faces | Where-Object { $_.catalogueId -eq $family.Id }).Count -ne 0) {
            throw "Font family already exists in manifest: $($family.Id)"
        }
        $notice = Join-Path $licenses "$($family.Id)-OFL.txt"
        if ($family.Liberation) {
            Copy-Item -LiteralPath (Join-Path $liberationDirectory 'LICENSE') -Destination $notice -Force
        } else {
            $url = "https://raw.githubusercontent.com/google/fonts/$googleCommit/ofl/$($family.Folder)/OFL.txt"
            Invoke-WebRequest -Uri $url -OutFile $notice
        }
        $licenseText = Get-Content -LiteralPath $notice -Raw
        if ($licenseText -notmatch '(?is)SIL OPEN FONT LICENSE\s*Version 1\.1') {
            throw "Unexpected font license for $($family.Id)"
        }
        foreach ($style in @('Regular', 'Bold')) {
            if ($style -eq 'Bold' -and -not $family.Bold) { continue }
            $sourceName = if ($style -eq 'Bold') { $family.Bold } else { $family.Regular }
            $sourceDirectory = if ($family.Liberation) { $liberationDirectory } else { '' }
            $asset = Copy-VerifiedFont $family $style $sourceName $sourceDirectory
            $version = if ($family.Liberation) { 'liberation-2-1-5' } else { "gfonts-$($googleCommit.Substring(0,8))" }
            $manifest.faces += @{
                catalogueId = $family.Id
                version = "$version-$($style.ToLowerInvariant())"
                displayName = "$($family.Name) $style"
                familyName = $family.Name
                category = $family.Category
                weight = if ($style -eq 'Bold') { 700 } else { 400 }
                style = 'Normal'
                webFamilyName = "SuperScanner $($family.Name) v1"
                selectableForNewEdits = $true
                assetSha256Hex = $asset.Hash
                licenseIdentifier = 'OFL-1.1'
                licenseNoticePath = "assets/fonts/licenses/$($family.Id)-OFL.txt"
                webAssetPath = "assets/fonts/$($asset.File)"
                rendererAssetPath = "assets/fonts/$($asset.File)"
                enabled = $true
            }
        }
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$enabled = @($manifest.faces | Where-Object { $_.enabled })
$uniqueFamilies = @($enabled | Select-Object -ExpandProperty catalogueId -Unique)
if ($uniqueFamilies.Count -ne 20) { throw "Expected 20 enabled families, found $($uniqueFamilies.Count)." }
$referenced = @($manifest.faces | ForEach-Object { Split-Path $_.rendererAssetPath -Leaf })
foreach ($face in $manifest.faces) {
    $serverFile = Join-Path $serverFonts (Split-Path $face.rendererAssetPath -Leaf)
    $webFile = Join-Path $webFonts (Split-Path $face.webAssetPath -Leaf)
    $hash = (Get-FileHash -LiteralPath $serverFile -Algorithm SHA256).Hash.ToLowerInvariant()
    $webHash = (Get-FileHash -LiteralPath $webFile -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $face.assetSha256Hex -or $webHash -ne $hash) {
        throw "Hash mismatch for $($face.catalogueId)/$($face.version)"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $repository $face.licenseNoticePath))) {
        throw "Missing license for $($face.catalogueId)"
    }
}
$orphans = @(Get-ChildItem -LiteralPath $serverFonts -Filter '*.ttf' | Where-Object { $_.Name -notin $referenced })
if ($orphans.Count -gt 0) { throw "Unreferenced font files: $($orphans.Name -join ', ')" }
Write-Host "Verified $($enabled.Count) faces across $($uniqueFamilies.Count) enabled families."
