param([switch]$Live)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testDirectory = Join-Path $env:TEMP ('WorkTimerUpdaterCompile-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDirectory | Out-Null
$testExecutable = Join-Path $testDirectory 'UpdaterTests.exe'
$stubExecutable = Join-Path $testDirectory 'Stub.exe'
try {
    & $compiler /nologo /codepage:65001 /target:winexe "/out:$stubExecutable" (Join-Path $PSScriptRoot 'UpdateStub.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Stub compilation failed' }
    & $compiler /nologo /codepage:65001 /target:exe /main:WorkTimer.UpdaterTests "/out:$testExecutable" /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll (Join-Path $projectRoot 'WorkTimer.cs') (Join-Path $projectRoot 'Updater.cs') (Join-Path $PSScriptRoot 'UpdaterTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Updater test compilation failed' }
    if ($Live) { & $testExecutable $stubExecutable --live } else { & $testExecutable $stubExecutable }
    if ($LASTEXITCODE -ne 0) { throw 'Updater tests failed' }
} finally {
    $resolvedTestDirectory = [IO.Path]::GetFullPath($testDirectory)
    if ($resolvedTestDirectory.StartsWith([IO.Path]::GetFullPath($env:TEMP) + '\', [StringComparison]::OrdinalIgnoreCase) -and (Split-Path -Leaf $resolvedTestDirectory).StartsWith('WorkTimerUpdaterCompile-')) {
        Remove-Item -LiteralPath $resolvedTestDirectory -Recurse -Force
    }
}
