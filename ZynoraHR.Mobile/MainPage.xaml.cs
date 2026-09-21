using System.Text.Json;

namespace ZynoraHR.Mobile;

public partial class MainPage : ContentPage
{
    private const string HomeCacheKey = "zynora.mobile.home_cache.v1";
    private static readonly JsonSerializerOptions CacheJson =
        new(JsonSerializerDefaults.Web);

    private readonly MobileApi _api = new();
    private bool _initialized;
    private bool _busy;
    private FileResult? _requestAttachment;

    public MainPage()
    {
        InitializeComponent();

        RequestTypePicker.ItemDisplayBinding =
            new Binding(nameof(MobileRequestType.DisplayName));
        FromDatePicker.Date = DateTime.Today;
        ToDatePicker.Date = DateTime.Today;
        RequestStartTimePicker.Time = DateTime.Now.TimeOfDay;
        RequestEndTimePicker.Time = DateTime.Now.AddHours(1).TimeOfDay;
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

                if (location.Accuracy is double accuracy && accuracy > 150)
                {
                    PunchLabel.Text =
                        $"دقة الموقع الحالية ±{accuracy:0} م. فعّل الموقع الدقيق ثم أعد المحاولة.";
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

    private void OnRequestTypeChanged(object? sender, EventArgs e)
    {
        UpdateRequestTypeFields();
    }

    private async void OnPickRequestAttachment(object? sender, EventArgs e)
    {
        if (_busy) return;

        try
        {
            var picked = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "اختر مرفق الطلب"
            });

            if (picked is null) return;

            _requestAttachment = picked;
            RequestAttachmentName.Text = picked.FileName;
            RequestStatusLabel.Text = "";
        }
        catch
        {
            RequestStatusLabel.Text = "تعذر فتح منتقي الملفات.";
        }
    }

