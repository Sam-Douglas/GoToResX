<#
.SYNOPSIS
    Turns this template into your extension: renames MyExtension everywhere and gives it new GUIDs.

.EXAMPLE
    ./Initialize-Extension.ps1 -Name ShowPathOnTabs -DisplayName "Show Path On Tabs" -Publisher "Jane Doe" -GitHubRepo jane/ShowPathOnTabs

    Run once, from the repository root, straight after creating your repository from the template.
    The script deletes itself when it's done.
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z_][A-Za-z0-9_.]*$')][string]$Name,
    [Parameter(Mandatory)][string]$DisplayName,
    [Parameter(Mandatory)][string]$Publisher,
    [string]$GitHubRepo
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# The package, command set and VSIX identity GUIDs. Each extension needs its own.
$guids = @(
    '0f7ad5b9-c891-48fd-91ac-48bf0413e5ab',
    'cdb7a5cc-f1d9-49d9-b316-4e935e683a98',
    'd9824dc7-5285-4e88-b6ce-18e735f9a3ac'
)

$replacements = [ordered]@{
    'your-account/MyExtension' = $(if ($GitHubRepo) { $GitHubRepo } else { 'your-account/MyExtension' })
    'MyExtension'              = $Name
    'My Extension'             = $DisplayName
    'Your Name'                = $Publisher
    'YEAR'                     = (Get-Date).Year.ToString()
}
foreach ($g in $guids) { $replacements[$g] = [guid]::NewGuid().ToString() }

$textExtensions = '.cs', '.csproj', '.slnx', '.vsct', '.vsixmanifest', '.json', '.yaml', '.yml', '.md', '.txt'
$files = Get-ChildItem -Recurse -File | Where-Object {
    $_.FullName -notmatch '[\\/](\.git|\.vs|bin|obj)[\\/]' -and
    $textExtensions -contains $_.Extension -and
    $_.Name -ne 'Initialize-Extension.ps1'
}

$utf8 = New-Object System.Text.UTF8Encoding $false
foreach ($file in $files) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    $updated = $text
    foreach ($key in $replacements.Keys) { $updated = $updated.Replace($key, $replacements[$key]) }
    if ($updated -ne $text) { [System.IO.File]::WriteAllText($file.FullName, $updated, $utf8) }
}

# Rename files, then folders (deepest first so parent paths stay valid).
Get-ChildItem -Recurse -File -Filter '*MyExtension*' | Where-Object { $_.FullName -notmatch '[\\/]\.git[\\/]' } |
    ForEach-Object { Rename-Item $_.FullName ($_.Name.Replace('MyExtension', $Name)) }
Get-ChildItem -Recurse -Directory -Filter '*MyExtension*' | Where-Object { $_.FullName -notmatch '[\\/]\.git[\\/]' } |
    Sort-Object { $_.FullName.Length } -Descending |
    ForEach-Object { Rename-Item $_.FullName ($_.Name.Replace('MyExtension', $Name)) }

Remove-Item $PSCommandPath
Write-Host "Done. Open $Name.slnx, then fill in the description, tags and vs-publish.json publisher." -ForegroundColor Green
