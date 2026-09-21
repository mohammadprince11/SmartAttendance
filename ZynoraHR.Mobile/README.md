# ZynoraHR Mobile

## Phase 2 — 0.3.0

Native employee mobile app built with .NET MAUI.

### Phase 1
- Native login via `POST /api/v1/auth/login`
- Bearer token in MAUI SecureStorage
- Session restore
- Employee profile via `GET /api/v1/me`
- Recent attendance via `GET /api/v1/me/attendance`
- Leave balances via `GET /api/v1/me/leave-balance`
- GPS check-in/check-out via `POST /api/v1/me/online-punch`
- Server token revocation on logout
- Connectivity handling

### Phase 2
- Native Requests tab
- List employee self-service requests
- Submit Leave / Personal Exit / Work Exit / Overtime
- Cancel cancelable self-service requests
- Dedicated Missing Punch submission
- List Missing Punch requests
- Existing `/api/v1/me/*` endpoints reused; no duplicate backend logic

Package: `com.zynorahr.employee`

Build:
```powershell
dotnet restore ZynoraHR.Mobile/ZynoraHR.Mobile.csproj
dotnet build ZynoraHR.Mobile/ZynoraHR.Mobile.csproj -f net10.0-android -c Release -p:AndroidPackageFormats=apk
dotnet publish ZynoraHR.Mobile/ZynoraHR.Mobile.csproj -f net10.0-android -c Release -p:AndroidPackageFormats=apk
```

Next:
1. Native manager approvals.
2. Device registration + FCM/APNs.
3. Native biometric confirmation for attendance.
4. Profile/documents/team pages.
5. Offline-safe read cache.