#!/usr/bin/env bash
#
# Reproducibly builds native/linux-x64/libe_sqlite3.so from the SQLite
# amalgamation, inside an old-glibc container so the result loads on every
# CS2 game-server host (Debian 10+ / Ubuntu 20.04+).
#
# Why this exists:
#   SQLitePCLRaw's bundled libe_sqlite3.so on the 2.1.x line ships a SQLite
#   older than 3.50.2 (CVE-2025-6965), and the patched 3.0.x line links against
#   GLIBC_2.33+, which the game container lacks. So we compile a patched SQLite
#   ourselves against ancient glibc (manylinux2014 => GLIBC_2.14) and override
#   the package's native at build time (see MatchZy.csproj OverridePatchedSqliteNative).
#
# Re-run this when bumping SQLITE_VER (e.g. on a future SQLite CVE), then commit
# the regenerated native/linux-x64/libe_sqlite3.so.
#
# Usage:    ./tools/build-sqlite-native.sh
# Requires: docker.
set -euo pipefail

SQLITE_VER=3500200          # SQLite 3.50.2 (CVE-2025-6965 fixed in >= 3.50.2)
SQLITE_YEAR=2025            # download-path year on sqlite.org
BUILD_IMAGE=quay.io/pypa/manylinux2014_x86_64   # CentOS 7 => GLIBC_2.17 toolchain

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# Usage: tools/build-sqlite-native.sh [all|linux|windows]   (default all)
TARGETS="${1:-all}"

if [ "$TARGETS" = all ] || [ "$TARGETS" = linux ]; then
OUT_DIR="$REPO_DIR/native/linux-x64"
mkdir -p "$OUT_DIR"

docker run --rm -e SQLITE_VER="$SQLITE_VER" -e SQLITE_YEAR="$SQLITE_YEAR" -v "$OUT_DIR":/out "$BUILD_IMAGE" bash -euo pipefail -c '
  curl -fsSL "https://sqlite.org/${SQLITE_YEAR}/sqlite-amalgamation-${SQLITE_VER}.zip" -o /tmp/s.zip
  PY=$(ls /opt/python/*/bin/python 2>/dev/null | head -1)
  "$PY" -c "import zipfile; zipfile.ZipFile(\"/tmp/s.zip\").extractall(\"/tmp\")"
  cd /tmp/sqlite-amalgamation-${SQLITE_VER}
  # Slim build: only what MatchZy uses (plain tables, WAL, pragma_table_info, foreign keys, math).
  # No FTS, R-tree, geopoly, session/preupdate, RBU, dbstat/bytecode/stmt vtabs: half the size
  # (about 740 K instead of 1.5 MB). SQLitePCLRaw binds exports lazily, so a left-out feature only
  # fails if it is called. -Os for size.
  gcc -shared -fPIC -Os -pthread \
    -DSQLITE_ENABLE_COLUMN_METADATA -DSQLITE_ENABLE_MATH_FUNCTIONS \
    -DSQLITE_ENABLE_UNLOCK_NOTIFY \
    -DSQLITE_THREADSAFE=1 -DSQLITE_USE_URI=1 -DSQLITE_ENABLE_DESERIALIZE \
    -DSQLITE_DEFAULT_FOREIGN_KEYS=1 \
    -o /out/libe_sqlite3.so sqlite3.c -lm -ldl -lpthread
  strip /out/libe_sqlite3.so
  echo "built SQLite $(grep -oE "SQLITE_VERSION +\"[0-9.]+\"" sqlite3.h | head -1)"
  echo "max GLIBC $(objdump -T /out/libe_sqlite3.so | grep -oE "GLIBC_[0-9]+\.[0-9]+" | sort -V | tail -1)"
'
echo "Wrote $OUT_DIR/libe_sqlite3.so"
fi

# Windows (e_sqlite3.dll): the same SQLite and flags, cross-compiled with mingw-w64. The package's
# win-x64 native is the same vulnerable SQLite < 3.50.2 as its Linux one. -static-libgcc so the dll
# needs only Windows system dlls (SQLite uses Win32 threads, no winpthread).
if [ "$TARGETS" = all ] || [ "$TARGETS" = windows ]; then
WIN_IMAGE=debian:bookworm-slim
WIN_OUT_DIR="$REPO_DIR/native/win-x64"
mkdir -p "$WIN_OUT_DIR"
docker run --rm -e SQLITE_VER="$SQLITE_VER" -e SQLITE_YEAR="$SQLITE_YEAR" -v "$WIN_OUT_DIR":/out "$WIN_IMAGE" bash -euo pipefail -c '
  apt-get update -qq && apt-get install -y -qq --no-install-recommends gcc-mingw-w64-x86-64 binutils-mingw-w64-x86-64 curl ca-certificates unzip >/dev/null
  curl -fsSL "https://sqlite.org/${SQLITE_YEAR}/sqlite-amalgamation-${SQLITE_VER}.zip" -o /tmp/s.zip
  unzip -q /tmp/s.zip -d /tmp
  cd /tmp/sqlite-amalgamation-${SQLITE_VER}
  x86_64-w64-mingw32-gcc -shared -Os -static-libgcc \
    -DSQLITE_ENABLE_COLUMN_METADATA -DSQLITE_ENABLE_MATH_FUNCTIONS \
    -DSQLITE_ENABLE_UNLOCK_NOTIFY \
    -DSQLITE_THREADSAFE=1 -DSQLITE_USE_URI=1 -DSQLITE_ENABLE_DESERIALIZE \
    -DSQLITE_DEFAULT_FOREIGN_KEYS=1 \
    -o /out/e_sqlite3.dll sqlite3.c
  x86_64-w64-mingw32-strip /out/e_sqlite3.dll
  echo "built SQLite $(grep -oE "SQLITE_VERSION +\"[0-9.]+\"" sqlite3.h | head -1) for win-x64"
  echo "imports: $(x86_64-w64-mingw32-objdump -p /out/e_sqlite3.dll | grep -oE "DLL Name: .*" | sed "s/DLL Name: //" | tr "\n" " ")"
'
echo "Wrote $WIN_OUT_DIR/e_sqlite3.dll"
fi