    private async void OnSubmitRequest(object? sender, EventArgs e)
    {
        if (_busy) return;

        if (!Online())
        {
            RequestStatusLabel.Text = "لا يمكن إرسال الطلب بدون إنترنت.";
            return;
        }

        if (RequestTypePicker.SelectedItem is not MobileRequestType requestType)
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

        TimeSpan? startTime = null;
        TimeSpan? endTime = null;
        if (requestType.NeedsTime)
        {
            startTime = RequestStartTimePicker.Time;
            endTime = RequestEndTimePicker.Time;
            if (startTime is null || endTime is null)
            {
                RequestStatusLabel.Text = "حدد وقت البداية والنهاية.";
                return;
            }
        }

        if (requestType.AttachmentRequired && _requestAttachment is null)
        {
            RequestStatusLabel.Text = "هذا النوع يتطلب مرفقاً قبل الإرسال.";
            return;
        }

        if (requestType.ReasonRequired &&
            string.IsNullOrWhiteSpace(RequestReasonEditor.Text))
        {
            RequestStatusLabel.Text = "سبب الطلب إلزامي حسب سياسة الشركة.";
            return;
        }

        await Busy("جاري إرسال الطلب...", async () =>
        {
            try
            {
                var result = await _api.SubmitRequestAsync(
                    requestType,
                    from,
                    to,
                    startTime,
                    endTime,
                    RequestReasonEditor.Text,
                    _requestAttachment);

                RequestStatusLabel.Text = result.Message;
                RequestReasonEditor.Text = "";
                ResetRequestAttachment();
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

            SecureStorage.Default.Remove(HomeCacheKey);
            ResetRequestAttachment();
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
            var loadedCache = await LoadHomeCacheAsync();
            AuthNav.IsVisible = true;
            HomePanel.IsVisible = true;
            RequestsPanel.IsVisible = false;
            LoginCard.IsVisible = false;
            PunchLabel.Text = loadedCache
                ? "وضع عدم الاتصال — يتم عرض آخر بيانات محفوظة على الجهاز."
                : "أنت غير متصل بالإنترنت ولا توجد بيانات محفوظة بعد.";
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

            RenderHome(profile, attendance, leave);
            await SaveHomeCacheAsync(profile, attendance, leave);

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
            if (LoginCard.IsVisible)
            {
                Error(ex.Message);
            }
            else
            {
                await LoadHomeCacheAsync();
                PunchLabel.Text = ex.Message;
            }
        }
        catch
        {
            if (LoginCard.IsVisible)
            {
                Error("تعذر تحديث بيانات الموظف.");
            }
            else
            {
                await LoadHomeCacheAsync();
                PunchLabel.Text = "تعذر تحديث البيانات؛ يتم عرض آخر نسخة محفوظة.";
            }
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
            var catalogTask = _api.RequestTypesAsync();

            await Task.WhenAll(requestsTask, missingTask, catalogTask);

            RequestsList.ItemsSource = await requestsTask;
            MissingPunchesList.ItemsSource = await missingTask;

            var catalog = await catalogTask;
            var currentId =
                (RequestTypePicker.SelectedItem as MobileRequestType)?.Id;

            MissingPunchRequestCard.IsVisible =
                catalog.Eligible && catalog.CanSubmitMissingPunch;

            RequestTypePicker.ItemsSource = catalog.Items;
            RequestTypePicker.IsEnabled =
                catalog.Eligible && catalog.Items.Count > 0;

            if (!catalog.Eligible)
            {
                RequestStatusLabel.Text =
                    catalog.Message ?? "لا يمكنك تقديم طلب حالياً.";
                RequestTypePicker.SelectedIndex = -1;
            }
            else if (catalog.Items.Count == 0)
            {
                RequestStatusLabel.Text =
                    "لا توجد أنواع طلبات متاحة لهذا الحساب.";
                RequestTypePicker.SelectedIndex = -1;
            }
            else
            {
                var selectedIndex = currentId is int id
                    ? catalog.Items.FindIndex(x => x.Id == id)
                    : -1;

                RequestTypePicker.SelectedIndex =
                    selectedIndex >= 0 ? selectedIndex : 0;
            }

            UpdateRequestTypeFields();
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

    private void RenderHome(
        EmployeeProfile profile,
        List<AttendanceDay> attendance,
        List<LeaveBalance> leave)
    {
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
    }

    private async Task SaveHomeCacheAsync(
        EmployeeProfile profile,
        List<AttendanceDay> attendance,
        List<LeaveBalance> leave)
    {
        try
        {
            var json = JsonSerializer.Serialize(
                new HomeCacheSnapshot
                {
                    Profile = profile,
                    Attendance = attendance,
                    Leave = leave,
                    CachedAtUtc = DateTime.UtcNow
                },
                CacheJson);

            await SecureStorage.Default.SetAsync(HomeCacheKey, json);
        }
        catch
        {
        }
    }

    private async Task<bool> LoadHomeCacheAsync()
    {
        try
        {
            var json = await SecureStorage.Default.GetAsync(HomeCacheKey);
            if (string.IsNullOrWhiteSpace(json))
                return false;

            var cache = JsonSerializer.Deserialize<HomeCacheSnapshot>(json, CacheJson);
            if (cache?.Profile is null)
                return false;

            RenderHome(
                cache.Profile,
                cache.Attendance ?? new(),
                cache.Leave ?? new());

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateRequestTypeFields()
    {
        if (RequestTypePicker.SelectedItem is not MobileRequestType type)
        {
            RequestTimeFields.IsVisible = false;
            RequestAttachmentFields.IsVisible = false;
            return;
        }

        RequestTimeFields.IsVisible = type.NeedsTime;
        RequestAttachmentFields.IsVisible = true;

        var attachmentText = type.AttachmentRequired
            ? "المرفق إلزامي"
            : "المرفق اختياري";

        if (!string.IsNullOrWhiteSpace(type.AttachmentLabel))
            attachmentText += $" · {type.AttachmentLabel}";

        RequestAttachmentHint.Text = attachmentText;
        RequestReasonEditor.Placeholder = type.ReasonRequired
            ? "السبب / الملاحظات (إلزامي)"
            : "السبب / الملاحظات";
    }

    private void ResetRequestAttachment()
    {
        _requestAttachment = null;
        RequestAttachmentName.Text = "لم يتم اختيار ملف";
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

    private sealed class HomeCacheSnapshot
    {
        public EmployeeProfile? Profile { get; set; }
        public List<AttendanceDay>? Attendance { get; set; }
        public List<LeaveBalance>? Leave { get; set; }
        public DateTime CachedAtUtc { get; set; }
    }
}