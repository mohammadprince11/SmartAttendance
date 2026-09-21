# ZynoraHR Mobile

## Phase 1 — 0.2.0

The mobile app now starts as a native .NET MAUI employee app instead of a raw WebView.

Implemented:
- Native login via `POST /api/v1/auth/login`
- Bearer token in MAUI SecureStorage
- Session restore
- Employee profile via `GET /api/v1/me`
- Recent attendance via `GET /api/v1/me/attendance`
- Leave balances via `GET /api/v1/me/leave-balance`
- GPS check-in/check-out via `POST /api/v1/me/online-punch`
- Logout revokes the server token
- Connectivity handling

Package: `com.zynorahr.employee`

Build:
```powershell
dotnet restore ZynoraHR.Mobile/ZynoraHR.Mobile.csproj
dotnet build ZynoraHR.Mobile/ZynoraHR.Mobile.csproj -f net10.0-android -c Release
dotnet publish ZynoraHR.Mobile/ZynoraHR.Mobile.csproj -f net10.0-android -c Release -p:AndroidPackageFormats=apk
```

Next: requests/approvals, push notifications, native biometric confirmation, profile/documents/team.