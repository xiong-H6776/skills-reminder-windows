$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $projectRoot 'SkillsReminder.cs'
$output = Join-Path $projectRoot 'SkillsReminder.exe'
$manifest = Join-Path $projectRoot 'app.manifest'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "Windows .NET Framework compiler not found: $compiler"
}

& $compiler /nologo /target:winexe /optimize+ /platform:anycpu /win32manifest:$manifest /out:$output /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll $source
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

Get-Item -LiteralPath $output | Select-Object FullName, Length, LastWriteTime
