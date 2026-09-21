namespace ZynoraHR.Mobile;

public partial class MainPage : ContentPage
{
    private readonly MobileApi _api = new();
    private bool _initialized;
    private bool _busy;

    public MainPage() => InitializeComponent();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Connectivity.Current.ConnectivityChanged += ConnectivityChanged;
        OfflineBanner.IsVisible = !Online();

        if (!_initialized)
        {
            _initialized = true;
            _ = InitializeAsync();
        }
    }

    protected override void OnDisappearing()
    {
        Connectivity.Current.ConnectivityChanged -= ConnectivityChanged;
        base.OnDisappearing();
    }

    private async Task InitializeAsync()
    {
        if (await _api.HasSessionAsync()) await LoadAsync();
        else ShowLogin();
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        if (_busy) return;

        var u = UsernameEntry.Text?.Trim();
        var p = PasswordEntry.Text;
        if (string.IsNullOrWhiteSpace(u) || string.IsNullOrWhiteSpace(p))
        {
            Error("أدخل اسم المستخدم وكلمة المرور.");
            return;
        }

        await Busy("جاري تسجيل الدخول...", async () =>
        {
            try
            {
                await _api.LoginAsync(u, p);
                PasswordEntry.Text = "";
                await LoadCoreAsync();
            }
            catch (MobileApiException ex) { ShowLogin(); Error(ex.Message); }
            catch { ShowLogin(); Error("تعذر الاتصال بالخادم."); }
        });
    }

    private async void OnRefresh(object? sender, EventArgs e)
    {
        if (!_busy) await LoadAsync();
    }

    private async void OnCheckIn(object? sender, EventArgs e) => await Punch("In");
    private async void OnCheckOut(object? sender, EventArgs e) => await Punch("Out");

    private async Task Punch(string type)
    {
        if (_busy) return;
        if (!Online())
        {
            PunchLabel.Text = "لا يمكن تسجيل البصمة بدون إنترنت.";
            return;
        }

        await Busy("جاري التحقق من الموقع...", async () =>
        {
            try
            {
                var permission = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                if (permission != PermissionStatus.Granted)
                    permission = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();

                if (permission != PermissionStatus.Granted)
                {
                    PunchLabel.Text = "يجب منح صلاحية الموقع لتسجيل البصمة.";
                    return;
                }

                var location = await Geolocation.Default.GetLocationAsync(
                    new GeolocationRequest(GeolocationAccuracy.High, TimeSpan.FromSeconds(15)));

                if (location is null)
                {
                    PunchLabel.Text = "تعذر الحصول على موقع الجهاز.";
                    return;
                }

                var result = await _api.PunchAsync(
                    type, location.Latitude, location.Longitude);

                PunchLabel.Text = result.Message;
                await LoadCoreAsync();
            }
            catch (MobileSessionExpiredException ex)
            {
                ShowLogin();
                Error(ex.Message);
            }
            catch (MobileApiException ex) { PunchLabel.Text = ex.Message; }
            catch (PermissionException) { PunchLabel.Text = "صلاحية الموقع مرفوضة."; }
            catch (FeatureNotEnabledException) { PunchLabel.Text = "GPS غير مفعّل."; }
            catch { PunchLabel.Text = "تعذر تسجيل البصمة حالياً."; }
        });
    }

    private async void OnLogout(object? sender, EventArgs e)
    {
        if (_busy) return;

        await Busy("جاري تسجيل الخروج...", async () =>
        {
            try { await _api.LogoutAsync(); } catch { }
            UsernameEntry.Text = "";
            PasswordEntry.Text = "";
            ShowLogin();
        });
    }

    private async Task LoadAsync()
    {
        if (_busy) return;
        if (!Online())
        {
            PunchLabel.Text = "أنت غير متصل بالإنترنت.";
            HomePanel.IsVisible = true;
            LoginCard.IsVisible = false;
            return;
        }

        await Busy("جاري تحميل بياناتك...", LoadCoreAsync);
    }

    private async Task LoadCoreAsync()
    {
        try
        {
            var profileTask = _api.ProfileAsync();
            var attendanceTask = _api.AttendanceAsync();
            var leaveTask = _api.LeaveBalancesAsync();

            await Task.WhenAll(profileTask, attendanceTask, leaveTask);

            var p = await profileTask;
            var a = await attendanceTask;
            var l = await leaveTask;

            NameLabel.Text = string.IsNullOrWhiteSpace(p.FullName) ? "موظف ZYNORA" : p.FullName;
            PositionLabel.Text = string.IsNullOrWhiteSpace(p.Position) ? "بدون منصب" : p.Position;
            OrgLabel.Text = string.Join(" · ", new[] { p.Department, p.Branch }.Where(x => !string.IsNullOrWhiteSpace(x)));
            EmployeeNoLabel.Text = p.EmployeeNo;

            if (a.Count > 0)
            {
                var x = a[0];
                AttendanceLabel.Text = x.Status;
                AttendanceDetailLabel.Text = $"{x.Date} · {x.CheckIn ?? "—"} → {x.CheckOut ?? "—"}";
            }
            else
            {
                AttendanceLabel.Text = "لا توجد بيانات";
                AttendanceDetailLabel.Text = "آخر 7 أيام";
            }

            var annual = l.FirstOrDefault(x =>
                x.Type.Contains("Annual", StringComparison.OrdinalIgnoreCase) ||
                x.Type.Contains("سن", StringComparison.OrdinalIgnoreCase)) ?? l.FirstOrDefault();

            if (annual is not null)
            {
                LeaveLabel.Text = annual.Remaining.ToString("0.##");
                LeaveDetailLabel.Text = $"{annual.Type} · مستخدم {annual.Used:0.##} من {annual.Entitled:0.##} {annual.Unit}";
            }
            else
            {
                LeaveLabel.Text = "—";
                LeaveDetailLabel.Text = "لا يوجد رصيد";
            }

            ErrorLabel.IsVisible = false;
            LoginCard.IsVisible = false;
            HomePanel.IsVisible = true;
        }
        catch (MobileSessionExpiredException ex)
        {
            ShowLogin();
            Error(ex.Message);
        }
        catch (MobileApiException ex)
        {
            Error(ex.Message);
        }
        catch
        {
            Error("تعذر تحديث بيانات الموظف.");
        }
    }

    private async Task Busy(string text, Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        LoadingLabel.Text = text;
        LoadingLayer.IsVisible = true;

        try { await action(); }
        finally
        {
            LoadingLayer.IsVisible = false;
            _busy = false;
        }
    }

    private void ShowLogin()
    {
        LoginCard.IsVisible = true;
        HomePanel.IsVisible = false;
    }

    private void Error(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }

    private void ConnectivityChanged(object? sender, ConnectivityChangedEventArgs e) =>
        MainThread.BeginInvokeOnMainThread(() => OfflineBanner.IsVisible = !Online());

    private static bool Online() =>
        Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
}