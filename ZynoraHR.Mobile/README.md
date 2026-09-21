# ZynoraHR Mobile

## Phase 2 stabilization — 0.3.1

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
- Dynamic request types loaded from the active server catalog
- RequestTypeId is authoritative; generic hard-coded leave names are no longer submitted
- Timed requests send start/end time for permissions and overtime
- Optional/required request attachments use the protected server file store
- Company request policy and profile eligibility are enforced by the API
- Legacy 0.3.0 request submission is blocked to prevent incomplete production records
- Dedicated Missing Punch submission respects self-service permission
- Offline home snapshot is encrypted in SecureStorage and cleared on logout
- GPS punch rejects very low-accuracy locations before sending
- Android manifest contains only permissions used by this phase

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
5. Expanded offline request/history cache.