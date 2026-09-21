namespace ZynoraHR.Mobile;

public partial class MainPage : ContentPage
{
    private readonly MobileApi _api = new();
    private bool _initialized;
    private bool _busy;

    public MainPage()
    {
        InitializeComponent();

        RequestTypePicker.SelectedIndex = 0;
        FromDatePicker.Date = DateTime.Today;
        ToDatePicker.Date = DateTime.Today;
        MissingDatePicker.Date = DateTime.Today;
        MissingTimePicker.Time = DateTime.Now.TimeOfDay;
    }

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

        var username = UsernameEntry.Text?.Trim();
        var password = PasswordEntry.Text;

        if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password))
        {
            Error("أدخل اسم المستخدم وكلمة المرور.");
            return;
        }

        await Busy("جاري تسجيل الدخول...", async () =>
        {
            try
            {
                await _api.LoginAsync(username, password);
                PasswordEntry.Text = "";
                await LoadCoreAsync();
            }
            catch (MobileApiException ex)
            {
                ShowLogin();
                Error(ex.Message);
            }
            catch
            {
                ShowLogin();
                Error("تعذر الاتصال بالخادم.");
            }
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
                var permission =
                    await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();

                if (permission != PermissionStatus.Granted)
                    permission = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();

                if (permission != PermissionStatus.Granted)
                {
                    PunchLabel.Text = "يجب منح صلاحية الموقع لتسجيل البصمة.";
                    return;
                }

                var location =
                    await Geolocation.Default.GetLocationAsync(
                        new GeolocationRequest(
                            GeolocationAccuracy.High,
                            TimeSpan.FromSeconds(15)));

                if (location is null)
                {
                    PunchLabel.Text = "تعذر الحصول على موقع الجهاز.";
                    return;
                }

                var result =
                    await _api.PunchAsync(
                        type,
                        location.Latitude,
                        location.Longitude);

                PunchLabel.Text = result.Message;
                await LoadCoreAsync();
            }
            catch (MobileSessionExpiredException ex)
            {
                ShowLogin();
                Error(ex.Message);
            }
            catch (MobileApiException ex)
            {
                PunchLabel.Text = ex.Message;
            }
            catch (PermissionException)
            {
                PunchLabel.Text = "صلاحية الموقع مرفوضة.";
            }
            catch (FeatureNotEnabledException)
            {
                PunchLabel.Text = "GPS غير مفعّل.";
            }
            catch
            {
                PunchLabel.Text = "تعذر تسجيل البصمة حالياً.";
            }
        });
    }

    private async void OnHomeTab(object? sender, EventArgs e)
    {
        ShowHome();

        if (Online() && !_busy)
            await LoadAsync();
    }

    private async void OnRequestsTab(object? sender, EventArgs e)
    {
        ShowRequests();

        if (!Online())
        {
            RequestStatusLabel.Text = "اتصل بالإنترنت لعرض الطلبات.";
            return;
        }

        if (!_busy)
            await LoadRequestsAsync();
    }

    private async void OnRefreshRequests(object? sender, EventArgs e)
    {
        if (!_busy)
            await LoadRequestsAsync();
    }

    private async void OnSubmitRequest(object? sender, EventArgs e)
    {
        if (_busy) return;

        if (!Online())
        {
            RequestStatusLabel.Text = "لا يمكن إرسال الطلب بدون إنترنت.";
            return;
        }

        var requestType =
            RequestTypePicker.SelectedItem?.ToString();

        if (string.IsNullOrWhiteSpace(requestType))
        {
            RequestStatusLabel.Text = "اختر نوع الطلب.";
            return;
        }

        var from = FromDatePicker.Date ?? DateTime.Today;
        var to = ToDatePicker.Date ?? from;

        if (to < from)
        {
            RequestStatusLabel.Text = "تاريخ النهاية لا يمكن أن يسبق تاريخ البداية.";
            return;
        }

        await Busy("جاري إرسال الطلب...", async () =>
        {
            try
            {
                var result =
                    await _api.SubmitRequestAsync(
                        requestType,
                        from,
                        to,
                        RequestReasonEditor.Text);

                RequestStatusLabel.Text = result.Message;
                RequestReasonEditor.Text = "";
                await LoadRequestsCoreAsync();
            }
            catch (MobileSessionExpiredException ex)
            {
                ShowLogin();
                Error(ex.Message);
            }
            catch (MobileApiException ex)
            {
                RequestStatusLabel.Text = ex.Message;
            }
            catch
            {
                RequestStatusLabel.Text = "تعذر إرسال الطلب حالياً.";
            }
        });
    }

    private async void OnSubmitMissingPunch(object? sender, EventArgs e)
    {
        if (_busy) return;

        if (!Online())
        {
            MissingStatusLabel.Text = "لا يمكن إرسال طلب البصمة بدون إنترنت.";
            return;
        }

        await Busy("جاري إرسال طلب البصمة...", async () =>
        {
            try
            {
                var result =
                    await _api.SubmitMissingPunchAsync(
                        MissingDatePicker.Date ?? DateTime.Today,
                        MissingTimePicker.Time ?? DateTime.Now.TimeOfDay,
                        MissingReasonEditor.Text);

                MissingStatusLabel.Text = result.Message;
                MissingReasonEditor.Text = "";
                await LoadRequestsCoreAsync();
            }
            catch (MobileSessionExpiredException ex)
            {
                ShowLogin();
                Error(ex.Message);
            }
            catch (MobileApiException ex)
            {
                MissingStatusLabel.Text = ex.Message;
            }
            catch
            {
                MissingStatusLabel.Text = "تعذر إرسال طلب البصمة حالياً.";
            }
        });
    }

    private async void OnCancelRequest(object? sender, EventArgs e)
    {
        if (_busy ||
            sender is not Button button ||
            button.CommandParameter is null ||
            !int.TryParse(button.CommandParameter.ToString(), out var requestId))
            return;

        if (!Online())
        {
            RequestStatusLabel.Text = "لا يمكن إلغاء الطلب بدون إنترنت.";
            return;
        }

        await Busy("جاري إلغاء الطلب...", async () =>
        {
            try
            {
                var result = await _api.CancelRequestAsync(requestId);
                RequestStatusLabel.Text = result.Message;
                await LoadRequestsCoreAsync();
            }
            catch (MobileSessionExpiredException ex)
            {
                ShowLogin();
                Error(ex.Message);
            }
            catch (MobileApiException ex)
            {
                RequestStatusLabel.Text = ex.Message;
            }
            catch
            {
                RequestStatusLabel.Text = "تعذر إلغاء الطلب حالياً.";
            }
        });
    }

    private async void OnLogout(object? sender, EventArgs e)
    {
        if (_busy) return;

        await Busy("جاري تسجيل الخروج...", async () =>
        {
            try { await _api.LogoutAsync(); }
            catch { }

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
            AuthNav.IsVisible = true;
            HomePanel.IsVisible = true;
            RequestsPanel.IsVisible = false;
            LoginCard.IsVisible = false;
            PunchLabel.Text = "أنت غير متصل بالإنترنت.";
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

            var profile = await profileTask;
            var attendance = await attendanceTask;
            var leave = await leaveTask;

            NameLabel.Text =
                string.IsNullOrWhiteSpace(profile.FullName)
                    ? "موظف ZYNORA"
                    : profile.FullName;

            PositionLabel.Text =
                string.IsNullOrWhiteSpace(profile.Position)
                    ? "بدون منصب"
                    : profile.Position;

            OrgLabel.Text =
                string.Join(
                    " · ",
                    new[] { profile.Department, profile.Branch }
                        .Where(x => !string.IsNullOrWhiteSpace(x)));

            EmployeeNoLabel.Text = profile.EmployeeNo;

            if (attendance.Count > 0)
            {
                var row = attendance[0];
                AttendanceLabel.Text = row.Status;
                AttendanceDetailLabel.Text =
                    $"{row.Date} · {row.CheckIn ?? "—"} → {row.CheckOut ?? "—"}";
            }
            else
            {
                AttendanceLabel.Text = "لا توجد بيانات";
                AttendanceDetailLabel.Text = "آخر 7 أيام";
            }

            var annual =
                leave.FirstOrDefault(x =>
                    x.Type.Contains("Annual", StringComparison.OrdinalIgnoreCase) ||
                    x.Type.Contains("سن", StringComparison.OrdinalIgnoreCase))
                ?? leave.FirstOrDefault();

            if (annual is not null)
            {
                LeaveLabel.Text = annual.Remaining.ToString("0.##");
                LeaveDetailLabel.Text =
                    $"{annual.Type} · مستخدم {annual.Used:0.##} من {annual.Entitled:0.##} {annual.Unit}";
            }
            else
            {
                LeaveLabel.Text = "—";
                LeaveDetailLabel.Text = "لا يوجد رصيد";
            }

            ErrorLabel.IsVisible = false;
            LoginCard.IsVisible = false;
            AuthNav.IsVisible = true;

            if (!RequestsPanel.IsVisible)
                ShowHome();
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

    private async Task LoadRequestsAsync()
    {
        if (_busy) return;

        if (!Online())
        {
            RequestStatusLabel.Text = "اتصل بالإنترنت لعرض الطلبات.";
            return;
        }

        await Busy("جاري تحميل الطلبات...", LoadRequestsCoreAsync);
    }

    private async Task LoadRequestsCoreAsync()
    {
        try
        {
            var requestsTask = _api.RequestsAsync();
            var missingTask = _api.MissingPunchesAsync();

            await Task.WhenAll(requestsTask, missingTask);

            RequestsList.ItemsSource = await requestsTask;
            MissingPunchesList.ItemsSource = await missingTask;
        }
        catch (MobileSessionExpiredException ex)
        {
            ShowLogin();
            Error(ex.Message);
        }
        catch (MobileApiException ex)
        {
            RequestStatusLabel.Text = ex.Message;
        }
        catch
        {
            RequestStatusLabel.Text = "تعذر تحميل الطلبات.";
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
        AuthNav.IsVisible = false;
        HomePanel.IsVisible = false;
        RequestsPanel.IsVisible = false;
    }

    private void ShowHome()
    {
        LoginCard.IsVisible = false;
        AuthNav.IsVisible = true;
        HomePanel.IsVisible = true;
        RequestsPanel.IsVisible = false;

        HomeTabButton.BackgroundColor = Color.FromArgb("#18AEE8");
        HomeTabButton.TextColor = Colors.White;
        RequestsTabButton.BackgroundColor = Color.FromArgb("#17314B");
        RequestsTabButton.TextColor = Color.FromArgb("#DDEAF5");
    }

    private void ShowRequests()
    {
        LoginCard.IsVisible = false;
        AuthNav.IsVisible = true;
        HomePanel.IsVisible = false;
        RequestsPanel.IsVisible = true;

        RequestsTabButton.BackgroundColor = Color.FromArgb("#18AEE8");
        RequestsTabButton.TextColor = Colors.White;
        HomeTabButton.BackgroundColor = Color.FromArgb("#17314B");
        HomeTabButton.TextColor = Color.FromArgb("#DDEAF5");
    }

    private void Error(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }

    private void ConnectivityChanged(object? sender, ConnectivityChangedEventArgs e) =>
        MainThread.BeginInvokeOnMainThread(
            () => OfflineBanner.IsVisible = !Online());

    private static bool Online() =>
        Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
}