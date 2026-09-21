# Unity 内蔵の Android NDK Clang を使用して libOthelloCvPlugin.so (ARM64) をビルドするスクリプト

$ndkClang = "C:\Program Files\Unity\Hub\Editor\6000.3.19f1\Editor\Data\PlaybackEngines\AndroidPlayer\NDK\toolchains\llvm\prebuilt\windows-x86_64\bin\clang++.exe"
$projectRoot = Split-Path -Parent $PSScriptRoot
$src = Join-Path $PSScriptRoot "OthelloCvPlugin.cpp"
$inc = Join-Path $projectRoot "NativeBuild\OpenCV-android-sdk\sdk\native\jni\include"
$libDir1 = Join-Path $projectRoot "NativeBuild\OpenCV-android-sdk\sdk\native\staticlibs\arm64-v8a"
$libDir2 = Join-Path $projectRoot "NativeBuild\OpenCV-android-sdk\sdk\native\3rdparty\libs\arm64-v8a"

$destDir = Join-Path $projectRoot "Assets\Plugins\Android\libs\arm64-v8a"
if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
$outSo = Join-Path $destDir "libOthelloCvPlugin.so"

$args = @(
    "--target=aarch64-linux-android29",
    "-shared",
    "-fPIC",
    "-O3",
    "-std=c++17",
    "-static-libstdc++",
    "-Wl,-soname,libOthelloCvPlugin.so",
    "-Wl,-z,max-page-size=16384",
    "-I$inc",
    "-L$libDir1",
    "-L$libDir2",
    "$src",
    "-lopencv_calib3d",
    "-lopencv_features2d",
    "-lopencv_flann",
    "-lopencv_imgproc",
    "-lopencv_core",
    "-ltbb",
    "-littnotify",
    "-ltegra_hal",
    "-lcpufeatures",
    "-llog",
    "-lz",
    "-o",
    "$outSo"
)

Write-Host "Compiling libOthelloCvPlugin.so for ARM64..."
& $ndkClang $args
if ($LASTEXITCODE -eq 0) {
    Write-Host "SUCCESS: Generated $outSo" -ForegroundColor Green
} else {
    Write-Host "FAILED: Compiler exited with code $LASTEXITCODE" -ForegroundColor Red
}
