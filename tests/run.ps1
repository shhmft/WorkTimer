param([string]$PreviewPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testExecutable = Join-Path $env:TEMP ('WorkTimerTests-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    & $compiler /nologo /target:exe /main:WorkTimer.WorkTimerTests "/out:$testExecutable" /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll (Join-Path $projectRoot 'WorkTimer.cs') (Join-Path $PSScriptRoot 'WorkTimerTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
    if ($PreviewPath) { & $testExecutable $PreviewPath } else { & $testExecutable }
    if ($LASTEXITCODE -ne 0) { throw 'WorkTimer tests failed' }
} finally {
    if (Test-Path -LiteralPath $testExecutable) { Remove-Item -LiteralPath $testExecutable }
}
