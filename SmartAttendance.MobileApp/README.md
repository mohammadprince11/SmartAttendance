# ZYNORA Android WebView app

WebView wrapper opening the portal as a standalone app.
Loads https://192.168.1.53:5443/EmployeePortal (edit IP in app/src/com/zynora/portal/MainActivity.java).
Build: install JDK17 + Android SDK (build-tools;34.0.0, platforms;android-34), fix paths in build.sh, run: bash build.sh -> build/ZynoraPortal.apk. Copy to SmartAttendance.Web/wwwroot/downloads/ZynoraPortal.apk (served at /app.apk).
