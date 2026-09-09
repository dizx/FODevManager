[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $PublishDir,
    [string] $Configuration = 'Release',
    [string] $RuntimeIdentifier = 'win-x64',
    [ValidateSet('true', 'false')]
    [string] $SelfContained = 'true',
    [ValidateSet('true', 'false')]
    [string] $PublishReadyToRun = 'true',
    [ValidateSet('true')]
    [string] $UseAppHost = 'true',
    [switch] $SanitizeOnly
)

$ErrorActionPreference = 'Stop'
$publishPath = [System.IO.Path]::GetFullPath($PublishDir)
if ($publishPath -eq [System.IO.Path]::GetPathRoot($publishPath)) {
    throw 'PublishDir must not be a filesystem root'
}
$publishPath = $publishPath.TrimEnd('\', '/')
$cliProject = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\FODevManager\FODevManager.csproj'))
$sourceDirectories = @($PSScriptRoot, [System.IO.Path]::GetDirectoryName($cliProject))
if (-not [System.IO.Path]::IsPathRooted($PublishDir) -or $publishPath -in $sourceDirectories) {
    throw 'PublishDir must be an absolute output directory, not an application source directory'
}
if (-not (Test-Path -LiteralPath $publishPath -PathType Container)) {
    throw 'The WinUI publish directory does not exist'
}

$settingsPath = Join-Path $publishPath 'appsettings.json'
$nugetPath = Join-Path $publishPath 'nuget.config'
$developmentPath = Join-Path $publishPath 'appsettings.Development.json'
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Protect-PublishedConfiguration {
    Remove-Item -LiteralPath $developmentPath -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $developmentPath) {
        throw 'Could not remove published development configuration'
    }

    $settings = [System.IO.File]::ReadAllText($settingsPath) | ConvertFrom-Json
    # Walk all sections without assuming where local credential settings are stored
    $pending = New-Object System.Collections.Generic.Queue[object]
    $pending.Enqueue($settings)
    while ($pending.Count -gt 0) {
        $node = $pending.Dequeue()
        if ($node -is [System.Management.Automation.PSCustomObject]) {
            foreach ($property in $node.PSObject.Properties) {
                if ($property.Name -in @('AzureArtifactsUsername', 'AzureArtifactsPat', 'AzureArtifactsApiKey')) {
                    $property.Value = ''
                } elseif ($null -ne $property.Value) {
                    $pending.Enqueue($property.Value)
                }
            }
        } elseif ($node -is [System.Array]) {
            foreach ($item in $node) {
                if ($null -ne $item) { $pending.Enqueue($item) }
            }
        }
    }
    $json = $settings | ConvertTo-Json -Depth 100
    [System.IO.File]::WriteAllText($settingsPath, (($json -replace '\r?\n', "`r`n") + "`r`n"), $utf8)

    # Disable external XML resolution and remove only credential sections, retaining sources
    $xml = New-Object System.Xml.XmlDocument
    $xml.XmlResolver = $null
    $xml.Load($nugetPath)
    foreach ($section in @($xml.SelectNodes('//*[local-name()="packageSourceCredentials" or local-name()="apikeys"]'))) {
        [void] $section.ParentNode.RemoveChild($section)
    }
    $writerSettings = New-Object System.Xml.XmlWriterSettings
    $writerSettings.Encoding = $utf8
    $writerSettings.Indent = $true
    $writerSettings.NewLineChars = "`r`n"
    $writer = [System.Xml.XmlWriter]::Create($nugetPath, $writerSettings)
    try { $xml.Save($writer) } finally { $writer.Dispose() }
}

try {
    # Sanitize before starting the child so a failed CLI publish cannot retain local credentials
    Protect-PublishedConfiguration
    if (-not $SanitizeOnly) {
        $settingsHash = (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash
        $arguments = @(
            'publish', $cliProject,
            '--configuration', $Configuration,
            '--runtime', $RuntimeIdentifier,
            '--self-contained', $SelfContained,
            '--output', $publishPath,
            '-p:PublishAsWinUICompanion=true',
            '-p:PublishTrimmed=false',
            '-p:PublishSingleFile=false',
            "-p:PublishReadyToRun=$PublishReadyToRun",
            "-p:UseAppHost=$UseAppHost"
        )
        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw 'CLI companion publish failed' }
        if ((Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash -ne $settingsHash) {
            throw 'CLI publish replaced the canonical WinUI configuration'
        }
        Protect-PublishedConfiguration
        foreach ($file in @('FODevManager.WinUI.exe', 'fodev.exe', 'MainWindow.xbf', 'FODevManager.WinUI.pri')) {
            if (-not (Test-Path -LiteralPath (Join-Path $publishPath $file) -PathType Leaf)) {
                throw 'Combined publish is missing a required executable or XAML resource'
            }
        }
    }
} catch {
    # Fail closed without echoing parser errors that might contain secret configuration values
    foreach ($file in @($settingsPath, $nugetPath, $developmentPath)) {
        Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue
    }
    throw 'Combined publish failed; published configuration was removed. Do not distribute this output'
}
