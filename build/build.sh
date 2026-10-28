#!/usr/bin/env bash
# Builds VCM entirely in Docker on Linux:
#   1. the native virtual camera DLL (MSVC under wine, image built from build/Dockerfile.msvc)
#   2. unit tests + the WPF app (.NET 10 SDK)
#   3. the installer (Inno Setup under wine)
# Output: out/native, out/app, out/VCM-Setup-<version>.exe
#
# Building the MSVC image downloads Visual Studio Build Tools and the Windows SDK and accepts their license
# (https://go.microsoft.com/fwlink/?LinkId=2086102).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
USER_ID="$(id -u):$(id -g)"

DOTNET_IMAGE="mcr.microsoft.com/dotnet/sdk:10.0@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b"
INNO_IMAGE="amake/innosetup@sha256:e003376ba818547275fe10c95e2a29be0f2d12d45e9eb8f205b6672dc5685bb1"
MSVC_IMAGE="vcm-msvc:1"

step() { printf '\n==> %s\n' "$*"; }

mkdir -p "$ROOT/out/native" "$ROOT/out/app" "$ROOT/.cache/nuget"

if ! docker image inspect "$MSVC_IMAGE" >/dev/null 2>&1; then
    step "Building the MSVC image (one-time, ~15-30 minutes)"
    docker build -f "$ROOT/build/Dockerfile.msvc" -t "$MSVC_IMAGE" "$ROOT/build"
fi

step "Native virtual camera DLL"
docker run --rm --user "$USER_ID" -v "$ROOT":/src -w /src "$MSVC_IMAGE" bash -c '
    set -e
    wineserver -p >/dev/null 2>&1 || true
    wine wineboot >/dev/null 2>&1 || true
    CC=cl CXX=cl cmake -S src/Vcm.VirtualCamera -B build/cmake-x64 -G Ninja \
        -DCMAKE_BUILD_TYPE=Release -DCMAKE_SYSTEM_NAME=Windows -DCMAKE_RC_COMPILER=rc >/dev/null
    cmake --build build/cmake-x64
    cp build/cmake-x64/VcmVirtualCamera.dll out/native/'

dotnet() {
    docker run --rm --user "$USER_ID" \
        -e HOME=/tmp/home -e DOTNET_CLI_HOME=/tmp/home -e NUGET_PACKAGES=/src/.cache/nuget \
        -e DOTNET_NOLOGO=1 -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
        -v "$ROOT":/src -w /src "$DOTNET_IMAGE" dotnet "$@"
}

step "Unit tests"
dotnet test --project tests/Vcm.Core.Tests

step "WPF app"
rm -rf "$ROOT/out/app"
dotnet publish src/Vcm.App -c Release -r win-x64 --self-contained false -o out/app -p:DebugType=none
test -f "$ROOT/out/app/VcmVirtualCamera.dll" || { echo "VcmVirtualCamera.dll missing from out/app" >&2; exit 1; }

step "Installer"
docker run --rm --user "$USER_ID" -v "$ROOT":/work "$INNO_IMAGE" installer/vcm.iss

step "Done"
ls -la "$ROOT/out"
