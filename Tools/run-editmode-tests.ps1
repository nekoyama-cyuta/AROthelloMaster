<#
.SYNOPSIS
  実験システムの EditMode テストを Unity バッチモードで実行する。

.DESCRIPTION
  Unity エディタでこのプロジェクトを開いたままでも実行できるよう、既定ではプロジェクトを
  一時フォルダへコピー (robocopy /MIR) してからコピー側でテストを実行する。
  2 回目以降は差分だけコピーされ、Library も再利用されるので速い。
  XREAL パッケージは Android ターゲットでないとコンパイルできないため -buildTarget Android で起動する。

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Tools/run-editmode-tests.ps1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Tools/run-editmode-tests.ps1 -InPlace   # エディタを閉じている場合
#>
param(
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.3.19f1\Editor\Unity.exe",
    [string]$AssemblyNames = "AROthello.Experiment.Tests.EditMode",
    [string]$WorkDir = (Join-Path $env:TEMP "AROthelloMaster_TestRun"),
    [switch]$InPlace
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null

if ($InPlace) {
    $testProject = $projectRoot
} else {
    $testProject = Join-Path $WorkDir "project"
    foreach ($d in 'Assets', 'Packages', 'ProjectSettings', 'com.xreal.xr') {
        robocopy (Join-Path $projectRoot $d) (Join-Path $testProject $d) /MIR /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "robocopy failed for $d (exit $LASTEXITCODE)" }
    }
    $cacheDst = Join-Path $testProject "Library\PackageCache"
    if (-not (Test-Path $cacheDst)) {
        robocopy (Join-Path $projectRoot "Library\PackageCache") $cacheDst /MIR /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    }
}

$results = Join-Path $WorkDir "results.xml"
$log = Join-Path $WorkDir "unity.log"
if (Test-Path $results) { Remove-Item $results -Force }

Write-Host "Running EditMode tests ($AssemblyNames) in $testProject ..."
$unityArgs = @(
    "-batchmode", "-nographics", "-buildTarget", "Android",
    "-projectPath", $testProject,
    "-runTests", "-testPlatform", "EditMode",
    "-assemblyNames", $AssemblyNames,
    "-testResults", $results,
    "-logFile", $log
)
$proc = Start-Process -FilePath $UnityPath -ArgumentList $unityArgs -Wait -PassThru -NoNewWindow

if (-not (Test-Path $results)) {
    Write-Host "No test results were produced (Unity exit $($proc.ExitCode)). Compile errors:"
    Select-String -Path $log -Pattern "error CS" | Select-Object -First 20 | ForEach-Object { $_.Line }
    exit 1
}

[xml]$xml = Get-Content $results
$run = $xml.'test-run'
Write-Host ("Result: {0}  total={1} passed={2} failed={3} skipped={4}" -f $run.result, $run.total, $run.passed, $run.failed, $run.skipped)
$xml.SelectNodes("//test-case[@result='Failed']") | ForEach-Object {
    Write-Host ("FAILED: {0}`n  {1}" -f $_.fullname, $_.failure.message.'#cdata-section')
}
if ($run.result -ne "Passed") { exit 1 }
exit 0
