using System.Globalization;
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
    private string _activeSection = "Home";
    private FileResult? _requestAttachment;
    private EmployeeProfile? _currentProfile;
    private List<AttendanceDay> _currentAttendance = new();
    private List<LeaveBalance> _currentLeave = new();

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
            SetPunchMessage("لا يمكن تسجيل البصمة بدون إنترنت.");
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
                    SetPunchMessage("يجب منح صلاحية الموقع لتسجيل البصمة.");
                    return;
                }

                var location =
                    await Geolocation.Default.GetLocationAsync(
                        new GeolocationRequest(
                            GeolocationAccuracy.High,
                            TimeSpan.FromSeconds(15)));

                if (location is null)
                {
                    SetPunchMessage("تعذر الحصول على موقع الجهاز.");
                    return;
                }

                if (location.Accuracy is double accuracy && accuracy > 150)
                {
                    SetPunchMessage(
                        $"دقة الموقع الحالية ±{accuracy:0} م. فعّل الموقع الدقيق ثم أعد المحاولة.");
                    return;
                }

                var result =
                    await _api.PunchAsync(
                        type,
                        location.Latitude,
                        location.Longitude);

                SetPunchMessage(result.Message);
                await LoadCoreAsync();
            }
            catch (MobileSessionExpiredException ex)
            {
                ShowLogin();
                Error(ex.Message);
            }
            catch (MobileApiException ex)
            {
                SetPunchMessage(ex.Message);
            }
            catch (PermissionException)
            {
                SetPunchMessage("صلاحية الموقع مرفوضة.");
            }
            catch (FeatureNotEnabledException)
            {
                SetPunchMessage("GPS غير مفعّل.");
            }
            catch
            {
                SetPunchMessage("تعذر تسجيل البصمة حالياً.");
            }
        });
    }

    private async void OnHomeTab(object? sender, EventArgs e)
    {
        ShowHome();
        await MainScrollView.ScrollToAsync(0, 0, true);

        if (Online() && !_busy)
            await LoadAsync();
    }

    private async void OnAttendanceTab(object? sender, EventArgs e)
    {
        ShowAttendance();
        await MainScrollView.ScrollToAsync(0, 0, true);

        if (Online() && !_busy)
            await LoadAsync();
    }

    private async void OnProfileTab(object? sender, EventArgs e)
    {
        ShowProfile();
        await MainScrollView.ScrollToAsync(0, 0, true);

        if (Online() && !_busy)
            await LoadAsync();
    }

    private async void OnCompensationTab(object? sender, EventArgs e)
    {
        ShowCompensation();
        await MainScrollView.ScrollToAsync(0, 0, true);

        if (Online() && !_busy)
            await LoadAsync();
    }

    private async void OnRequestsTab(object? sender, EventArgs e)
    {
        ShowRequests();
        await MainScrollView.ScrollToAsync(0, 0, true);

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
            LoginCard.IsVisible = false;
            ShowActiveSection();

            var message = loadedCache
                ? "وضع عدم الاتصال — يتم عرض آخر بيانات محفوظة على الجهاز."
                : "أنت غير متصل بالإنترنت ولا توجد بيانات محفوظة بعد.";
            SetPunchMessage(message);
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

            var announcements = new List<MobileAnnouncement>();
            try
            {
                announcements = await _api.AnnouncementsAsync();
            }
            catch (MobileApiException)
            {
                // الإعلان إضافة تدريجية؛ لا نعطّل بيانات الموظف الأساسية عند تعذرها.
            }

            var compensation = new MobileCompensation();
            try
            {
                compensation = await _api.CompensationAsync();
            }
            catch (MobileApiException)
            {
                // التعويضات حساسة وإضافية؛ تعذرها لا يعطّل باقي تجربة الموظف.
            }

            RenderHome(
                profile,
                attendance,
                leave,
                announcements,
                compensation);
            await SaveHomeCacheAsync(profile, attendance, leave);

            ErrorLabel.IsVisible = false;
            LoginCard.IsVisible = false;
            AuthNav.IsVisible = true;
            ShowActiveSection();
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
                SetPunchMessage(ex.Message);
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
                SetPunchMessage("تعذر تحديث البيانات؛ يتم عرض آخر نسخة محفوظة.");
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
        List<LeaveBalance> leave,
        List<MobileAnnouncement>? announcements = null,
        MobileCompensation? compensation = null)
    {
        var now = DateTime.Now;
        GreetingLabel.Text = now.Hour switch
        {
            < 12 => "صباح الخير",
            < 17 => "مساء الخير",
            _ => "مساء الخير"
        };
        TodayLabel.Text = now.ToString(
            "dddd، d MMMM",
            CultureInfo.GetCultureInfo("ar-IQ"));
        AttendancePageTodayLabel.Text = TodayLabel.Text;

        _currentProfile = profile;
        _currentAttendance = attendance;
        _currentLeave = leave;

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

        ProfileNameLabel.Text =
            string.IsNullOrWhiteSpace(profile.FullName)
                ? "موظف ZYNORA"
                : profile.FullName;
        ProfilePositionLabel.Text =
            string.IsNullOrWhiteSpace(profile.Position)
                ? "بدون منصب"
                : profile.Position;
        ProfileEmployeeNoLabel.Text =
            string.IsNullOrWhiteSpace(profile.EmployeeNo)
                ? "—"
                : profile.EmployeeNo;
        ProfileDepartmentLabel.Text =
            string.IsNullOrWhiteSpace(profile.Department)
                ? "—"
                : profile.Department;
        ProfileBranchLabel.Text =
            string.IsNullOrWhiteSpace(profile.Branch)
                ? "—"
                : profile.Branch;
        ProfileStatusLabel.Text = profile.IsActive ? "ACTIVE" : "INACTIVE";
        ProfileStatusLabel.TextColor = profile.IsActive
            ? Color.FromArgb("#3FD49B")
            : Color.FromArgb("#FF7383");

        AttendanceHistoryContainer.BindingContext = attendance;
        AttendanceEmptyLabel.IsVisible = attendance.Count == 0;
        ProfileLeaveBalancesContainer.BindingContext = leave;

        var homeAnnouncements = announcements ?? new List<MobileAnnouncement>();
        AnnouncementsList.ItemsSource = homeAnnouncements;
        AnnouncementsEmptyLabel.IsVisible = homeAnnouncements.Count == 0;

        RenderCompensation(compensation ?? new MobileCompensation());

        if (attendance.Count > 0)
        {
            var row = attendance[0];
            AttendanceLabel.Text = row.Status;
            AttendanceDetailLabel.Text =
                $"{row.Date} · {row.CheckIn ?? "—"} → {row.CheckOut ?? "—"}";
            AttendancePageStatusLabel.Text = row.Status;
            AttendancePageDetailLabel.Text = AttendanceDetailLabel.Text;
        }
        else
        {
            AttendanceLabel.Text = "لا توجد بيانات";
            AttendanceDetailLabel.Text = "آخر 7 أيام";
            AttendancePageStatusLabel.Text = "لا توجد بيانات";
            AttendancePageDetailLabel.Text = "لا توجد حركات حضور في آخر 7 أيام.";
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

    private void RenderCompensation(MobileCompensation compensation)
    {
        CompensationEmptyLabel.IsVisible = !compensation.HasData;

        if (!compensation.HasData)
        {
            CompensationNetLabel.Text = "—";
            CompensationCurrencyLabel.Text = "IQD";
            CompensationBasicLabel.Text = "—";
            CompensationAllowancesLabel.Text = "—";
            CompensationDeductionsLabel.Text = "—";
            CompensationPaymentMethodLabel.Text = "—";
            CompensationBankLabel.Text = "—";
            CompensationAccountLabel.Text = "—";
            return;
        }

        var currency = string.IsNullOrWhiteSpace(compensation.Currency)
            ? "IQD"
            : compensation.Currency.Trim();

        CompensationNetLabel.Text = compensation.Net.ToString("N0");
        CompensationCurrencyLabel.Text = currency;
        CompensationBasicLabel.Text = compensation.BasicSalary.ToString("N0");
        CompensationAllowancesLabel.Text = compensation.Allowances.ToString("N0");
        CompensationDeductionsLabel.Text = compensation.Deductions.ToString("N0");
        CompensationPaymentMethodLabel.Text =
            string.IsNullOrWhiteSpace(compensation.PaymentMethod)
                ? "—"
                : compensation.PaymentMethod;
        CompensationBankLabel.Text =
            string.IsNullOrWhiteSpace(compensation.BankName)
                ? "—"
                : compensation.BankName;
        CompensationAccountLabel.Text =
            string.IsNullOrWhiteSpace(compensation.BankAccount)
                ? "—"
                : compensation.BankAccount;
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
        _activeSection = "Login";
        LoginCard.IsVisible = true;
        AuthNav.IsVisible = false;
        HomePanel.IsVisible = false;
        AttendancePanel.IsVisible = false;
        RequestsPanel.IsVisible = false;
        ProfilePanel.IsVisible = false;
        CompensationPanel.IsVisible = false;
    }

    private void ShowHome()
    {
        _activeSection = "Home";
        ShowAuthenticatedPanel(HomePanel, HomeTabButton);
    }

    private void ShowAttendance()
    {
        _activeSection = "Attendance";
        ShowAuthenticatedPanel(AttendancePanel, AttendanceTabButton);
    }

    private void ShowRequests()
    {
        _activeSection = "Requests";
        ShowAuthenticatedPanel(RequestsPanel, RequestsTabButton);
    }

    private void ShowProfile()
    {
        _activeSection = "Profile";
        ShowAuthenticatedPanel(ProfilePanel, ProfileTabButton);
    }

    private void ShowCompensation()
    {
        _activeSection = "Compensation";
        ShowAuthenticatedPanel(CompensationPanel, HomeTabButton);
    }

    private void ShowActiveSection()
    {
        switch (_activeSection)
        {
            case "Attendance":
                ShowAttendance();
                break;
            case "Requests":
                ShowRequests();
                break;
            case "Profile":
                ShowProfile();
                break;
            case "Compensation":
                ShowCompensation();
                break;
            default:
                ShowHome();
                break;
        }
    }

    private void ShowAuthenticatedPanel(View activePanel, Button activeTab)
    {
        LoginCard.IsVisible = false;
        AuthNav.IsVisible = true;

        HomePanel.IsVisible = ReferenceEquals(activePanel, HomePanel);
        AttendancePanel.IsVisible = ReferenceEquals(activePanel, AttendancePanel);
        RequestsPanel.IsVisible = ReferenceEquals(activePanel, RequestsPanel);
        ProfilePanel.IsVisible = ReferenceEquals(activePanel, ProfilePanel);
        CompensationPanel.IsVisible = ReferenceEquals(activePanel, CompensationPanel);

        SetNavState(activeTab);
    }

    private void SetNavState(Button activeButton)
    {
        foreach (var button in new[]
                 {
                     HomeTabButton,
                     AttendanceTabButton,
                     RequestsTabButton,
                     ProfileTabButton
                 })
        {
            button.BackgroundColor = Colors.Transparent;
            button.TextColor = Color.FromArgb("#8FA9BE");
        }

        activeButton.BackgroundColor = Color.FromArgb("#18C7BD");
        activeButton.TextColor = Color.FromArgb("#07111F");
    }

    private void SetPunchMessage(string message)
    {
        PunchLabel.Text = message;
        AttendancePunchLabel.Text = message;
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