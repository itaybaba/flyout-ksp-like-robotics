$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path -Parent $PSScriptRoot
$taskPayload = Join-Path $taskRepo 'bin\Release\net6.0\RoboticsMod.dll'
if (-not (Test-Path -LiteralPath $taskPayload)) { throw 'Build RoboticsMod.csproj in Release mode first.' }
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) { $taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$taskOutputDir = Join-Path $taskRepo 'bin\installer'
New-Item -ItemType Directory -Path $taskOutputDir -Force | Out-Null
$taskOutput = Join-Path $taskOutputDir '(optional)-KSP-Like-Robotics-v1-Installer.exe'
# Embed the DLL and every part file (config, meshes, icons) under the logical name parts/<Folder>/<file>.
$taskParts = Join-Path $taskRepo 'parts'
$taskResources = @("/resource:$taskPayload,RoboticsMod.dll")
Get-ChildItem -LiteralPath $taskParts -Recurse -File | ForEach-Object {
    $taskName = 'parts/' + $_.FullName.Substring($taskParts.Length + 1).Replace('\', '/')
    $taskResources += "/resource:$($_.FullName),$taskName"
}
& $taskCompiler /nologo /target:winexe /platform:anycpu /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll "/win32manifest:$PSScriptRoot\installer.manifest" @taskResources "/out:$taskOutput" "$PSScriptRoot\OptionalInstaller.cs"
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
Write-Output $taskOutput
