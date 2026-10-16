#!/bin/zsh
set -euo pipefail

SOURCE="${0:A:h}"
WORK="${MUSIC_BUILD_ROOT:?Set MUSIC_BUILD_ROOT to an external temporary build directory}"
RUN="$WORK/build-$(date +%Y%m%d-%H%M%S)"
SIGNING_IDENTITY="${MUSIC_SIGNING_IDENTITY:?A Developer ID signing identity is required}"
NOTARY_PROFILE="${MUSIC_NOTARY_PROFILE:?A notarytool keychain profile is required}"
NOTARY_KEYCHAIN="${MUSIC_NOTARY_KEYCHAIN:-$HOME/Library/Keychains/login.keychain-db}"
ARCH="${MUSIC_ARCH:-arm64}"
case "$ARCH" in
arm64) TARGET_FLAGS=(); PACKAGE_ARCH="" ;;
x86_64) TARGET_FLAGS=(--triple x86_64-apple-macosx14.0); PACKAGE_ARCH="-Intel" ;;
  *) print -u2 "Unsupported Mac architecture: $ARCH"; exit 2 ;;
esac
mkdir -p "$RUN/tmp" "$RUN/stage"
export TMPDIR="$RUN/tmp/"
export CLANG_MODULE_CACHE_PATH="$RUN/module-cache"
SWIFT_CACHE=(--cache-path "$WORK/swift-cache")
cd "$SOURCE"

security find-identity -v -p codesigning | grep -Fq '"'"$SIGNING_IDENTITY"'"'
xcrun notarytool history --keychain-profile "$NOTARY_PROFILE" --keychain "$NOTARY_KEYCHAIN" >/dev/null

if [[ "$ARCH" == "x86_64" ]]; then
  if [[ "${MUSIC_SKIP_INTEL_TESTS:-0}" == "1" ]]; then
    print -u2 'WARNING: Intel runtime tests explicitly skipped. This build is not runtime-verified.'
    swift build --build-tests --build-path "$RUN/swift-build" "${SWIFT_CACHE[@]}" "${TARGET_FLAGS[@]}"
  else
    arch -x86_64 /usr/bin/swift test --disable-swift-testing --build-path "$RUN/swift-build" "${SWIFT_CACHE[@]}" "${TARGET_FLAGS[@]}"
  fi
else
  swift test --build-path "$RUN/swift-build" "${SWIFT_CACHE[@]}"
fi
swift build -c release --build-path "$RUN/swift-build" "${SWIFT_CACHE[@]}" "${TARGET_FLAGS[@]}"
BIN_DIR=$(swift build -c release --show-bin-path --build-path "$RUN/swift-build" "${SWIFT_CACHE[@]}" "${TARGET_FLAGS[@]}")

APP="$RUN/stage/ElevenLabs Music Generator.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN_DIR/ElevenLabsMusicGeneratorMac" "$APP/Contents/MacOS/"
cp "$SOURCE/Info.plist" "$APP/Contents/Info.plist"
cp "$SOURCE/Manual.html" "$APP/Contents/Resources/Manual.html"
cp "$SOURCE/LICENSE.txt" "$APP/Contents/Resources/LICENSE.txt"
for bundle in "$BIN_DIR"/*.bundle(N); do
  cp -R "$bundle" "$APP/Contents/Resources/"
done
codesign --force --options runtime --timestamp --sign "$SIGNING_IDENTITY" "$APP"
codesign --verify --deep --strict "$APP"

VERSION=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$SOURCE/Info.plist")
ZIP="$RUN/ElevenLabs-Music-Generator-Mac${PACKAGE_ARCH}-$VERSION.zip"
ditto -c -k --sequesterRsrc --keepParent "$APP" "$ZIP"
xcrun notarytool submit "$ZIP" --keychain-profile "$NOTARY_PROFILE" --keychain "$NOTARY_KEYCHAIN" --wait
xcrun stapler staple "$APP"
xcrun stapler validate "$APP"
spctl --assess --type execute --verbose "$APP"
ditto -c -k --sequesterRsrc --keepParent "$APP" "$ZIP"
printf 'APP=%s\nZIP=%s\n' "$APP" "$ZIP"
