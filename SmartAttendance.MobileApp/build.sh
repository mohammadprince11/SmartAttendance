#!/usr/bin/env bash
set -e
cd "$(dirname "$0")"
ROOT="$(pwd)"
export JAVA_HOME="$ROOT/jdk/jdk-17.0.19+10"
export PATH="$JAVA_HOME/bin:$PATH"
SDK="$ROOT/sdk"
BT="$SDK/build-tools/34.0.0"
ANDROID_JAR="$SDK/platforms/android-34/android.jar"
AAPT2="$BT/aapt2.exe"
D8="$BT/d8.bat"
ZIPALIGN="$BT/zipalign.exe"
APKSIGNER="$BT/apksigner.bat"
JAR="$JAVA_HOME/bin/jar.exe"
JAVAC="$JAVA_HOME/bin/javac.exe"
KEYTOOL="$JAVA_HOME/bin/keytool.exe"

rm -rf build && mkdir -p build/gen build/classes build/dex
echo "== 1) aapt2 compile resources =="
"$AAPT2" compile --dir app/res -o build/res.zip

echo "== 2) aapt2 link =="
"$AAPT2" link -o build/base.apk -I "$ANDROID_JAR" \
  --manifest app/AndroidManifest.xml \
  --java build/gen \
  --min-sdk-version 24 --target-sdk-version 34 \
  --auto-add-overlay build/res.zip

echo "== 3) javac =="
"$JAVAC" -source 8 -target 8 -encoding UTF-8 \
  -bootclasspath "$ANDROID_JAR" -cp "$ANDROID_JAR" \
  -d build/classes \
  app/src/com/zynora/portal/MainActivity.java \
  build/gen/com/zynora/portal/R.java 2>&1 | grep -v "warning:" || true

echo "== 4) d8 -> classes.dex =="
"$D8" --release --min-api 24 --lib "$ANDROID_JAR" \
  --output build/dex \
  $(find build/classes -name '*.class')

echo "== 5) add classes.dex into apk =="
cp build/base.apk build/app-unsigned.apk
( cd build/dex && "$JAR" uf "$ROOT/build/app-unsigned.apk" classes.dex )

echo "== 6) zipalign =="
"$ZIPALIGN" -f -p 4 build/app-unsigned.apk build/app-aligned.apk

echo "== 7) keystore (debug) =="
if [ ! -f build/debug.keystore ]; then
  "$KEYTOOL" -genkeypair -keystore build/debug.keystore -alias key -keyalg RSA \
    -keysize 2048 -validity 10000 -storepass android -keypass android \
    -dname "CN=Zynora Portal, O=Zynora, C=IQ" >/dev/null 2>&1
fi

echo "== 8) apksigner sign =="
"$APKSIGNER" sign --ks build/debug.keystore --ks-pass pass:android --key-pass pass:android \
  --out build/ZynoraPortal.apk build/app-aligned.apk

echo "== DONE =="
ls -la build/ZynoraPortal.apk
"$APKSIGNER" verify --print-certs build/ZynoraPortal.apk 2>&1 | head -3
