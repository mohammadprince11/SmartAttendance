using System.Globalization;
using System.Text.Json;
using Microsoft.Maui.Layouts;

namespace ZynoraHR.Mobile;

public partial class MainPage : ContentPage
{
    private const string HomeCacheKeyPrefix = "zynora.mobile.home_cache.v2";
    private const string LegacyHomeCacheKey = "zynora.mobile.home_cache.v1";
    private static string HomeCacheKey =>
        $"{HomeCacheKeyPrefix}.{UiLocalization.CurrentLanguage}";
    private const string BiometricLockKey = "zynora.mobile.biometric_lock.v1";
    private const string TenantCodePreferenceKey = "zynora.mobile.tenant_code.v1";
    private const string ProfilePhotoCacheFileName = "zynora-profile-photo.bin";
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
    private List<MobileRequestType> _requestCatalog = new();
    private bool _requestsLoaded;
    private MobileRequestType? _overtimeRequestType;
    private string _requestCategory = "الإجازات";
    private Action? _serviceBackAction;

    public MainPage()
    {
        InitializeComponent();
        TenantCodeEntry.Text = Preferences.Default.Get(TenantCodePreferenceKey, string.Empty);
        FlowDirection = UiLocalization.CurrentFlowDirection;
        UiLocalization.Attach(this);

        RequestTypePicker.ItemDisplayBinding =
            new Binding(nameof(MobileRequestType.DisplayName));
        FromDatePicker.Date = DateTime.Today;
        ToDatePicker.Date = DateTime.Today;
        RequestStartTimePicker.Time = DateTime.Now.TimeOfDay;
        RequestEndTimePicker.Time = DateTime.Now.AddHours(1).TimeOfDay;
        MissingDatePicker.Date = DateTime.Today;
        MissingTimePicker.Time = DateTime.Now.TimeOfDay;

        ModalFromDatePicker.Date = DateTime.Today;
        ModalToDatePicker.Date = DateTime.Today;
        ModalStartTimePicker.Time = DateTime.Now.TimeOfDay;
        ModalEndTimePicker.Time = DateTime.Now.AddHours(1).TimeOfDay;
        UpdateModalDateSummary();
        UpdateModalDuration();
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

    protected override bool OnBackButtonPressed()
    {
        if (LogoutConfirmOverlay.IsVisible)
        {
            HideLogoutConfirmation();
            return true;
        }

        if (RequestTypeDropdownPanel.IsVisible)
        {
            RequestTypeDropdownPanel.IsVisible = false;
            return true;
        }

        if (ModalDateRangePanel.IsVisible)
        {
            ModalDateRangePanel.IsVisible = false;
            return true;
        }

        if (RequestTypeOverlay.IsVisible)
        {
            RequestTypeOverlay.IsVisible = false;
            return true;
        }

        if (RequestSheetOverlay.IsVisible)
        {
            RequestSheetOverlay.IsVisible = false;
            return true;
        }

        if (ServiceRequestOverlay.IsVisible)
        {
            var backAction = _serviceBackAction;
            if (backAction is null)
                CloseServiceRequest();
            else
                backAction();
            return true;
        }

        if (MoreOverlay.IsVisible)
        {
            MoreOverlay.IsVisible = false;
            ShowActiveSection();
            return true;
        }

        if (string.Equals(_activeSection, "Login", StringComparison.Ordinal) ||
            LoginCard.IsVisible)
            return base.OnBackButtonPressed();

        if (!string.Equals(_activeSection, "Home", StringComparison.Ordinal))
        {
            ShowHome();
            return true;
        }

        return base.OnBackButtonPressed();
    }

    private async Task InitializeAsync()
    {
        var hasSession = await _api.HasSessionAsync();
        if (hasSession && Preferences.Default.Get(BiometricLockKey, false))
        {
            if (!DeviceBiometricAuth.IsAvailable())
            {
                ShowLogin();
                Error("قفل البصمة مفعّل لكن لا توجد بصمة/وجه متاحة على هذا الجهاز.");
                return;
            }

            var unlocked = await DeviceBiometricAuth.AuthenticateAsync(
                UiLocalization.T("فتح ZYNORA HR"),
                UiLocalization.T("تحقق ببصمة الوجه أو الأصبع للمتابعة."));

            if (!unlocked)
            {
                ShowLogin(allowBiometricRetry: true);
                Error("تم إلغاء التحقق البيومتري. يمكنك تسجيل الدخول بكلمة المرور.");
                return;
            }
        }

        if (hasSession) await LoadAsync();
        else ShowLogin();
    }

    private async void OnBiometricRetryClicked(object? sender, EventArgs e)
    {
        if (_busy)
            return;

        if (!await _api.HasSessionAsync() ||
            !Preferences.Default.Get(BiometricLockKey, false))
        {
            ShowLogin();
            return;
        }

        if (!DeviceBiometricAuth.IsAvailable())
        {
            ShowLogin();
            Error("قفل البصمة مفعّل لكن لا توجد بصمة/وجه متاحة على هذا الجهاز.");
            return;
        }

        var unlocked = await DeviceBiometricAuth.AuthenticateAsync(
            UiLocalization.T("فتح ZYNORA HR"),
            UiLocalization.T("تحقق ببصمة الوجه أو الأصبع للمتابعة."));

        if (!unlocked)
        {
            ShowLogin(allowBiometricRetry: true);
            Error("تم إلغاء التحقق البيومتري. يمكنك تسجيل الدخول بكلمة المرور.");
            return;
        }

        BiometricRetryButton.IsVisible = false;
        await LoadAsync();
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        if (_busy) return;

        var tenantCode = TenantCodeEntry.Text?.Trim();
        var username = UsernameEntry.Text?.Trim();
        var password = PasswordEntry.Text;

        if (tenantCode is not { Length: 4 } || !tenantCode.All(char.IsAsciiDigit) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password))
        {
            Error("أدخل كود المنظومة من 4 أرقام واسم المستخدم وكلمة المرور.");
            return;
        }

        var factorInput = TwoFactorCodeEntry.Text?.Trim();
        if (TwoFactorLoginPanel.IsVisible && string.IsNullOrWhiteSpace(factorInput))
        {
            Error("أدخل رمز Authenticator أو Recovery Code.");
            return;
        }

        var normalizedDigits = string.IsNullOrWhiteSpace(factorInput)
            ? string.Empty
            : new string(factorInput.Where(char.IsDigit).ToArray());
        var twoFactorCode =
            normalizedDigits.Length == 6 && normalizedDigits.Length == factorInput?.Length
                ? normalizedDigits
                : null;
        var recoveryCode = twoFactorCode is null ? factorInput : null;

        await Busy("جاري تسجيل الدخول...", async () =>
        {
            try
            {
                var result = await _api.LoginAsync(
                    tenantCode,
                    username,
                    password,
                    twoFactorCode,
                    recoveryCode);

                if (result.RequiresTwoFactor)
                {
                    TwoFactorLoginPanel.IsVisible = true;
                    LoginButton.Text = "تحقق وتسجيل الدخول";
                    TwoFactorCodeEntry.Focus();
                    Error(result.Message ?? "أدخل رمز المصادقة الثنائية.");
                    return;
                }

                PasswordEntry.Text = "";
                Preferences.Default.Set(TenantCodePreferenceKey, tenantCode);
                TwoFactorCodeEntry.Text = "";
                TwoFactorLoginPanel.IsVisible = false;
                LoginButton.Text = "دخول إلى ZYNORA";
                _activeSection = "Home";
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

        var todayKey = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var todayRow = _currentAttendance.FirstOrDefault(row =>
            string.Equals(row.Date, todayKey, StringComparison.OrdinalIgnoreCase));

        if (string.Equals(type, "In", StringComparison.OrdinalIgnoreCase) &&
            todayRow is not null &&
            !string.IsNullOrWhiteSpace(todayRow.CheckIn))
        {
            SetPunchMessage("تم تسجيل الدخول لهذا اليوم مسبقاً.");
            return;
        }

        if (string.Equals(type, "Out", StringComparison.OrdinalIgnoreCase))
        {
            if (todayRow is null || string.IsNullOrWhiteSpace(todayRow.CheckIn))
            {
                SetPunchMessage("لا يمكن تسجيل الخروج قبل تسجيل الدخول.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(todayRow.CheckOut))
            {
                SetPunchMessage("تم تسجيل الخروج لهذا اليوم مسبقاً.");
                return;
            }
        }

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
                    AttendanceLocationLabel.Text =
                        "صلاحية الموقع غير مفعلة — اضغط «تحديث الموقع» للسماح.";
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
                    AttendanceLocationLabel.Text = "تعذر الحصول على موقع الجهاز.";
                    SetPunchMessage("تعذر الحصول على موقع الجهاز.");
                    return;
                }

                SetLocationPreview(location);

                if (location.Accuracy is double accuracy && accuracy > 150)
                {
                    SetPunchMessage(
                        $"دقة الموقع الحالية ±{accuracy:0} م. فعّل الموقع الدقيق ثم أعد المحاولة.");
                    return;
                }

                string? bioToken = null;
                var biometricKeys = await _api.BiometricKeysAsync();
                if (biometricKeys.Any(key =>
                        string.Equals(
                            key.Status,
                            "Active",
                            StringComparison.OrdinalIgnoreCase)))
                {
                    SetPunchMessage("أكد هويتك ببصمة الوجه أو الأصبع...");
                    var begin = await _api.BeginBiometricPunchAsync();
                    if (string.IsNullOrWhiteSpace(begin.Key) ||
                        begin.Options.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                    {
                        throw new MobileApiException(
                            "استجابة التأكيد البيولوجي غير صالحة.");
                    }

                    var assertionJson =
                        await AndroidPasskeyAuthentication.GetAsync(
                            begin.Options.GetRawText());

                    bioToken =
                        await _api.CompleteBiometricPunchAsync(
                            begin.Key,
                            assertionJson);
                }

                var result =
                    await _api.PunchAsync(
                        type,
                        location.Latitude,
                        location.Longitude,
                        bioToken);

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
                AttendanceLocationLabel.Text = "صلاحية الموقع مرفوضة.";
                SetPunchMessage("صلاحية الموقع مرفوضة.");
            }
            catch (FeatureNotEnabledException)
            {
                AttendanceLocationLabel.Text = "GPS غير مفعّل — فعّل الموقع في الهاتف.";
                SetPunchMessage("GPS غير مفعّل.");
            }
            catch
            {
                AttendanceLocationLabel.Text = "تعذر التحقق من الموقع حالياً.";
                SetPunchMessage("تعذر تسجيل البصمة حالياً.");
            }
        });
    }

    private async void OnRefreshLocationTapped(object? sender, EventArgs e) =>
        await RefreshLocationPreviewAsync(requestPermission: true);

    private async Task RefreshLocationPreviewAsync(bool requestPermission)
    {
        AttendanceLocationLabel.Text = "جاري التحقق من حالة الموقع...";

        try
        {
            var permission =
                await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();

            if (permission != PermissionStatus.Granted && requestPermission)
                permission =
                    await Permissions.RequestAsync<Permissions.LocationWhenInUse>();

            if (permission != PermissionStatus.Granted)
            {
                AttendanceLocationLabel.Text =
                    "صلاحية الموقع غير مفعلة — اضغط «تحديث الموقع» للسماح.";
                return;
            }

            Location? location = null;

            if (!requestPermission)
            {
                try
                {
                    location = await Geolocation.Default.GetLastKnownLocationAsync();
                }
                catch
                {
                }
            }

            if (location is null)
            {
                location =
                    await Geolocation.Default.GetLocationAsync(
                        new GeolocationRequest(
                            requestPermission
                                ? GeolocationAccuracy.High
                                : GeolocationAccuracy.Medium,
                            TimeSpan.FromSeconds(requestPermission ? 12 : 6)));
            }

            if (location is null)
            {
                AttendanceLocationLabel.Text = "تعذر الحصول على موقع الجهاز.";
                return;
            }

            SetLocationPreview(location);
        }
        catch (PermissionException)
        {
            AttendanceLocationLabel.Text = "صلاحية الموقع مرفوضة.";
        }
        catch (FeatureNotEnabledException)
        {
            AttendanceLocationLabel.Text = "GPS غير مفعّل — فعّل الموقع في الهاتف.";
        }
        catch
        {
            AttendanceLocationLabel.Text = "تعذر التحقق من الموقع حالياً.";
        }
    }

    private void SetLocationPreview(Location location)
    {
        var accuracy = location.Accuracy is double value
            ? UiLocalization.T($"±{value:0} م")
            : UiLocalization.T("الدقة غير متاحة");

        AttendanceLocationLabel.Text =
            UiLocalization.T($"الموقع جاهز · {accuracy}\n") +
            $"{location.Latitude:F5}, {location.Longitude:F5}";
    }

    private async void OnHomeTab(object? sender, EventArgs e) =>
        await SwitchSectionAsync("Home", ShowHome);

    private async void OnAttendanceTab(object? sender, EventArgs e)
    {
        await SwitchSectionAsync("Attendance", ShowAttendance);
        await RefreshLocationPreviewAsync(requestPermission: false);
    }

    private async void OnFingerprintTab(object? sender, EventArgs e)
    {
        ShowAttendance();
        await Task.Yield();
        await MainScrollView.ScrollToAsync(
            AttendancePunchLabel,
            ScrollToPosition.Center,
            false);
    }

    private async void OnProfileTab(object? sender, EventArgs e) =>
        await SwitchSectionAsync("Profile", ShowProfile);

    private void OnMoreTab(object? sender, EventArgs e)
    {
        MoreOverlay.IsVisible = true;
        SetNavState(ProfileTabButton);
    }

    private async void OnProfileFromMore(object? sender, EventArgs e)
    {
        MoreOverlay.IsVisible = false;
        await SwitchSectionAsync("Profile", ShowProfile);
    }

    private void OnCloseMoreTapped(object? sender, TappedEventArgs e)
    {
        MoreOverlay.IsVisible = false;
        ShowActiveSection();
    }

    private async void OnCompensationTab(object? sender, EventArgs e) =>
        await SwitchSectionAsync("Compensation", ShowCompensation);

    private async void OnSurveysTapped(object? sender, EventArgs e)
    {
        MoreOverlay.IsVisible = false;
        await BuildSurveysServiceAsync();
    }

    private async Task BuildSurveysServiceAsync()
    {
        OpenServiceRequest(
            "الاستبيانات",
            "الاستبيانات المتاحة لك حسب شروط الأهلية في ZYNORA.");

        var status = ServiceStatus();
        status.Text = "جاري تحميل الاستبيانات...";
        ServiceRequestHost.Children.Add(status);

        if (!Online())
        {
            status.Text = "اتصل بالإنترنت لعرض الاستبيانات.";
            return;
        }

        try
        {
            var pollsTask = _api.PollsAsync();
            var surveysTask = _api.SurveysAsync();
            await Task.WhenAll(pollsTask, surveysTask);

            var polls = await pollsTask;
            var items = await surveysTask;
            ServiceRequestHost.Children.Clear();

            if (polls.Count == 0 && items.Count == 0)
            {
                ServiceRequestHost.Children.Add(
                    ServiceHint("لا توجد استبيانات متاحة لك حالياً."));
                return;
            }

            if (polls.Count > 0)
            {
                ServiceRequestHost.Children.Add(ServiceFieldTitle("استطلاعات Pulse"));

                foreach (var poll in polls)
                {
                    var suffix = poll.HasVoted ? "  ·  تم التصويت" : "";
                    ServiceRequestHost.Children.Add(
                        ServiceFieldTitle($"{poll.Title}{suffix}"));

                    if (!string.IsNullOrWhiteSpace(poll.Question))
                        ServiceRequestHost.Children.Add(ServiceHint(poll.Question));

                    var meta = string.Join(
                        " · ",
                        new[]
                        {
                            poll.Category,
                            poll.PublishDate?.ToLocalTime().ToString("yyyy-MM-dd")
                        }.Where(value => !string.IsNullOrWhiteSpace(value)));
                    if (!string.IsNullOrWhiteSpace(meta))
                        ServiceRequestHost.Children.Add(ServiceHint(meta));

                    if (poll.HasVoted)
                    {
                        ServiceRequestHost.Children.Add(
                            ServiceHint("تم تسجيل تصويتك مسبقاً لهذا الاستطلاع."));
                        continue;
                    }

                    var selectedOptionId = 0;
                    var optionButtons = new List<Button>();
                    var optionLabels = poll.Options.ToDictionary(
                        option => option.Id,
                        option => option.Text);

                    foreach (var option in poll.Options.OrderBy(option => option.DisplayOrder))
                    {
                        var optionButton = ServiceSelectorButton($"○  {option.Text}");
                        optionButton.CommandParameter = option.Id;
                        optionButton.Clicked += (_, _) =>
                        {
                            selectedOptionId = option.Id;

                            foreach (var candidate in optionButtons)
                            {
                                var candidateId = candidate.CommandParameter is int id
                                    ? id
                                    : 0;
                                var label = optionLabels.TryGetValue(candidateId, out var text)
                                    ? text
                                    : candidate.Text;
                                candidate.Text = candidateId == selectedOptionId
                                    ? $"●  {label}"
                                    : $"○  {label}";
                            }
                        };
                        optionButtons.Add(optionButton);
                        ServiceRequestHost.Children.Add(optionButton);
                    }

                    var vote = ServicePrimaryButton("إرسال التصويت");
                    vote.IsEnabled = false;
                    var voteStatus = ServiceStatus();
                    voteStatus.Text = "اختر إجابة أولاً.";

                    foreach (var optionButton in optionButtons)
                    {
                        optionButton.Clicked += (_, _) =>
                        {
                            vote.IsEnabled = selectedOptionId > 0 && Online();
                            voteStatus.Text = vote.IsEnabled
                                ? "جاهز لإرسال التصويت."
                                : "اتصل بالإنترنت لإرسال التصويت.";
                        };
                    }

                    vote.Clicked += async (_, _) =>
                    {
                        if (selectedOptionId <= 0 || !Online())
                            return;

                        vote.IsEnabled = false;
                        foreach (var button in optionButtons)
                            button.IsEnabled = false;
                        voteStatus.Text = "جاري تسجيل التصويت...";

                        try
                        {
                            var result = await _api.VotePollAsync(
                                poll.Id,
                                selectedOptionId);

                            poll.HasVoted = true;
                            vote.Text = "تم التصويت";
                            voteStatus.Text = string.IsNullOrWhiteSpace(result.Message)
                                ? "تم تسجيل تصويتك."
                                : result.Message;
                        }
                        catch (MobileSessionExpiredException ex)
                        {
                            CloseServiceRequest();
                            ShowLogin();
                            Error(ex.Message);
                        }
                        catch (MobileApiException ex)
                        {
                            foreach (var button in optionButtons)
                                button.IsEnabled = true;
                            vote.IsEnabled = selectedOptionId > 0 && Online();
                            voteStatus.Text = ex.Message;
                        }
                        catch
                        {
                            foreach (var button in optionButtons)
                                button.IsEnabled = true;
                            vote.IsEnabled = selectedOptionId > 0 && Online();
                            voteStatus.Text = "تعذر تسجيل التصويت حالياً.";
                        }
                    };

                    ServiceRequestHost.Children.Add(vote);
                    ServiceRequestHost.Children.Add(voteStatus);
                }
            }

            if (items.Count > 0)
            {
                ServiceRequestHost.Children.Add(ServiceFieldTitle("نماذج الاستبيان"));

                foreach (var survey in items)
                {
                    var suffix = survey.Submitted ? "  ·  تمت الإجابة" : "";
                    var button = ServiceSelectorButton($"{survey.DisplayName}{suffix}");
                    button.Clicked += async (_, _) =>
                        await BuildSurveyDetailServiceAsync(survey.Id);
                    ServiceRequestHost.Children.Add(button);

                    var meta = string.Join(
                        " · ",
                        new[] { survey.DisplayTypeLabel, survey.Description }
                            .Where(value => !string.IsNullOrWhiteSpace(value)));
                    if (!string.IsNullOrWhiteSpace(meta))
                        ServiceRequestHost.Children.Add(ServiceHint(meta));
                }
            }
        }
        catch (MobileSessionExpiredException ex)
        {
            CloseServiceRequest();
            ShowLogin();
            Error(ex.Message);
        }
        catch (MobileApiException ex)
        {
            status.Text = ex.Message;
        }
        catch
        {
            status.Text = "تعذر تحميل الاستبيانات حالياً.";
        }
    }

    private async Task BuildSurveyDetailServiceAsync(int id)
    {
        OpenServiceRequest(
            "الاستبيان",
            "جاري تحميل تفاصيل الاستبيان...",
            () => { _ = BuildSurveysServiceAsync(); });

        var status = ServiceStatus();
        status.Text = "جاري التحميل...";
        ServiceRequestHost.Children.Add(status);

        try
        {
            var survey = await _api.SurveyAsync(id);
            ServiceRequestTitle.Text = survey.DisplayName;
            ServiceRequestSubtitle.Text = string.IsNullOrWhiteSpace(survey.Description)
                ? survey.DisplayTypeLabel
                : survey.Description;
            ServiceRequestHost.Children.Clear();

            var answers = new Dictionary<int, string?>();
            var submissionToken = Guid.NewGuid();
            var submittedNow = false;
            Button? submit = null;
            Label? submitStatus = null;

            void UpdateSubmitState()
            {
                if (submit is null || submittedNow)
                    return;

                var requiredComplete = survey.Fields
                    .Where(field => field.Required)
                    .All(field =>
                        answers.TryGetValue(field.Id, out var value) &&
                        !string.IsNullOrWhiteSpace(value));

                submit.IsEnabled =
                    Online() &&
                    survey.Fields.Count > 0 &&
                    requiredComplete;

                if (submitStatus is not null)
                {
                    submitStatus.Text = requiredComplete
                        ? (Online()
                            ? "جاهز لإرسال الإجابات."
                            : "اتصل بالإنترنت لإرسال الإجابات.")
                        : "أكمل الحقول الإلزامية أولاً.";
                }
            }

            ServiceRequestHost.Children.Add(ServiceHint(
                survey.Submitted
                    ? "تم تسجيل إجابة سابقة. يمكنك إرسال إجابة جديدة إذا كان الاستبيان ما زال متاحاً."
                    : "أجب عن الحقول المطلوبة ثم أرسل الاستبيان."));

            void AddChoiceField(MobileSurveyField field, IReadOnlyList<string> options)
            {
                var trigger = ServiceSelectorButton("— اختر —");
                var panel = ServiceOptionsPanel();

                trigger.Clicked += (_, _) =>
                    panel.IsVisible = !panel.IsVisible;

                foreach (var option in options)
                {
                    var optionButton = ServiceSelectorButton(option);
                    optionButton.Clicked += (_, _) =>
                    {
                        answers[field.Id] = option;
                        trigger.Text = option;
                        panel.IsVisible = false;
                        UpdateSubmitState();
                    };
                    panel.Children.Add(optionButton);
                }

                ServiceRequestHost.Children.Add(trigger);
                ServiceRequestHost.Children.Add(panel);
            }

            void AddField(MobileSurveyField field)
            {
                ServiceRequestHost.Children.Add(
                    ServiceFieldTitle($"{field.DisplayLabel}{(field.Required ? " *" : "")}"));

                switch (field.ControlType)
                {
                    case "TextArea":
                    {
                        var editor = ServiceEditor("اكتب إجابتك");
                        editor.TextChanged += (_, _) =>
                        {
                            answers[field.Id] = editor.Text;
                            UpdateSubmitState();
                        };
                        ServiceRequestHost.Children.Add(editor);
                        break;
                    }
                    case "Number":
                    {
                        var entry = ServiceEntry("أدخل رقماً", Keyboard.Numeric);
                        entry.TextChanged += (_, _) =>
                        {
                            answers[field.Id] = entry.Text;
                            UpdateSubmitState();
                        };
                        ServiceRequestHost.Children.Add(entry);
                        break;
                    }
                    case "Date":
                    {
                        var picker = new DatePicker
                        {
                            Date = DateTime.Today,
                            Format = "'— اختر التاريخ —'",
                            BackgroundColor = Color.FromArgb("#06101D"),
                            TextColor = Color.FromArgb("#E6F0FA"),
                            HeightRequest = 48
                        };
                        picker.DateSelected += (_, _) =>
                        {
                            var selected = picker.Date ?? DateTime.Today;
                            answers[field.Id] = selected.ToString("yyyy-MM-dd");
                            picker.Format = "yyyy-MM-dd";
                            UpdateSubmitState();
                        };
                        ServiceRequestHost.Children.Add(picker);
                        break;
                    }
                    case "Time":
                    {
                        var picker = new TimePicker
                        {
                            Time = DateTime.Now.TimeOfDay,
                            Format = "'— اختر الوقت —'",
                            BackgroundColor = Color.FromArgb("#06101D"),
                            TextColor = Color.FromArgb("#E6F0FA"),
                            HeightRequest = 48
                        };
                        picker.PropertyChanged += (_, args) =>
                        {
                            if (args.PropertyName != TimePicker.TimeProperty.PropertyName)
                                return;

                            var selected = picker.Time ?? DateTime.Now.TimeOfDay;
                            answers[field.Id] = selected.ToString(@"hh\:mm");
                            picker.Format = "HH:mm";
                            UpdateSubmitState();
                        };
                        ServiceRequestHost.Children.Add(picker);
                        break;
                    }
                    case "YesNoNa":
                        AddChoiceField(field, new[] { "نعم", "لا", "بلا إجابة" });
                        break;
                    case "Select":
                        AddChoiceField(field, field.Options);
                        break;
                    case "MultiSelect":
                    {
                        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var option in field.Options)
                        {
                            var optionButton = ServiceSelectorButton($"☐  {option}");
                            optionButton.Clicked += (_, _) =>
                            {
                                if (!selected.Add(option))
                                    selected.Remove(option);

                                optionButton.Text =
                                    $"{(selected.Contains(option) ? "☑" : "☐")}  {option}";
                                answers[field.Id] = selected.Count == 0
                                    ? null
                                    : string.Join(", ", selected);
                                UpdateSubmitState();
                            };
                            ServiceRequestHost.Children.Add(optionButton);
                        }
                        break;
                    }
                    case "Rating":
                    {
                        var row = new HorizontalStackLayout
                        {
                            Spacing = 6,
                            HorizontalOptions = LayoutOptions.Fill
                        };
                        var buttons = new List<Button>();

                        for (var value = 1; value <= Math.Max(1, field.Scale); value++)
                        {
                            var rating = value;
                            var button = ServiceSelectorButton(rating.ToString());
                            button.WidthRequest = 48;
                            button.Clicked += (_, _) =>
                            {
                                answers[field.Id] = rating.ToString();
                                foreach (var item in buttons)
                                    item.Text = item == button
                                        ? $"● {rating}"
                                        : item.CommandParameter?.ToString() ?? item.Text;

                                UpdateSubmitState();
                            };
                            button.CommandParameter = rating.ToString();
                            buttons.Add(button);
                            row.Children.Add(button);
                        }

                        ServiceRequestHost.Children.Add(row);
                        break;
                    }
                    default:
                    {
                        var placeholder = field.ControlType == "File"
                            ? "رابط أو وصف المرفق"
                            : field.ControlType is "DateRange" or "TimeRange"
                                ? "أدخل من – إلى"
                                : "اكتب إجابتك";
                        var entry = ServiceEntry(placeholder);
                        entry.TextChanged += (_, _) =>
                        {
                            answers[field.Id] = entry.Text;
                            UpdateSubmitState();
                        };
                        ServiceRequestHost.Children.Add(entry);
                        break;
                    }
                }

                if (!string.IsNullOrWhiteSpace(field.ControlLabel))
                    ServiceRequestHost.Children.Add(ServiceHint(field.ControlLabel));
            }

            foreach (var group in survey.Groups.OrderBy(item => item.SortOrder))
            {
                var fields = survey.Fields
                    .Where(field => field.GroupId == group.Id)
                    .OrderBy(field => field.SortOrder)
                    .ToList();
                if (fields.Count == 0)
                    continue;

                ServiceRequestHost.Children.Add(ServiceFieldTitle(group.DisplayName));
                foreach (var field in fields)
                    AddField(field);
            }

            var ungrouped = survey.Fields
                .Where(field => field.GroupId is null)
                .OrderBy(field => field.SortOrder)
                .ToList();

            if (ungrouped.Count > 0)
            {
                ServiceRequestHost.Children.Add(ServiceFieldTitle("الأسئلة"));
                foreach (var field in ungrouped)
                    AddField(field);
            }

            submit = ServicePrimaryButton(
                survey.Submitted ? "إرسال إجابة جديدة" : "إرسال إجاباتي");
            submit.IsEnabled = false;
            submitStatus = ServiceStatus();

            submit.Clicked += async (_, _) =>
            {
                UpdateSubmitState();
                if (!submit.IsEnabled)
                    return;

                submit.IsEnabled = false;
                submitStatus.Text = "جاري إرسال الإجابات...";

                try
                {
                    var result = await _api.SubmitSurveyAsync(
                        survey.Id,
                        submissionToken,
                        answers);

                    submittedNow = true;
                    submit.Text = "تم إرسال الإجابات";
                    submit.IsEnabled = false;
                    submitStatus.Text = string.IsNullOrWhiteSpace(result.Message)
                        ? "شكراً — سُجِّلت إجاباتك."
                        : result.Message;
                }
                catch (MobileSessionExpiredException ex)
                {
                    CloseServiceRequest();
                    ShowLogin();
                    Error(ex.Message);
                }
                catch (MobileApiException ex)
                {
                    submitStatus.Text = ex.Message;
                    UpdateSubmitState();
                }
                catch
                {
                    submitStatus.Text = "تعذر إرسال الإجابات حالياً.";
                    UpdateSubmitState();
                }
            };

            ServiceRequestHost.Children.Add(submit);
            ServiceRequestHost.Children.Add(submitStatus);
            UpdateSubmitState();
        }
        catch (MobileSessionExpiredException ex)
        {
            CloseServiceRequest();
            ShowLogin();
            Error(ex.Message);
        }
        catch (MobileApiException ex)
        {
            status.Text = ex.Message;
        }
        catch
        {
            status.Text = "تعذر تحميل تفاصيل الاستبيان حالياً.";
        }
    }

    private async void OnFeedbackTapped(object? sender, EventArgs e)
    {
        MoreOverlay.IsVisible = false;
        await BuildFeedbackServiceAsync();
    }

    private async Task BuildFeedbackServiceAsync()
    {
        OpenServiceRequest(
            "الشكاوى والمقترحات",
            "أرسل رسالة للموارد البشرية وتابع حالة الرد من داخل ZYNORA.");

        var status = ServiceStatus();
        status.Text = "جاري تحميل الرسائل...";
        ServiceRequestHost.Children.Add(status);

        if (!Online())
        {
            status.Text = "اتصل بالإنترنت لعرض الرسائل.";
            return;
        }

        try
        {
            var items = await _api.FeedbackAsync();
            ServiceRequestHost.Children.Clear();

            var create = ServicePrimaryButton("إرسال شكوى أو مقترح جديد");
            create.Clicked += async (_, _) =>
                await BuildFeedbackCreateServiceAsync();
            ServiceRequestHost.Children.Add(create);

            ServiceRequestHost.Children.Add(ServiceFieldTitle("متابعة الردود"));

            if (items.Count == 0)
            {
                ServiceRequestHost.Children.Add(
                    ServiceHint("لا توجد شكاوى أو مقترحات سابقة."));
                return;
            }

            foreach (var item in items)
            {
                ServiceRequestHost.Children.Add(
                    ServiceFieldTitle($"{item.DisplayType} · {item.Title}"));

                var meta = string.Join(
                    " · ",
                    new[]
                    {
                        item.DisplayPriority,
                        item.StatusText,
                        item.CreatedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                    }.Where(value => !string.IsNullOrWhiteSpace(value)));
                ServiceRequestHost.Children.Add(ServiceHint(meta));
                ServiceRequestHost.Children.Add(ServiceHint(item.Message));

                if (!string.IsNullOrWhiteSpace(item.AdminReply))
                {
                    ServiceRequestHost.Children.Add(ServiceFieldTitle("رد الإدارة"));
                    var replyMeta = string.Join(
                        " · ",
                        new[]
                        {
                            item.RepliedBy,
                            item.RepliedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                        }.Where(value => !string.IsNullOrWhiteSpace(value)));
                    ServiceRequestHost.Children.Add(ServiceHint(
                        string.IsNullOrWhiteSpace(replyMeta)
                            ? item.AdminReply
                            : $"{item.AdminReply}\n{replyMeta}"));
                }
            }
        }
        catch (MobileSessionExpiredException ex)
        {
            CloseServiceRequest();
            ShowLogin();
            Error(ex.Message);
        }
        catch (MobileApiException ex)
        {
            status.Text = ex.Message;
        }
        catch
        {
            status.Text = "تعذر تحميل الشكاوى والمقترحات حالياً.";
        }
    }

    private async Task BuildFeedbackCreateServiceAsync()
    {
        OpenServiceRequest(
            "شكوى أو مقترح",
            "لن تُرسل الرسالة إلا بعد الضغط على زر الإرسال.",
            () => { _ = BuildFeedbackServiceAsync(); });

        var type = "اقتراح";
        var priority = "متوسط";
        var sent = false;

        ServiceRequestHost.Children.Add(ServiceFieldTitle("النوع"));
        var typeButton = ServiceSelectorButton(type);
        var typePanel = ServiceOptionsPanel();
        typeButton.Clicked += (_, _) =>
            typePanel.IsVisible = !typePanel.IsVisible;

        foreach (var option in new[] { "اقتراح", "شكوى", "استفسار" })
        {
            var value = option;
            var button = ServiceSelectorButton(value);
            button.Clicked += (_, _) =>
            {
                type = value;
                typeButton.Text = value;
                typePanel.IsVisible = false;
            };
            typePanel.Children.Add(button);
        }

        ServiceRequestHost.Children.Add(typeButton);
        ServiceRequestHost.Children.Add(typePanel);

        ServiceRequestHost.Children.Add(ServiceFieldTitle("الأولوية"));
        var priorityButton = ServiceSelectorButton(priority);
        var priorityPanel = ServiceOptionsPanel();
        priorityButton.Clicked += (_, _) =>
            priorityPanel.IsVisible = !priorityPanel.IsVisible;

        foreach (var option in new[] { "منخفض", "متوسط", "عالي" })
        {
            var value = option;
            var button = ServiceSelectorButton(value);
            button.Clicked += (_, _) =>
            {
                priority = value;
                priorityButton.Text = value;
                priorityPanel.IsVisible = false;
            };
            priorityPanel.Children.Add(button);
        }

        ServiceRequestHost.Children.Add(priorityButton);
        ServiceRequestHost.Children.Add(priorityPanel);

        ServiceRequestHost.Children.Add(ServiceFieldTitle("العنوان *"));
        var title = ServiceEntry("اكتب عنواناً مختصراً");
        ServiceRequestHost.Children.Add(title);

        ServiceRequestHost.Children.Add(ServiceFieldTitle("التفاصيل *"));
        var message = ServiceEditor("اكتب التفاصيل");
        ServiceRequestHost.Children.Add(message);

        var submit = ServicePrimaryButton("إرسال للموارد البشرية");
        submit.IsEnabled = false;
        var status = ServiceStatus();

        void UpdateState()
        {
            if (sent)
                return;

            var complete =
                !string.IsNullOrWhiteSpace(title.Text) &&
                !string.IsNullOrWhiteSpace(message.Text);

            submit.IsEnabled = complete && Online();
            status.Text = complete
                ? (Online()
                    ? "جاهز للإرسال."
                    : "اتصل بالإنترنت لإرسال الرسالة.")
                : "العنوان والتفاصيل مطلوبان.";
        }

        title.TextChanged += (_, _) => UpdateState();
        message.TextChanged += (_, _) => UpdateState();

        submit.Clicked += async (_, _) =>
        {
            UpdateState();
            if (!submit.IsEnabled)
                return;

            submit.IsEnabled = false;
            status.Text = "جاري إرسال الرسالة...";

            try
            {
                var result = await _api.SubmitFeedbackAsync(
                    type,
                    priority,
                    title.Text ?? string.Empty,
                    message.Text ?? string.Empty);

                sent = true;
                submit.Text = "تم الإرسال";
                submit.IsEnabled = false;
                status.Text = string.IsNullOrWhiteSpace(result.Message)
                    ? "تم إرسال الرسالة."
                    : result.Message;
            }
            catch (MobileSessionExpiredException ex)
            {
                CloseServiceRequest();
                ShowLogin();
                Error(ex.Message);
            }
            catch (MobileApiException ex)
            {
                status.Text = ex.Message;
                UpdateState();
            }
            catch
            {
                status.Text = "تعذر إرسال الرسالة حالياً.";
                UpdateState();
            }
        };

        ServiceRequestHost.Children.Add(submit);
        ServiceRequestHost.Children.Add(status);
        UpdateState();
    }

    private async void OnTeamTapped(object? sender, EventArgs e)
    {
        MoreOverlay.IsVisible = false;
        await BuildTeamServiceAsync();
    }

    private async Task BuildTeamServiceAsync()
    {
        OpenServiceRequest(
            "فريقي",
            "الموظفون المباشرون ضمن هيكلك الإداري مع ملخص حضور اليوم.");

        var status = ServiceStatus();
        status.Text = "جاري تحميل بيانات الفريق...";
        ServiceRequestHost.Children.Add(status);

        if (!Online())
        {
            status.Text = "اتصل بالإنترنت لعرض الفريق.";
            return;
        }

        try
        {
            var items = await _api.TeamAsync();
            status.Text = items.Count == 0
                ? "لا يوجد موظفون مباشرون مرتبطون بك حالياً."
                : $"{items.Count} موظف ضمن فريقك المباشر.";

            foreach (var item in items)
            {
                ServiceRequestHost.Children.Add(
                    ServiceFieldTitle($"{item.DisplayName} · {item.EmployeeNo}"));

                var job = string.Join(
                    " · ",
                    new[] { item.DisplayPosition, item.DisplayDepartment, item.DisplayBranch }
                        .Where(value => !string.IsNullOrWhiteSpace(value)));
                if (!string.IsNullOrWhiteSpace(job))
                    ServiceRequestHost.Children.Add(ServiceHint(job));

                ServiceRequestHost.Children.Add(
                    ServiceHint($"حضور اليوم: {item.AttendanceToday}"));

                var contact = string.Join(
                    " · ",
                    new[] { item.Phone, item.Email }
                        .Where(value => !string.IsNullOrWhiteSpace(value)));
                if (!string.IsNullOrWhiteSpace(contact))
                    ServiceRequestHost.Children.Add(ServiceHint(contact));

                ServiceRequestHost.Children.Add(new BoxView
                {
                    HeightRequest = 1,
                    BackgroundColor = Color.FromArgb("#20384F"),
                    Margin = new Thickness(0, 4, 0, 6)
                });
            }
        }
        catch (MobileSessionExpiredException ex)
        {
            CloseServiceRequest();
            ShowLogin();
            Error(ex.Message);
        }
        catch (MobileApiException ex)
        {
            status.Text = ex.Message;
        }
        catch
        {
            status.Text = "تعذر تحميل بيانات الفريق حالياً.";
        }
    }

    private async void OnApprovalsTapped(object? sender, EventArgs e)
    {
        MoreOverlay.IsVisible = false;
        await BuildApprovalsServiceAsync();
    }

    private async Task BuildApprovalsServiceAsync()
    {
        OpenServiceRequest(
            "الموافقات",
            "الطلبات التي تنتظر قرارك وفق مسار الموافقات والصلاحيات الفعلية.");

        var status = ServiceStatus();
        status.Text = "جاري تحميل صندوق الموافقات...";
        ServiceRequestHost.Children.Add(status);

        if (!Online())
        {
            status.Text = "اتصل بالإنترنت لعرض الموافقات.";
            return;
        }

        try
        {
            var items = await _api.ApprovalsAsync();
            ServiceRequestHost.Children.Clear();

            ServiceRequestHost.Children.Add(ServiceHint(
                items.Count == 0
                    ? "لا توجد طلبات بانتظار قرارك حالياً."
                    : $"{items.Count} طلب بانتظار قرارك."));

            foreach (var item in items)
            {
                ServiceRequestHost.Children.Add(
                    ServiceFieldTitle($"{item.DisplayRequestType} · {item.DisplayEmployeeName}"));

                var employeeMeta = string.Join(
                    " · ",
                    new[]
                    {
                        item.EmployeeNo,
                        item.DisplayPosition,
                        item.DisplayDepartment,
                        item.DisplayBranch
                    }.Where(value => !string.IsNullOrWhiteSpace(value)));
                if (!string.IsNullOrWhiteSpace(employeeMeta))
                    ServiceRequestHost.Children.Add(ServiceHint(employeeMeta));

                var requestMeta = new List<string>
                {
                    item.DateRange
                };
                if (!string.IsNullOrWhiteSpace(item.TimeRange))
                    requestMeta.Add(item.TimeRange);
                if (item.DaysCount is decimal days)
                    requestMeta.Add(UiLocalization.T($"{days:0.##} يوم"));
                if (!string.IsNullOrWhiteSpace(item.CurrentStep))
                    requestMeta.Add($"{UiLocalization.T("الخطوة")}: {UiLocalization.Data(item.CurrentStep)}");
                if (!string.IsNullOrWhiteSpace(item.CreatedAt))
                    requestMeta.Add($"{UiLocalization.T("أُرسل")}: {item.CreatedAt}");

                ServiceRequestHost.Children.Add(
                    ServiceHint(string.Join(" · ", requestMeta)));

                if (!string.IsNullOrWhiteSpace(item.Reason))
                {
                    ServiceRequestHost.Children.Add(ServiceFieldTitle("السبب"));
                    ServiceRequestHost.Children.Add(ServiceHint(item.Reason));
                }

                if (item.Financial is { } financial)
                {
                    ServiceRequestHost.Children.Add(ServiceFieldTitle("التفاصيل المالية"));
                    var financialLines = new List<string>
                    {
                        financial.DisplayKindLabel,
                        UiLocalization.T($"المبلغ: {financial.Amount:N0}"),
                        UiLocalization.T($"الفترة: {financial.DisplayPeriod}")
                    };
                    if (financial.InstallmentCount > 1)
                        financialLines.Add(UiLocalization.T($"الأقساط: {financial.InstallmentCount}"));
                    if (!string.IsNullOrWhiteSpace(financial.DisplayPaymentType))
                        financialLines.Add(UiLocalization.T($"طريقة الصرف: {financial.DisplayPaymentType}"));
                    if (financial.Taxable)
                        financialLines.Add(UiLocalization.T("خاضع للضريبة"));
                    if (!string.IsNullOrWhiteSpace(financial.Reason))
                        financialLines.Add($"{UiLocalization.T("السبب")}: {financial.Reason}");
                    if (!string.IsNullOrWhiteSpace(financial.Note))
                        financialLines.Add($"{UiLocalization.T("ملاحظة")}: {financial.Note}");

                    ServiceRequestHost.Children.Add(
                        ServiceHint(string.Join("\n", financialLines)));
                }

                var approvedKeys = new HashSet<string>(
                    item.DataChangeFields
                        .Where(field =>
                            !string.Equals(
                                field.Decision,
                                "Rejected",
                                StringComparison.OrdinalIgnoreCase))
                        .Select(field => field.Key),
                    StringComparer.OrdinalIgnoreCase);

                if (item.DataChangeFields.Count > 0)
                {
                    ServiceRequestHost.Children.Add(
                        ServiceFieldTitle("حقول تعديل البيانات"));

                    foreach (var field in item.DataChangeFields)
                    {
                        var key = field.Key;
                        var check = new CheckBox
                        {
                            IsChecked = approvedKeys.Contains(key),
                            Color = Color.FromArgb("#19D3E0"),
                            VerticalOptions = LayoutOptions.Start
                        };
                        check.CheckedChanged += (_, args) =>
                        {
                            if (args.Value) approvedKeys.Add(key);
                            else approvedKeys.Remove(key);
                        };

                        var text = new Label
                        {
                            Text =
                                $"{field.DisplayLabel}\n" +
                                UiLocalization.T($"الحالي: {field.OldValue}\n") +
                                UiLocalization.T($"المطلوب: {field.NewValue}"),
                            TextColor = Color.FromArgb("#CFE0F0"),
                            FontSize = 11.5,
                            LineHeight = 1.35
                        };

                        var row = new Grid
                        {
                            ColumnDefinitions =
                            {
                                new ColumnDefinition(GridLength.Auto),
                                new ColumnDefinition(GridLength.Star)
                            },
                            ColumnSpacing = 8,
                            Margin = new Thickness(0, 2)
                        };
                        row.Add(check, 0);
                        row.Add(text, 1);
                        ServiceRequestHost.Children.Add(row);
                    }

                    ServiceRequestHost.Children.Add(ServiceHint(
                        "المحدد فقط سيُعتمد. الحقول غير المحددة ستُرفض ولن تُطبّق."));
                }

                ServiceRequestHost.Children.Add(ServiceFieldTitle("ملاحظة القرار"));
                var note = ServiceEditor("ملاحظة اختيارية أو سبب الرفض/الإعادة");
                note.HeightRequest = 90;
                ServiceRequestHost.Children.Add(note);

                var actions = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Star)
                    },
                    ColumnSpacing = 7
                };

                var approve = ServicePrimaryButton("اعتماد");
                var returnButton = ServiceSelectorButton("إعادة");
                var reject = ServiceSelectorButton("رفض");
                reject.TextColor = Color.FromArgb("#FFB8C3");
                reject.BorderColor = Color.FromArgb("#7A3344");

                actions.Add(approve, 0);
                actions.Add(returnButton, 1);
                actions.Add(reject, 2);
                ServiceRequestHost.Children.Add(actions);

                approve.Clicked += async (_, _) =>
                {
                    var confirmed = await DisplayAlertAsync(
                        "اعتماد الطلب",
                        UiLocalization.T($"هل تريد اعتماد طلب {item.DisplayEmployeeName}؟"),
                        "اعتماد",
                        "إلغاء");
                    if (!confirmed) return;

                    approve.IsEnabled = false;
                    try
                    {
                        var result = await _api.ApproveAsync(
                            item.Id,
                            note.Text,
                            item.DataChangeFields.Count > 0
                                ? approvedKeys.ToArray()
                                : null);
                        await DisplayAlertAsync(
                            "الموافقات",
                            string.IsNullOrWhiteSpace(result.Message)
                                ? "تم اعتماد الطلب."
                                : result.Message,
                            "حسناً");
                        await BuildApprovalsServiceAsync();
                    }
                    catch (MobileSessionExpiredException ex)
                    {
                        CloseServiceRequest();
                        ShowLogin();
                        Error(ex.Message);
                    }
                    catch (MobileApiException ex)
                    {
                        approve.IsEnabled = true;
                        await DisplayAlertAsync("تعذر الاعتماد", ex.Message, "حسناً");
                    }
                    catch
                    {
                        approve.IsEnabled = true;
                        await DisplayAlertAsync(
                            "تعذر الاعتماد",
                            "تعذر اعتماد الطلب حالياً.",
                            "حسناً");
                    }
                };

                returnButton.Clicked += async (_, _) =>
                {
                    if (string.IsNullOrWhiteSpace(note.Text))
                    {
                        await DisplayAlertAsync(
                            "سبب الإعادة مطلوب",
                            "اكتب سبب إعادة الطلب للتعديل قبل المتابعة.",
                            "حسناً");
                        return;
                    }

                    var confirmed = await DisplayAlertAsync(
                        "إعادة للتعديل",
                        $"هل تريد إعادة طلب {item.DisplayEmployeeName} للتعديل؟",
                        "إعادة",
                        "إلغاء");
                    if (!confirmed) return;

                    returnButton.IsEnabled = false;
                    try
                    {
                        var result = await _api.ReturnApprovalAsync(
                            item.Id,
                            note.Text);
                        await DisplayAlertAsync(
                            "الموافقات",
                            string.IsNullOrWhiteSpace(result.Message)
                                ? "تمت إعادة الطلب للتعديل."
                                : result.Message,
                            "حسناً");
                        await BuildApprovalsServiceAsync();
                    }
                    catch (MobileSessionExpiredException ex)
                    {
                        CloseServiceRequest();
                        ShowLogin();
                        Error(ex.Message);
                    }
                    catch (MobileApiException ex)
                    {
                        returnButton.IsEnabled = true;
                        await DisplayAlertAsync("تعذر الإرجاع", ex.Message, "حسناً");
                    }
                    catch
                    {
                        returnButton.IsEnabled = true;
                        await DisplayAlertAsync(
                            "تعذر الإرجاع",
                            "تعذر إعادة الطلب حالياً.",
                            "حسناً");
                    }
                };

                reject.Clicked += async (_, _) =>
                {
                    if (item.CommentRequiredOnReject &&
                        string.IsNullOrWhiteSpace(note.Text))
                    {
                        await DisplayAlertAsync(
                            "سبب الرفض مطلوب",
                            "مسار الموافقة يشترط كتابة تعليق قبل رفض الطلب.",
                            "حسناً");
                        return;
                    }

                    var confirmed = await DisplayAlertAsync(
                        "رفض الطلب",
                        UiLocalization.T($"هل تريد رفض طلب {item.DisplayEmployeeName}؟"),
                        "رفض",
                        "إلغاء");
                    if (!confirmed) return;

                    reject.IsEnabled = false;
                    try
                    {
                        var result = await _api.RejectApprovalAsync(
                            item.Id,
                            note.Text);
                        await DisplayAlertAsync(
                            "الموافقات",
                            string.IsNullOrWhiteSpace(result.Message)
                                ? "تم رفض الطلب."
                                : result.Message,
                            "حسناً");
                        await BuildApprovalsServiceAsync();
                    }
                    catch (MobileSessionExpiredException ex)
                    {
                        CloseServiceRequest();
                        ShowLogin();
                        Error(ex.Message);
                    }
                    catch (MobileApiException ex)
                    {
                        reject.IsEnabled = true;
                        await DisplayAlertAsync("تعذر الرفض", ex.Message, "حسناً");
                    }
                    catch
                    {
                        reject.IsEnabled = true;
                        await DisplayAlertAsync(
                            "تعذر الرفض",
                            "تعذر رفض الطلب حالياً.",
                            "حسناً");
                    }
                };

                ServiceRequestHost.Children.Add(new BoxView
                {
                    HeightRequest = 1,
                    BackgroundColor = Color.FromArgb("#20384F"),
                    Margin = new Thickness(0, 8, 0, 10)
                });
            }
        }
        catch (MobileSessionExpiredException ex)
        {
            CloseServiceRequest();
            ShowLogin();
            Error(ex.Message);
        }
        catch (MobileApiException ex)
        {
            status.Text = ex.Message;
        }
        catch
        {
            status.Text = "تعذر تحميل صندوق الموافقات حالياً.";
        }
    }

    private async void OnDisciplineTapped(object? sender, EventArgs e)
    {
        MoreOverlay.IsVisible = false;
        await BuildDisciplineServiceAsync();
    }

    private async Task BuildDisciplineServiceAsync()
    {
        OpenServiceRequest(
            "التقييم والانضباط",
            "سجل الانضباط الفعلي للموظف من قاعدة بيانات ZYNORA.");

        ServiceRequestHost.Children.Add(ServiceHint(
            "التقييم الرسمي للأداء غير مفعّل بعد في النظام. عند اعتماد دورة تقييم ستظهر نتيجتك هنا."));

        var status = ServiceStatus();
        status.Text = "جاري تحميل سجل الانضباط...";
        ServiceRequestHost.Children.Add(status);

        if (!Online())
        {
            status.Text = "اتصل بالإنترنت لعرض سجل الانضباط.";
            return;
        }

        try
        {
            var items = await _api.DisciplineAsync();
            status.Text = "";

            ServiceRequestHost.Children.Add(ServiceFieldTitle("الانضباط والعقوبات"));

            if (items.Count == 0)
            {
                ServiceRequestHost.Children.Add(
                    ServiceHint("لا توجد مخالفات أو عقوبات مسجلة على ملفك."));
                return;
            }

            foreach (var item in items)
            {
                var title = string.IsNullOrWhiteSpace(item.DisplayTitle)
                    ? UiLocalization.T("مخالفة")
                    : item.DisplayTitle;
                ServiceRequestHost.Children.Add(
                    ServiceFieldTitle($"{item.ReferenceNo} · {title}"));

                var meta = string.Join(
                    " · ",
                    new[]
                    {
                        item.EventDate,
                        item.Category,
                        item.Status,
                        item.ActionStatus
                    }.Where(value => !string.IsNullOrWhiteSpace(value)));
                if (!string.IsNullOrWhiteSpace(meta))
                    ServiceRequestHost.Children.Add(ServiceHint(meta));

                if (!string.IsNullOrWhiteSpace(item.FinalPenaltyAction))
                    ServiceRequestHost.Children.Add(
                        ServiceHint($"الإجراء النهائي: {item.FinalPenaltyAction}"));

                if (item.DeductionAmount > 0)
                    ServiceRequestHost.Children.Add(
                        ServiceHint($"الاقتطاع: {item.DeductionAmount:N0}"));

                if (!string.IsNullOrWhiteSpace(item.EmployeeReply))
                    ServiceRequestHost.Children.Add(
                        ServiceHint($"رد الموظف: {item.EmployeeReply}"));
            }
        }
        catch (MobileSessionExpiredException ex)
        {
            CloseServiceRequest();
            ShowLogin();
            Error(ex.Message);
        }
        catch (MobileApiException ex)
        {
            status.Text = ex.Message;
        }
        catch
        {
            status.Text = "تعذر تحميل سجل الانضباط حالياً.";
        }
    }

    private async void OnFeatureComingSoon(object? sender, EventArgs e)
    {
        MoreOverlay.IsVisible = false;
        await DisplayAlertAsync(
            "ZYNORA HR",
            "هذه الوحدة موجودة في بوابة الموظف القديمة وسيتم نقلها Native في المرحلة التالية.",
            "حسناً");
    }

    private async void OnRequestsTab(object? sender, EventArgs e)
    {
        await SwitchSectionAsync("Requests", ShowRequests);

        if (!Online())
        {
            if (!_requestsLoaded)
                RequestStatusLabel.Text = "اتصل بالإنترنت لعرض الطلبات.";
            return;
        }

        if (!_requestsLoaded && !_busy)
            await LoadRequestsAsync();
    }

    private async void OnRequestFabClicked(object? sender, EventArgs e)
    {
        MoreOverlay.IsVisible = false;
        RequestTypeOverlay.IsVisible = false;
        RequestSheetOvertimeButton.IsVisible = _overtimeRequestType is not null;
        RequestSheetOverlay.IsVisible = true;

        if (_requestCatalog.Count == 0 && Online())
            await EnsureRequestCatalogForSheetAsync();
    }

    private async Task EnsureRequestCatalogForSheetAsync()
    {
        if (_requestCatalog.Count > 0 || !Online())
            return;

        try
        {
            var catalog = await _api.RequestTypesAsync();
            _requestCatalog = catalog.Items ?? new();
            _overtimeRequestType = _requestCatalog.FirstOrDefault(IsOvertimeRequestType);

            RequestSheetOvertimeButton.IsVisible = _overtimeRequestType is not null;
            if (_overtimeRequestType is not null)
                RequestSheetOvertimeButton.Text = _overtimeRequestType.DisplayTypeName;
        }
        catch (MobileSessionExpiredException ex)
        {
            RequestSheetOverlay.IsVisible = false;
            ShowLogin();
            Error(ex.Message);
        }
        catch
        {
            // Keep the static request sheet available even if the catalog refresh fails.
        }
    }

    private void OnCloseRequestSheetTapped(object? sender, TappedEventArgs e) =>
        RequestSheetOverlay.IsVisible = false;

    private async void OnOpenLeaveRequestSheet(object? sender, EventArgs e)
    {
        if (_requestCatalog.Count == 0 && Online())
            await EnsureRequestCatalogForSheetAsync();

        RequestSheetOverlay.IsVisible = false;
        ShowRequests();
        await MainScrollView.ScrollToAsync(SelectedRequestTypeButton, ScrollToPosition.Center, false);
        OpenRequestTypeChooser("الإجازات");
    }

    private async void OnOpenMissingPunchFromSheet(object? sender, EventArgs e)
    {
        RequestSheetOverlay.IsVisible = false;
        await BuildMissingPunchServiceAsync();
    }

    private async void OnOpenDataChangeFromSheet(object? sender, EventArgs e)
    {
        RequestSheetOverlay.IsVisible = false;
        await BuildDataChangeServiceAsync();
    }

    private async void OnOpenFinancialFromSheet(object? sender, EventArgs e)
    {
        RequestSheetOverlay.IsVisible = false;
        await BuildFinancialServiceAsync();
    }

    private async void OnOpenShiftFromSheet(object? sender, EventArgs e)
    {
        RequestSheetOverlay.IsVisible = false;
        await BuildShiftServiceAsync();
    }

    private void OnCloseServiceRequest(object? sender, EventArgs e) =>
        CloseServiceRequest();

    private void OnCloseServiceRequestTapped(object? sender, TappedEventArgs e) =>
        CloseServiceRequest();

    private void CloseServiceRequest()
    {
        ServiceRequestOverlay.IsVisible = false;
        ServiceRequestHost.Children.Clear();
        _serviceBackAction = null;
    }

    private void OpenServiceRequest(string title, string subtitle, Action? backAction = null)
    {
        MoreOverlay.IsVisible = false;
        RequestSheetOverlay.IsVisible = false;
        RequestTypeOverlay.IsVisible = false;
        ServiceRequestHost.Children.Clear();
        _serviceBackAction = backAction;
        ServiceRequestTitle.Text = UiLocalization.T(title);
        ServiceRequestSubtitle.Text = UiLocalization.T(subtitle);
        ServiceRequestOverlay.IsVisible = true;
    }

    private static Label ServiceFieldTitle(string text) => new()
    {
        Text = UiLocalization.T(text),
        TextColor = Color.FromArgb("#CFE0F0"),
        FontSize = 12.5,
        FontAttributes = FontAttributes.Bold
    };

    private static Label ServiceHint(string text) => new()
    {
        Text = UiLocalization.T(text),
        TextColor = Color.FromArgb("#8FA6BA"),
        FontSize = 10.5,
        LineBreakMode = LineBreakMode.WordWrap
    };

    private static Label ServiceStatus() => new()
    {
        TextColor = Color.FromArgb("#9FB3C7"),
        FontSize = 11,
        HorizontalTextAlignment = TextAlignment.Center,
        LineBreakMode = LineBreakMode.WordWrap
    };

    private static Entry ServiceEntry(string placeholder, Keyboard? keyboard = null) => new()
    {
        Placeholder = UiLocalization.T(placeholder),
        PlaceholderColor = Color.FromArgb("#6F8498"),
        TextColor = Color.FromArgb("#E6F0FA"),
        BackgroundColor = Color.FromArgb("#06101D"),
        FontSize = 13,
        HeightRequest = 48,
        Keyboard = keyboard ?? Keyboard.Default
    };

    private static Editor ServiceEditor(string placeholder) => new()
    {
        Placeholder = UiLocalization.T(placeholder),
        PlaceholderColor = Color.FromArgb("#6F8498"),
        TextColor = Color.FromArgb("#E6F0FA"),
        BackgroundColor = Color.FromArgb("#06101D"),
        FontSize = 12.5,
        AutoSize = EditorAutoSizeOption.TextChanges,
        HeightRequest = 82
    };

    private static Button ServiceSelectorButton(string text) => new()
    {
        Text = UiLocalization.T(text),
        BackgroundColor = Color.FromArgb("#06101D"),
        BorderColor = Color.FromArgb("#365069"),
        BorderWidth = 1,
        TextColor = Color.FromArgb("#E6F0FA"),
        FontSize = 13,
        FontAttributes = FontAttributes.Bold,
        CornerRadius = 12,
        HeightRequest = 48,
        Padding = new Thickness(12, 8)
    };

    private static Button ServicePrimaryButton(string text) => new()
    {
        Text = UiLocalization.T(text),
        BackgroundColor = Color.FromArgb("#19CFE0"),
        TextColor = Color.FromArgb("#041018"),
        FontAttributes = FontAttributes.Bold,
        FontSize = 14,
        CornerRadius = 14,
        HeightRequest = 52
    };

    private new Task DisplayAlertAsync(string title, string message, string cancel) =>
        base.DisplayAlertAsync(
            UiLocalization.T(title),
            UiLocalization.T(message),
            UiLocalization.T(cancel));

    private new Task<bool> DisplayAlertAsync(
        string title,
        string message,
        string accept,
        string cancel) =>
        base.DisplayAlertAsync(
            UiLocalization.T(title),
            UiLocalization.T(message),
            UiLocalization.T(accept),
            UiLocalization.T(cancel));

    private static VerticalStackLayout ServiceOptionsPanel() => new()
    {
        IsVisible = false,
        Spacing = 3,
        Padding = new Thickness(6),
        BackgroundColor = Color.FromArgb("#0B2033")
    };

    private async void OnNotificationsTapped(object? sender, TappedEventArgs e)
    {
        if (LoginCard.IsVisible)
            return;

        await BuildNotificationsServiceAsync();
    }

    private void OnSettingsTapped(object? sender, TappedEventArgs e)
    {
        if (LoginCard.IsVisible)
            return;

        BuildSettingsService();
    }

    private async Task BuildNotificationsServiceAsync()
    {
        OpenServiceRequest(
            "الإشعارات",
            "آخر الإعلانات والتنبيهات الموجهة لك من ZYNORA HR.");

        var status = ServiceStatus();
        status.Text = "جاري تحميل الإشعارات...";
        ServiceRequestHost.Children.Add(status);

        if (_currentProfile is null)
        {
            status.Text = "سجل الدخول أولاً لعرض الإشعارات.";
            return;
        }

        if (!Online())
        {
            status.Text = "اتصل بالإنترنت لتحديث الإشعارات.";
            return;
        }

        try
        {
            var items = await _api.AnnouncementsAsync();
            status.Text = items.Count == 0
                ? "لا توجد إشعارات أو إعلانات موجهة لك حالياً."
                : $"آخر {items.Count} إشعار/إعلان";

            foreach (var item in items)
            {
                var metaParts = new List<string>();
                if (!item.IsRead)
                    metaParts.Add("جديد");
                if (!string.IsNullOrWhiteSpace(item.Category))
                    metaParts.Add(item.Category);
                if (!string.IsNullOrWhiteSpace(item.PublishDate))
                    metaParts.Add(item.PublishDate!);

                var cardContent = new VerticalStackLayout
                {
                    Spacing = 6
                };

                if (metaParts.Count > 0)
                {
                    cardContent.Children.Add(new Label
                    {
                        Text = string.Join(" · ", metaParts),
                        TextColor = item.IsRead
                            ? Color.FromArgb("#8FA6BA")
                            : Color.FromArgb("#19CFE0"),
                        FontSize = 10.5,
                        FontAttributes = FontAttributes.Bold
                    });
                }

                cardContent.Children.Add(new Label
                {
                    Text = string.IsNullOrWhiteSpace(item.Title)
                        ? "إشعار"
                        : item.Title,
                    TextColor = Color.FromArgb("#E6F0FA"),
                    FontSize = 15,
                    FontAttributes = FontAttributes.Bold,
                    LineBreakMode = LineBreakMode.WordWrap
                });

                if (!string.IsNullOrWhiteSpace(item.Body))
                {
                    cardContent.Children.Add(new Label
                    {
                        Text = item.Body,
                        TextColor = Color.FromArgb("#B9CADB"),
                        FontSize = 11.5,
                        LineHeight = 1.45,
                        LineBreakMode = LineBreakMode.WordWrap
                    });
                }

                ServiceRequestHost.Children.Add(new Border
                {
                    BackgroundColor = Color.FromArgb("#0D1B2A"),
                    Padding = new Thickness(13),
                    Margin = new Thickness(0, 0, 0, 5),
                    Content = cardContent
                });
            }
        }
        catch (MobileSessionExpiredException ex)
        {
            CloseServiceRequest();
            ShowLogin();
            Error(ex.Message);
        }
        catch (MobileApiException ex)
        {
            status.Text = ex.Message;
        }
        catch
        {
            status.Text = "تعذر تحميل الإشعارات حالياً.";
        }
    }

    private void BuildSettingsService()
    {
        OpenServiceRequest(
            "الإعدادات",
            "الحساب، اللغة، الأمان والخصوصية، وإدارة الجلسة.");

        var profileName = string.IsNullOrWhiteSpace(_currentProfile?.DisplayName)
            ? UiLocalization.T("غير مسجل")
            : _currentProfile!.DisplayName;
        var employeeNo = string.IsNullOrWhiteSpace(_currentProfile?.EmployeeNo)
            ? "—"
            : _currentProfile!.EmployeeNo;
        var position = string.IsNullOrWhiteSpace(_currentProfile?.Position)
            ? "—"
            : _currentProfile!.Position;

        ServiceRequestHost.Children.Add(ServiceFieldTitle("الحساب"));
        ServiceRequestHost.Children.Add(new Label
        {
            Text = $"{profileName}\n{employeeNo} · {position}",
            TextColor = Color.FromArgb("#CFE0F0"),
            FontSize = 12.5,
            LineHeight = 1.45
        });

        var currentLanguageName = UiLocalization.CurrentLanguage switch
        {
            UiLocalization.English => "English",
            UiLocalization.Kurdish => "کوردی",
            _ => "العربية"
        };
        var languageButton = ServiceSelectorButton(
            $"{UiLocalization.T("لغة التطبيق")}  ·  {currentLanguageName}");
        languageButton.Clicked += (_, _) => BuildLanguageSettingsService();
        ServiceRequestHost.Children.Add(languageButton);

        var securityButton = ServiceSelectorButton("الأمان والخصوصية");
        securityButton.Clicked += (_, _) => BuildSecurityPrivacyService();
        ServiceRequestHost.Children.Add(securityButton);

        var connectionStatus = UiLocalization.T(
            Online() ? "متصل" : "غير متصل");
        var appVersion = AppInfo.Current.VersionString;
        ServiceRequestHost.Children.Add(ServiceFieldTitle("التطبيق"));
        ServiceRequestHost.Children.Add(ServiceHint(
            $"ZYNORA HR · الإصدار {appVersion}\nحالة الاتصال: {connectionStatus}"));

        if (_currentProfile is not null)
        {
            var profileButton = ServiceSelectorButton("فتح ملفي الشخصي");
            profileButton.Clicked += async (_, _) =>
            {
                CloseServiceRequest();
                ShowProfile();
                await MainScrollView.ScrollToAsync(0, 0, true);
            };
            ServiceRequestHost.Children.Add(profileButton);

            var refreshButton = ServiceSelectorButton("تحديث البيانات الآن");
            refreshButton.Clicked += async (_, _) =>
            {
                CloseServiceRequest();
                if (!_busy)
                    await LoadAsync();
            };
            ServiceRequestHost.Children.Add(refreshButton);
        }

        var logoutButton = ServiceSelectorButton("تسجيل الخروج من ZYNORA");
        logoutButton.TextColor = Color.FromArgb("#FFB8C3");
        logoutButton.BorderColor = Color.FromArgb("#7A3344");
        logoutButton.Clicked += (_, _) =>
        {
            CloseServiceRequest();
            ShowLogoutConfirmation();
        };
        ServiceRequestHost.Children.Add(logoutButton);
    }

    private void BuildLanguageSettingsService()
    {
        OpenServiceRequest(
            "لغة التطبيق",
            "لغة واجهة ZYNORA HR على هذا الجهاز.",
            BuildSettingsService);

        var current = UiLocalization.CurrentLanguage;
        var currentName = current switch
        {
            UiLocalization.English => "English · United States",
            UiLocalization.Kurdish => "کوردی · عێراق",
            _ => "العربية · العراق"
        };

        ServiceRequestHost.Children.Add(ServiceFieldTitle("اللغة الحالية"));
        ServiceRequestHost.Children.Add(ServiceHint(currentName));

        AddLanguageOption("العربية", UiLocalization.Arabic, current);
        AddLanguageOption("English", UiLocalization.English, current);
        AddLanguageOption("کوردی", UiLocalization.Kurdish, current);

        ServiceRequestHost.Children.Add(ServiceHint(
            "تُحفظ اللغة على هذا الجهاز وتُطبّق على كامل واجهة التطبيق بعد إعادة تحميل الواجهة."));
    }

    private void AddLanguageOption(
        string label,
        string languageCode,
        string currentLanguage)
    {
        var selected = string.Equals(
            languageCode,
            currentLanguage,
            StringComparison.Ordinal);

        var button = ServiceSelectorButton(
            selected ? $"{label}  ✓" : label);
        button.IsEnabled = !selected;
        button.Clicked += (_, _) =>
        {
            UiLocalization.SetLanguage(languageCode);
            CloseServiceRequest();

            var window = Application.Current?.Windows.FirstOrDefault();
            if (window is not null)
                window.Page = new AppShell();
        };

        ServiceRequestHost.Children.Add(button);
    }

    private void BuildSecurityPrivacyService()
    {
        OpenServiceRequest(
            "الأمان والخصوصية",
            "إدارة حماية الحساب والجهاز والجلسة.",
            BuildSettingsService);

        ServiceRequestHost.Children.Add(ServiceFieldTitle("المصادقة الثنائية (2FA)"));
        ServiceRequestHost.Children.Add(ServiceHint(
            UiLocalization.T("Authenticator/TOTP عامل ثانٍ مستقل عن كلمة المرور وبدون الاعتماد على SMS. ") + " " +
            UiLocalization.T("رموز الاسترداد تستخدم مرة واحدة عند فقدان تطبيق المصادقة.")));

        var twoFactor = ServiceSelectorButton("إدارة المصادقة الثنائية");
        twoFactor.Clicked += async (_, _) => await BuildTwoFactorServiceAsync();
        ServiceRequestHost.Children.Add(twoFactor);

        ServiceRequestHost.Children.Add(ServiceFieldTitle("قفل التطبيق بالبصمة / الوجه"));
        var biometricLockEnabled =
            Preferences.Default.Get(BiometricLockKey, false);
        var biometricAvailable = DeviceBiometricAuth.IsAvailable();

        var biometricStatusText = UiLocalization.T(
            biometricLockEnabled ? "مفعّل" : "غير مفعّل");
        ServiceRequestHost.Children.Add(ServiceHint(
            biometricAvailable
                ? $"الحالة: {biometricStatusText}. عند التفعيل سيطلب ZYNORA تحققاً بيومترياً قبل فتح الجلسة المحفوظة."
                : "لا توجد بصمة/وجه مسجلة أو مدعومة على هذا الجهاز."));

        var biometricLock = ServiceSelectorButton(
            biometricLockEnabled
                ? "إلغاء قفل التطبيق بالبصمة/الوجه"
                : "تفعيل قفل التطبيق بالبصمة/الوجه");
        biometricLock.IsEnabled = biometricAvailable;
        biometricLock.Clicked += async (_, _) =>
        {
            var verified = await DeviceBiometricAuth.AuthenticateAsync(
                UiLocalization.T(
                    biometricLockEnabled
                        ? "إلغاء القفل البيومتري"
                        : "تفعيل القفل البيومتري"),
                UiLocalization.T("تحقق ببصمة الوجه أو الأصبع للمتابعة."));

            if (!verified)
            {
                await DisplayAlertAsync(
                    "الأمان والخصوصية",
                    "لم يكتمل التحقق البيومتري.",
                    "حسناً");
                return;
            }

            Preferences.Default.Set(BiometricLockKey, !biometricLockEnabled);
            BuildSecurityPrivacyService();
        };
        ServiceRequestHost.Children.Add(biometricLock);

        ServiceRequestHost.Children.Add(ServiceFieldTitle("مفتاح الحضور WebAuthn"));
        ServiceRequestHost.Children.Add(ServiceHint(
            UiLocalization.T("مفتاح WebAuthn/Passkey يستخدم بصمة أو وجه الجهاز لتأكيد الحضور. ") + " " +
            UiLocalization.T("بعد التسجيل يبقى المفتاح معلّقاً حتى يعتمد من الموارد البشرية.")));

        var webAuthn = ServiceSelectorButton("إدارة مفتاح بصمة/وجه الحضور");
        webAuthn.Clicked += async (_, _) =>
            await BuildBiometricKeyServiceAsync();
        ServiceRequestHost.Children.Add(webAuthn);

        ServiceRequestHost.Children.Add(ServiceFieldTitle("حماية الجلسة والبيانات"));
        ServiceRequestHost.Children.Add(ServiceHint(
            UiLocalization.T("جلسة الموبايل محفوظة في SecureStorage، ولقطة البيانات Offline مشفرة، ") + " " +
            UiLocalization.T("وتُمسح عند تسجيل الخروج. تغيير كلمة المرور يبطل الجلسات القديمة عبر SecurityStamp.")));

        var password = ServiceSelectorButton("تغيير كلمة المرور");
        password.Clicked += (_, _) => BuildChangePasswordService();
        ServiceRequestHost.Children.Add(password);
    }

    private async Task BuildTwoFactorServiceAsync()
    {
        OpenServiceRequest(
            "المصادقة الثنائية",
            "إعداد Authenticator/TOTP ورموز الاسترداد.",
            BuildSecurityPrivacyService);

        var statusLabel = ServiceStatus();
        statusLabel.Text = "جاري تحميل حالة المصادقة الثنائية...";
        ServiceRequestHost.Children.Add(statusLabel);

        if (!Online())
        {
            statusLabel.Text = "اتصل بالإنترنت لإدارة المصادقة الثنائية.";
            return;
        }

        try
        {
            var state = await _api.TwoFactorStatusAsync();
            if (state.Enabled)
                BuildEnabledTwoFactorUi(state, statusLabel);
            else
                BuildDisabledTwoFactorUi(state, statusLabel);
        }
        catch (MobileSessionExpiredException ex)
        {
            CloseServiceRequest();
            ShowLogin();
            Error(ex.Message);
        }
        catch (MobileApiException ex)
        {
            statusLabel.Text = ex.Message;
        }
        catch
        {
            statusLabel.Text = "تعذر تحميل حالة المصادقة الثنائية حالياً.";
        }
    }

    private void BuildDisabledTwoFactorUi(
        MobileTwoFactorStatus state,
        Label statusLabel)
    {
        statusLabel.Text = state.SetupInProgress
            ? "الحالة: غير مفعّلة · يوجد إعداد غير مكتمل."
            : "الحالة: غير مفعّلة.";

        if (state.SetupInProgress)
        {
            ServiceRequestHost.Children.Add(ServiceHint(
                "أدخل رمز Authenticator المكوّن من 6 أرقام."));
            BuildPendingTwoFactorVerificationUi(statusLabel);
            ServiceRequestHost.Children.Add(ServiceHint(
                UiLocalization.T("سيُنشأ مفتاح سري جديد. أدخله في Google Authenticator أو Microsoft Authenticator ") + " " +
                UiLocalization.T("ثم تحقق برمز من 6 أرقام.")));
        }
        else
        {
            ServiceRequestHost.Children.Add(ServiceHint(
                UiLocalization.T("سيُنشأ مفتاح سري جديد. أدخله في Google Authenticator أو Microsoft Authenticator ") + " " +
                UiLocalization.T("ثم تحقق برمز من 6 أرقام.")));
        }

        var currentPassword = ServiceEntry("كلمة المرور الحالية");
        currentPassword.IsPassword = true;
        currentPassword.ReturnType = ReturnType.Done;
        ServiceRequestHost.Children.Add(ServiceFieldTitle("كلمة المرور الحالية"));
        ServiceRequestHost.Children.Add(currentPassword);

        var begin = ServicePrimaryButton(
            state.SetupInProgress ? "بدء إعداد جديد" : "بدء إعداد Authenticator");
        begin.Clicked += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(currentPassword.Text))
            {
                statusLabel.Text = "أدخل كلمة المرور الحالية.";
                return;
            }

            begin.IsEnabled = false;
            statusLabel.Text = "جاري إنشاء إعداد المصادقة الثنائية...";
            try
            {
                var setup = await _api.BeginTwoFactorSetupAsync(currentPassword.Text);
                BuildTwoFactorEnrollmentUi(setup);
            }
            catch (MobileSessionExpiredException ex)
            {
                CloseServiceRequest();
                ShowLogin();
                Error(ex.Message);
            }
            catch (MobileApiException ex)
            {
                statusLabel.Text = ex.Message;
            }
            finally
            {
                begin.IsEnabled = true;
            }
        };
        ServiceRequestHost.Children.Add(begin);
    }

    private void BuildPendingTwoFactorVerificationUi(Label statusLabel)
    {
        var code = ServiceEntry("000000");
        code.Keyboard = Keyboard.Numeric;
        code.MaxLength = 6;
        code.FlowDirection = FlowDirection.LeftToRight;
        ServiceRequestHost.Children.Add(ServiceFieldTitle("رمز التحقق الحالي"));
        ServiceRequestHost.Children.Add(code);

        var enable = ServicePrimaryButton("تفعيل المصادقة الثنائية");
        enable.Clicked += async (_, _) =>
        {
            var value = code.Text?.Trim();
            if (string.IsNullOrWhiteSpace(value) || value.Length != 6)
            {
                statusLabel.Text = "أدخل رمز Authenticator المكوّن من 6 أرقام.";
                return;
            }

            enable.IsEnabled = false;
            statusLabel.Text = "جاري التحقق من الرمز وتفعيل 2FA...";
            try
            {
                var result = await _api.EnableTwoFactorAsync(value);
                ShowTwoFactorRecoveryCodes(
                    result.RecoveryCodes,
                    "تم تفعيل المصادقة الثنائية.");
            }
            catch (MobileSessionExpiredException ex)
            {
                CloseServiceRequest();
                ShowLogin();
                Error(ex.Message);
            }
            catch (MobileApiException ex)
            {
                statusLabel.Text = ex.Message;
            }
            finally
            {
                enable.IsEnabled = true;
            }
        };
        ServiceRequestHost.Children.Add(enable);
    }

    private void BuildTwoFactorEnrollmentUi(MobileTwoFactorSetup setup)
    {
        OpenServiceRequest(
            "إعداد Authenticator",
            "أضف المفتاح إلى تطبيق المصادقة ثم أدخل الرمز الحالي.",
            () => _ = BuildTwoFactorServiceAsync());

        ServiceRequestHost.Children.Add(ServiceFieldTitle("المفتاح السري"));
        ServiceRequestHost.Children.Add(ServiceHint(
            UiLocalization.T($"المفتاح اليدوي:\n{setup.Secret}\n\n") +
            UiLocalization.T($"الرمز يتغير كل {setup.PeriodSeconds} ثانية.")));

        var copySecret = ServiceSelectorButton("نسخ المفتاح السري");
        copySecret.Clicked += async (_, _) =>
        {
            await Clipboard.Default.SetTextAsync(setup.Secret);
            await DisplayAlertAsync(
                "المصادقة الثنائية",
                "تم نسخ المفتاح السري. لا تشاركه مع أي شخص.",
                "حسناً");
        };
        ServiceRequestHost.Children.Add(copySecret);

        var openAuthenticator = ServiceSelectorButton("فتح تطبيق Authenticator");
        openAuthenticator.Clicked += async (_, _) =>
        {
            try
            {
                await Launcher.Default.OpenAsync(new Uri(setup.OtpAuthUri));
            }
            catch
            {
                await DisplayAlertAsync(
                    "Authenticator",
                    "تعذر فتح تطبيق المصادقة تلقائياً. استخدم المفتاح اليدوي الظاهر أعلاه.",
                    "حسناً");
            }
        };
        ServiceRequestHost.Children.Add(openAuthenticator);

        var code = ServiceEntry("000000");
        code.Keyboard = Keyboard.Numeric;
        code.MaxLength = 6;
        code.FlowDirection = FlowDirection.LeftToRight;
        ServiceRequestHost.Children.Add(ServiceFieldTitle("رمز التحقق الحالي"));
        ServiceRequestHost.Children.Add(code);

        var status = ServiceStatus();
        ServiceRequestHost.Children.Add(status);

        var enable = ServicePrimaryButton("تفعيل المصادقة الثنائية");
        enable.Clicked += async (_, _) =>
        {
            var value = code.Text?.Trim();
            if (string.IsNullOrWhiteSpace(value) || value.Length != 6)
            {
                status.Text = "أدخل رمز Authenticator المكوّن من 6 أرقام.";
                return;
            }

            enable.IsEnabled = false;
            status.Text = "جاري التحقق من الرمز وتفعيل 2FA...";
            try
            {
                var result = await _api.EnableTwoFactorAsync(value);
                ShowTwoFactorRecoveryCodes(
                    result.RecoveryCodes,
                    "تم تفعيل المصادقة الثنائية.");
            }
            catch (MobileSessionExpiredException ex)
            {
                CloseServiceRequest();
                ShowLogin();
                Error(ex.Message);
            }
            catch (MobileApiException ex)
            {
                status.Text = ex.Message;
            }
            finally
            {
                enable.IsEnabled = true;
            }
        };
        ServiceRequestHost.Children.Add(enable);
    }

    private void BuildEnabledTwoFactorUi(
        MobileTwoFactorStatus state,
        Label statusLabel)
    {
        statusLabel.Text =
            $"الحالة: مفعّلة · رموز الاسترداد المتبقية: {state.RecoveryCodesRemaining}.";

        ServiceRequestHost.Children.Add(ServiceHint(
            UiLocalization.T("للتعطيل أو إنشاء رموز استرداد جديدة، أدخل كلمة المرور الحالية ") + " " +
            UiLocalization.T("ثم رمز Authenticator أو Recovery Code.")));

        var currentPassword = ServiceEntry("كلمة المرور الحالية");
        currentPassword.IsPassword = true;
        ServiceRequestHost.Children.Add(ServiceFieldTitle("كلمة المرور الحالية"));
        ServiceRequestHost.Children.Add(currentPassword);

        var factor = ServiceEntry("رمز Authenticator أو Recovery Code");
        factor.FlowDirection = FlowDirection.LeftToRight;
        factor.MaxLength = 20;
        ServiceRequestHost.Children.Add(ServiceFieldTitle("العامل الثاني"));
        ServiceRequestHost.Children.Add(factor);

        var regenerate = ServiceSelectorButton("إنشاء رموز استرداد جديدة");
        regenerate.Clicked += async (_, _) =>
        {
            if (!TryReadTwoFactorAdminInput(
                    currentPassword,
                    factor,
                    statusLabel,
                    out var password,
                    out var code,
                    out var recovery))
                return;

            regenerate.IsEnabled = false;
            statusLabel.Text = "جاري إنشاء رموز استرداد جديدة...";
            try
            {
                var result = await _api.RegenerateTwoFactorRecoveryCodesAsync(
                    password,
                    code,
                    recovery);
                ShowTwoFactorRecoveryCodes(
                    result.RecoveryCodes,
                    "تم إنشاء رموز استرداد جديدة.");
            }
            catch (MobileApiException ex)
            {
                statusLabel.Text = ex.Message;
            }
            finally
            {
                regenerate.IsEnabled = true;
            }
        };
        ServiceRequestHost.Children.Add(regenerate);

        var disable = ServiceSelectorButton("تعطيل المصادقة الثنائية");
        disable.TextColor = Color.FromArgb("#FFB8C3");
        disable.BorderColor = Color.FromArgb("#7A3344");
        disable.Clicked += async (_, _) =>
        {
            if (!TryReadTwoFactorAdminInput(
                    currentPassword,
                    factor,
                    statusLabel,
                    out var password,
                    out var code,
                    out var recovery))
                return;

            var confirmed = await DisplayAlertAsync(
                "تعطيل المصادقة الثنائية",
                "سيتم إزالة Authenticator ورموز الاسترداد من الحساب. هل تريد المتابعة؟",
                "تعطيل",
                "إلغاء");
            if (!confirmed) return;

            disable.IsEnabled = false;
            statusLabel.Text = "جاري تعطيل المصادقة الثنائية...";
            try
            {
                var result = await _api.DisableTwoFactorAsync(
                    password,
                    code,
                    recovery);
                FinishTwoFactorReauthentication(result.Message);
            }
            catch (MobileApiException ex)
            {
                statusLabel.Text = ex.Message;
            }
            finally
            {
                disable.IsEnabled = true;
            }
        };
        ServiceRequestHost.Children.Add(disable);
    }

    private static bool TryReadTwoFactorAdminInput(
        Entry currentPassword,
        Entry factor,
        Label status,
        out string password,
        out string? code,
        out string? recoveryCode)
    {
        password = currentPassword.Text?.Trim() ?? string.Empty;
        var factorValue = factor.Text?.Trim() ?? string.Empty;
        code = null;
        recoveryCode = null;

        if (string.IsNullOrWhiteSpace(password))
        {
            status.Text = "أدخل كلمة المرور الحالية.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(factorValue))
        {
            status.Text = "أدخل رمز Authenticator أو Recovery Code.";
            return false;
        }

        var digits = new string(factorValue.Where(char.IsDigit).ToArray());
        if (digits.Length == 6 && digits.Length == factorValue.Length)
            code = digits;
        else
            recoveryCode = factorValue;

        return true;
    }

    private void ShowTwoFactorRecoveryCodes(
        IReadOnlyList<string> recoveryCodes,
        string message)
    {
        OpenServiceRequest(
            "رموز الاسترداد",
            "تظهر هذه الرموز الآن فقط. احفظها في مكان آمن خارج الهاتف.",
            () => FinishTwoFactorReauthentication(message));

        ServiceRequestHost.Children.Add(ServiceHint(
            UiLocalization.T(message) + Environment.NewLine +
            UiLocalization.T("كل رمز يعمل مرة واحدة فقط.")));

        var codesText = string.Join(Environment.NewLine, recoveryCodes);
        var codes = new Editor
        {
            Text = codesText,
            IsReadOnly = true,
            AutoSize = EditorAutoSizeOption.TextChanges,
            TextColor = Color.FromArgb("#E6F0FA"),
            BackgroundColor = Color.FromArgb("#06101D"),
            FontSize = 14,
            FontFamily = "monospace"
        };
        ServiceRequestHost.Children.Add(codes);

        var copy = ServiceSelectorButton("نسخ رموز الاسترداد");
        copy.Clicked += async (_, _) =>
        {
            await Clipboard.Default.SetTextAsync(codesText);
            await DisplayAlertAsync(
                "رموز الاسترداد",
                "تم نسخ الرموز. احفظها في مكان آمن ثم امسحها من الحافظة عند الانتهاء.",
                "حسناً");
        };
        ServiceRequestHost.Children.Add(copy);

        var finish = ServicePrimaryButton("حفظت الرموز · تسجيل الدخول من جديد");
        finish.Clicked += (_, _) => FinishTwoFactorReauthentication(message);
        ServiceRequestHost.Children.Add(finish);
    }

    private void FinishTwoFactorReauthentication(string message)
    {
        _api.ClearSession();
        CloseServiceRequest();
        ShowLogin();
        Error(
            UiLocalization.T(message) + " " +
            UiLocalization.T("سجل الدخول من جديد."));
    }

    private void BuildChangePasswordService()
    {
        OpenServiceRequest(
            "تغيير كلمة المرور",
            "إدارة كلمة مرور حساب ZYNORA من داخل التطبيق.",
            BuildSecurityPrivacyService);

        ServiceRequestHost.Children.Add(ServiceHint(
            UiLocalization.T("لن يتم فتح متصفح خارجي. نموذج التغيير Native، ") + " " +
            UiLocalization.T("وسيتم تفعيل الحفظ بعد ربط خدمة تغيير كلمة المرور الآمنة بالموبايل.")));

        var currentValue = ServiceEntry("كلمة المرور الحالية");
        currentValue.IsPassword = true;
        currentValue.ReturnType = ReturnType.Next;

        var newValue = ServiceEntry("كلمة المرور الجديدة");
        newValue.IsPassword = true;
        newValue.ReturnType = ReturnType.Next;

        var confirmation = ServiceEntry("تأكيد كلمة المرور الجديدة");
        confirmation.IsPassword = true;
        confirmation.ReturnType = ReturnType.Done;

        ServiceRequestHost.Children.Add(ServiceFieldTitle("كلمة المرور الحالية"));
        ServiceRequestHost.Children.Add(currentValue);
        ServiceRequestHost.Children.Add(ServiceFieldTitle("كلمة المرور الجديدة"));
        ServiceRequestHost.Children.Add(newValue);
        ServiceRequestHost.Children.Add(ServiceFieldTitle("تأكيد كلمة المرور"));
        ServiceRequestHost.Children.Add(confirmation);

        var save = ServicePrimaryButton("حفظ كلمة المرور الجديدة");
        save.IsEnabled = false;
        ServiceRequestHost.Children.Add(save);
        var status = ServiceStatus();
        status.Text = "الحفظ غير مفعّل في هذا الـBuild حتى يكتمل الربط الآمن مع خدمة الحساب.";
        ServiceRequestHost.Children.Add(status);
    }

    private async Task BuildBiometricKeyServiceAsync()
    {
        OpenServiceRequest(
            "مفتاح بصمة/وجه الحضور",
            "إدارة حالة مفاتيح WebAuthn الخاصة بحسابك من داخل ZYNORA.",
            BuildSecurityPrivacyService);

        var status = ServiceStatus();
        status.Text = "جاري تحميل حالة المفاتيح...";
        ServiceRequestHost.Children.Add(status);

        var deviceLabel = new Entry
        {
            Text = $"{DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}".Trim(),
            Placeholder = "اسم الجهاز",
            TextColor = Color.FromArgb("#E6F0FA"),
            PlaceholderColor = Color.FromArgb("#70879B"),
            BackgroundColor = Color.FromArgb("#06101D"),
            HeightRequest = 48
        };
        ServiceRequestHost.Children.Add(ServiceFieldTitle("اسم الجهاز"));
        ServiceRequestHost.Children.Add(deviceLabel);

        var register = ServicePrimaryButton("تسجيل مفتاح بصمة/وجه جديد");
        register.Clicked += async (_, _) =>
        {
            if (!Online())
            {
                status.Text = "اتصل بالإنترنت لتسجيل المفتاح.";
                return;
            }

            register.IsEnabled = false;
            status.Text = "جاري تجهيز طلب التسجيل الآمن...";
            try
            {
                var begin = await _api.BeginBiometricRegistrationAsync();
                if (string.IsNullOrWhiteSpace(begin.Key) ||
                    begin.Options.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                    throw new MobileApiException("استجابة تسجيل المفتاح غير صالحة.");

                status.Text = "استخدم بصمة الوجه أو الأصبع لإكمال إنشاء المفتاح...";
                var registrationJson = await AndroidPasskeyRegistration.CreateAsync(
                    begin.Options.GetRawText());

                status.Text = "جاري التحقق من المفتاح وحفظه...";
                var result = await _api.CompleteBiometricRegistrationAsync(
                    begin.Key,
                    registrationJson,
                    deviceLabel.Text);

                await BuildBiometricKeyServiceAsync();
                await DisplayAlertAsync(
                    "مفتاح بصمة/وجه الحضور",
                    result.Message,
                    "حسناً");
            }
            catch (MobileSessionExpiredException ex)
            {
                CloseServiceRequest();
                ShowLogin();
                Error(ex.Message);
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
            }
            finally
            {
                register.IsEnabled = true;
            }
        };
        ServiceRequestHost.Children.Add(register);

        var refresh = ServiceSelectorButton("تحديث الحالة");
        refresh.Clicked += async (_, _) => await BuildBiometricKeyServiceAsync();
        ServiceRequestHost.Children.Add(refresh);

        if (!Online())
        {
            status.Text = "اتصل بالإنترنت لعرض حالة مفاتيح الحضور.";
            return;
        }

        try
        {
            var items = await _api.BiometricKeysAsync();
            status.Text = items.Count == 0
                ? "لا يوجد مفتاح بصمة/وجه مسجل لهذا الحساب."
                : $"عدد المفاتيح المسجلة: {items.Count}";

            foreach (var item in items)
            {
                var label = string.IsNullOrWhiteSpace(item.DeviceLabel)
                    ? "مفتاح هذا الجهاز"
                    : item.DeviceLabel;

                var details = new List<string>
                {
                    UiLocalization.T($"الحالة: {item.DisplayStatusText}"),
                    $"{UiLocalization.T("تاريخ التسجيل")}: {item.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}"
                };

                if (item.ApprovedAt is { } approved)
                    details.Add($"{UiLocalization.T("تاريخ الاعتماد")}: {approved.ToLocalTime():yyyy-MM-dd HH:mm}");
                if (item.LastUsedAt is { } lastUsed)
                    details.Add($"{UiLocalization.T("آخر استخدام")}: {lastUsed.ToLocalTime():yyyy-MM-dd HH:mm}");

                var content = new VerticalStackLayout { Spacing = 6 };
                content.Children.Add(new Label
                {
                    Text = label,
                    TextColor = Color.FromArgb("#E6F0FA"),
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold
                });
                content.Children.Add(new Label
                {
                    Text = string.Join("\n", details),
                    TextColor = Color.FromArgb("#AFC2D4"),
                    FontSize = 11.5,
                    LineHeight = 1.45
                });

                ServiceRequestHost.Children.Add(new Border
                {
                    BackgroundColor = Color.FromArgb("#0D1B2A"),
                    Stroke = Color.FromArgb("#294158"),
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                    {
                        CornerRadius = new CornerRadius(14)
                    },
                    Padding = new Thickness(13),
                    Content = content
                });
            }

            ServiceRequestHost.Children.Add(ServiceHint(
                UiLocalization.T("تسجيل مفتاح جديد أو استبداله يحتاج عملية WebAuthn آمنة؛ ") + " " +
                UiLocalization.T("هذه الشاشة تعرض الحالة داخل التطبيق ولا تفتح المتصفح تلقائياً.")));
        }
        catch (MobileSessionExpiredException ex)
        {
            CloseServiceRequest();
            ShowLogin();
            Error(ex.Message);
        }
        catch (MobileApiException ex)
        {
            status.Text = ex.Message;
        }
        catch
        {
            status.Text = "تعذر تحميل حالة مفاتيح الحضور حالياً.";
        }
    }

    private async Task OpenEmployeeSecurityPageAsync(string relativePath, string title)
    {
        if (!Online())
        {
            await DisplayAlertAsync(
                title,
                "هذه العملية تحتاج اتصالاً بالإنترنت.",
                "حسناً");
            return;
        }

        try
        {
            var url = new Uri(new Uri(AppSettings.BaseUrl), relativePath);
            await Launcher.Default.OpenAsync(url);
        }
        catch
        {
            await DisplayAlertAsync(
                title,
                "تعذر فتح صفحة الأمان حالياً.",
                "حسناً");
        }
    }

    private async Task BuildMissingPunchServiceAsync()
    {
        OpenServiceRequest(
            "طلب نسيان بصمة",
            "حدد التاريخ والوقت؛ ZYNORA يعيد ترتيب بصمات اليوم ويحدد دخول/خروج تلقائياً.");

        var status = ServiceStatus();
        var datePicker = new DatePicker
        {
            Date = DateTime.Today,
            Format = "yyyy-MM-dd",
            BackgroundColor = Color.FromArgb("#06101D"),
            TextColor = Color.FromArgb("#E6F0FA"),
            HeightRequest = 48
        };
        var timePicker = new TimePicker
        {
            Time = DateTime.Now.TimeOfDay,
            Format = "HH:mm",
            BackgroundColor = Color.FromArgb("#06101D"),
            TextColor = Color.FromArgb("#E6F0FA"),
            HeightRequest = 48
        };
        var preview = new Label
        {
            TextColor = Color.FromArgb("#CFE0F0"),
            FontSize = 11.5,
            LineBreakMode = LineBreakMode.WordWrap,
            BackgroundColor = Color.FromArgb("#10202E"),
            Padding = new Thickness(12)
        };
        var reason = ServiceEditor("سبب غياب البصمة (نسيان، عطل جهاز...)");
        var submit = ServicePrimaryButton("إرسال طلب البصمة");
        var previewValid = false;
        submit.IsEnabled = false;

        ServiceRequestHost.Children.Add(ServiceFieldTitle("تاريخ البصمة *"));
        ServiceRequestHost.Children.Add(datePicker);
        ServiceRequestHost.Children.Add(ServiceFieldTitle("وقت البصمة المفقودة *"));
        ServiceRequestHost.Children.Add(timePicker);
        ServiceRequestHost.Children.Add(ServiceFieldTitle("بصمات هذا اليوم بعد الإضافة"));
        ServiceRequestHost.Children.Add(preview);
        ServiceRequestHost.Children.Add(ServiceFieldTitle("السبب"));
        ServiceRequestHost.Children.Add(reason);
        ServiceRequestHost.Children.Add(submit);
        ServiceRequestHost.Children.Add(status);

        async Task RefreshPreviewAsync()
        {
            if (!Online())
            {
                previewValid = false;
                submit.IsEnabled = false;
                preview.Text = "اتصل بالإنترنت لعرض بصمات اليوم.";
                return;
            }

            try
            {
                var selectedDate = datePicker.Date ?? DateTime.Today;
                var proposed = timePicker.Time ?? DateTime.Now.TimeOfDay;
                var punches = await _api.DayPunchesAsync(selectedDate);

                var points = new List<(TimeSpan Time, bool Proposed)>();
                foreach (var punch in punches)
                {
                    if (TimeSpan.TryParse(punch.At, out var parsed))
                        points.Add((parsed, false));
                }

                points.Add((proposed, true));
                points = points.OrderBy(point => point.Time).ToList();

                var lines = new List<string>();
                var total = TimeSpan.Zero;
                for (var i = 0; i < points.Count; i++)
                {
                    var type = UiLocalization.T(i % 2 == 0 ? "دخول" : "خروج");
                    var marker = points[i].Proposed
                        ? UiLocalization.T("  ← البصمة المقترحة")
                        : "";
                    lines.Add($"{i + 1}. {points[i].Time.ToString(@"hh\:mm")} · {type}{marker}");

                    if (i % 2 == 1)
                        total += points[i].Time - points[i - 1].Time;
                }

                previewValid = points.Count % 2 == 0;
                submit.IsEnabled = previewValid;

                var completeness = previewValid
                    ? UiLocalization.T("تسلسل البصمات مكتمل بعد الإضافة.")
                    : UiLocalization.T("يبقى تسلسل البصمات غير مكتمل بعد الإضافة.");

                preview.Text =
                    string.Join(Environment.NewLine, lines) +
                    Environment.NewLine +
                    UiLocalization.T(
                        $"ساعات العمل الناتجة: {(int)total.TotalHours:00}:{total.Minutes:00}") +
                    Environment.NewLine +
                    completeness;
            }
            catch (MobileSessionExpiredException ex)
            {
                CloseServiceRequest();
                ShowLogin();
                Error(ex.Message);
            }
            catch (MobileApiException ex)
            {
                previewValid = false;
                submit.IsEnabled = false;
                preview.Text = ex.Message;
            }
            catch
            {
                previewValid = false;
                submit.IsEnabled = false;
                preview.Text = "تعذر تحميل بصمات اليوم.";
            }
        }

        datePicker.DateSelected += async (_, _) => await RefreshPreviewAsync();
        timePicker.PropertyChanged += async (_, args) =>
        {
            if (args.PropertyName == TimePicker.TimeProperty.PropertyName)
                await RefreshPreviewAsync();
        };

        submit.Clicked += async (_, _) =>
        {
            if (!previewValid)
            {
                status.Text = "أكمل تسلسل الدخول/الخروج قبل إرسال الطلب.";
                return;
            }

            if (!Online())
            {
                status.Text = "لا يمكن إرسال الطلب بدون إنترنت.";
                return;
            }

            submit.IsEnabled = false;
            status.Text = "جاري إرسال الطلب...";
            try
            {
                var result = await _api.SubmitMissingPunchAsync(
                    datePicker.Date ?? DateTime.Today,
                    timePicker.Time ?? DateTime.Now.TimeOfDay,
                    reason.Text);
                status.Text = result.Message;
                reason.Text = "";
                await RefreshPreviewAsync();
            }
            catch (MobileSessionExpiredException ex)
            {
                CloseServiceRequest();
                ShowLogin();
                Error(ex.Message);
            }
            catch (MobileApiException ex)
            {
                status.Text = ex.Message;
            }
            catch
            {
                status.Text = "تعذر إرسال طلب البصمة حالياً.";
            }
            finally
            {
                submit.IsEnabled = previewValid;
            }
        };

        await RefreshPreviewAsync();
    }

    private async Task BuildDataChangeServiceAsync()
    {
        OpenServiceRequest(
            "طلب تعديل بياناتي",
            "اطلب تعديل بياناتك الشخصية أو صورتك؛ لا يُطبّق أي تغيير إلا بعد الاعتماد.");

        var status = ServiceStatus();
        status.Text = "جاري تحميل الحقول المتاحة...";
        ServiceRequestHost.Children.Add(status);

        if (!Online())
        {
            status.Text = "اتصل بالإنترنت لفتح نموذج تعديل البيانات.";
            return;
        }

        List<DataChangeField> fields;
        try
        {
            fields = await _api.DataChangeFieldsAsync();
        }
        catch (MobileSessionExpiredException ex)
        {
            CloseServiceRequest();
            ShowLogin();
            Error(ex.Message);
            return;
        }
        catch (MobileApiException ex)
        {
            status.Text = ex.Message;
            return;
        }
        catch
        {
            status.Text = "تعذر تحميل الحقول القابلة للتعديل.";
            return;
        }

        ServiceRequestHost.Children.Clear();
        status = ServiceStatus();

        if (fields.Count == 0)
        {
            status.Text = "لا توجد حقول متاحة للتعديل حالياً.";
            ServiceRequestHost.Children.Add(status);
            return;
        }

        var textEntries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        var selectedValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        FileResult? selectedPhoto = null;
        Button? submit = null;

        bool HasProposedDataChange() =>
            selectedPhoto is not null ||
            textEntries.Values.Any(entry => !string.IsNullOrWhiteSpace(entry.Text)) ||
            selectedValues.Values.Any(value => !string.IsNullOrWhiteSpace(value));

        void UpdateDataChangeSubmitState()
        {
            if (submit is not null)
                submit.IsEnabled = HasProposedDataChange();
        }

        var photoButton = ServiceSelectorButton("تغيير الصورة الشخصية");
        var photoName = ServiceHint("JPG / PNG / WEBP · حتى 5MB · لا تغيير");
        photoButton.Clicked += async (_, _) =>
        {
            try
            {
                var picked = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "اختر الصورة الشخصية",
                    FileTypes = FilePickerFileType.Images
                });

                if (picked is null)
                    return;

                selectedPhoto = picked;
                photoName.Text = picked.FileName;
                UpdateDataChangeSubmitState();
            }
            catch
            {
                status.Text = "تعذر فتح منتقي الصور.";
            }
        };

        ServiceRequestHost.Children.Add(ServiceFieldTitle("الصورة الشخصية"));
        ServiceRequestHost.Children.Add(photoButton);
        ServiceRequestHost.Children.Add(photoName);
        ServiceRequestHost.Children.Add(ServiceHint("اترك أي حقل فارغاً إذا لم ترغب بتعديله."));

        foreach (var field in fields.Where(field =>
                     !string.Equals(field.Kind, "photo", StringComparison.OrdinalIgnoreCase)))
        {
            var currentDisplay = field.CurrentValue;
            if (string.IsNullOrWhiteSpace(currentDisplay) &&
                field.Key.Contains("email", StringComparison.OrdinalIgnoreCase))
            {
                currentDisplay = _currentProfile?.PreferredEmail;
            }

            if (string.Equals(field.Kind, "select", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(field.CurrentValue))
            {
                currentDisplay = field.Options.FirstOrDefault(option =>
                    string.Equals(
                        option.Value,
                        field.CurrentValue,
                        StringComparison.OrdinalIgnoreCase))?.DisplayLabel
                    ?? UiLocalization.Data(field.CurrentValue);
            }

            var currentDisplayText =
                string.IsNullOrWhiteSpace(currentDisplay) ? "—" : currentDisplay;
            var currentLabel = UiLocalization.T("الحالي");
            ServiceRequestHost.Children.Add(
                ServiceFieldTitle(
                    $"{field.DisplayLabel}  ({currentLabel}: {currentDisplayText})"));

            if (string.Equals(field.Kind, "select", StringComparison.OrdinalIgnoreCase))
            {
                selectedValues[field.Key] = null;
                var trigger = ServiceSelectorButton("— لا تغيير —");
                var optionsPanel = ServiceOptionsPanel();

                var noChange = ServiceSelectorButton("— لا تغيير —");
                noChange.FontSize = 12;
                noChange.HeightRequest = 42;
                noChange.Clicked += (_, _) =>
                {
                    selectedValues[field.Key] = null;
                    trigger.Text = UiLocalization.T("— لا تغيير —");
                    optionsPanel.IsVisible = false;
                    UpdateDataChangeSubmitState();
                };
                optionsPanel.Children.Add(noChange);

                foreach (var optionItem in field.Options)
                {
                    var item = optionItem;
                    var optionButton = ServiceSelectorButton(item.DisplayLabel);
                    optionButton.FontSize = 12;
                    optionButton.HeightRequest = 42;
                    optionButton.Clicked += (_, _) =>
                    {
                        selectedValues[field.Key] = item.Value;
                        trigger.Text = item.DisplayLabel;
                        optionsPanel.IsVisible = false;
                        UpdateDataChangeSubmitState();
                    };
                    optionsPanel.Children.Add(optionButton);
                }

                trigger.Clicked += (_, _) =>
                    optionsPanel.IsVisible = !optionsPanel.IsVisible;

                ServiceRequestHost.Children.Add(trigger);
                ServiceRequestHost.Children.Add(optionsPanel);
                continue;
            }

            if (string.Equals(field.Kind, "date", StringComparison.OrdinalIgnoreCase))
            {
                selectedValues[field.Key] = null;
                var trigger = ServiceSelectorButton("اختر التاريخ الجديد");
                var datePicker = new DatePicker
                {
                    Date = DateTime.Today,
                    Format = "'— لا تغيير —'",
                    BackgroundColor = Color.FromArgb("#06101D"),
                    TextColor = Color.FromArgb("#E6F0FA"),
                    HeightRequest = 48
                };
                datePicker.DateSelected += (_, _) =>
                {
                    var selected = datePicker.Date ?? DateTime.Today;
                    selectedValues[field.Key] = selected.ToString("yyyy-MM-dd");
                    trigger.Text = selected.ToString("yyyy-MM-dd");
                    datePicker.Format = "yyyy-MM-dd";
                    UpdateDataChangeSubmitState();
                };

                ServiceRequestHost.Children.Add(trigger);
                ServiceRequestHost.Children.Add(datePicker);
                trigger.Clicked += (_, _) => datePicker.Focus();
                continue;
            }

            var keyboard =
                string.Equals(field.Kind, "email", StringComparison.OrdinalIgnoreCase)
                    ? Keyboard.Email
                    : string.Equals(field.Kind, "tel", StringComparison.OrdinalIgnoreCase)
                        ? Keyboard.Telephone
                        : Keyboard.Default;

            var entry = ServiceEntry("القيمة الجديدة", keyboard);
            textEntries[field.Key] = entry;
            entry.TextChanged += (_, _) => UpdateDataChangeSubmitState();
            ServiceRequestHost.Children.Add(entry);
        }

        var reason = ServiceEditor("سبب التعديل (اختياري)");
        submit = ServicePrimaryButton("إرسال طلب التعديل");
        submit.IsEnabled = HasProposedDataChange();

        ServiceRequestHost.Children.Add(ServiceFieldTitle("سبب التعديل"));
        ServiceRequestHost.Children.Add(reason);
        ServiceRequestHost.Children.Add(submit);
        ServiceRequestHost.Children.Add(status);

        submit.Clicked += async (_, _) =>
        {
            var proposed = new List<DataChangeSubmissionField>();

            foreach (var (key, entry) in textEntries)
            {
                var value = entry.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    proposed.Add(new DataChangeSubmissionField
                    {
                        Key = key,
                        NewValue = value
                    });
            }

            foreach (var (key, value) in selectedValues)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    proposed.Add(new DataChangeSubmissionField
                    {
                        Key = key,
                        NewValue = value
                    });
            }

            if (proposed.Count == 0 && selectedPhoto is null)
            {
                status.Text = "أدخل قيمة جديدة أو اختر صورة لتعديلها.";
                return;
            }

            submit.IsEnabled = false;
            status.Text = "جاري إرسال طلب التعديل...";
            try
            {
                var result = await _api.SubmitDataChangeMultipartAsync(
                    proposed,
                    reason.Text,
                    selectedPhoto);
                status.Text = result.Message;

                foreach (var entry in textEntries.Values)
                    entry.Text = "";
                foreach (var key in selectedValues.Keys.ToList())
                    selectedValues[key] = null;

                selectedPhoto = null;
                photoName.Text = "JPG / PNG / WEBP · حتى 5MB · لا تغيير";
                reason.Text = "";
            }
            catch (MobileSessionExpiredException ex)
            {
                CloseServiceRequest();
                ShowLogin();
                Error(ex.Message);
            }
            catch (MobileApiException ex)
            {
                status.Text = ex.Message;
            }
            catch
            {
                status.Text = "تعذر إرسال طلب تعديل البيانات حالياً.";
            }
            finally
            {
                submit.IsEnabled = true;
            }
        };
    }

    private async Task BuildShiftServiceAsync()
    {
        OpenServiceRequest(
            "طلب مناوبة",
            "اختر مناوبة متاحة ومدى الأيام؛ يطبّق التغيير بعد اعتماد الطلب.");

        var status = ServiceStatus();
        status.Text = "جاري تحميل المناوبات المتاحة...";
        ServiceRequestHost.Children.Add(status);

        if (!Online())
        {
            status.Text = "اتصل بالإنترنت لفتح طلب المناوبة.";
            return;
        }

        ShiftCatalogResponse catalog;
        try
        {
            catalog = await _api.ShiftCatalogAsync();
        }
        catch (MobileSessionExpiredException ex)
        {
            CloseServiceRequest();
            ShowLogin();
            Error(ex.Message);
            return;
        }
        catch (MobileApiException ex)
        {
            status.Text = ex.Message;
            return;
        }
        catch
        {
            status.Text = "تعذر تحميل المناوبات المتاحة.";
            return;
        }

        ServiceRequestHost.Children.Clear();
        status = ServiceStatus();

        if (!catalog.Eligible)
        {
            status.Text = catalog.Message ?? "طلب المناوبة غير متاح لهذا الحساب.";
            ServiceRequestHost.Children.Add(status);
            return;
        }

        if (catalog.Items.Count == 0)
        {
            status.Text = "لا توجد مناوبات متاحة للطلب حالياً.";
            ServiceRequestHost.Children.Add(status);
            return;
        }

        var selectedShift = catalog.Items[0];
        var shiftTrigger = ServiceSelectorButton(selectedShift.DisplayName);
        var shiftOptions = ServiceOptionsPanel();

        foreach (var shiftItem in catalog.Items)
        {
            var item = shiftItem;
            var option = ServiceSelectorButton(item.DisplayName);
            option.FontSize = 12;
            option.HeightRequest = 44;
            option.Clicked += (_, _) =>
            {
                selectedShift = item;
                shiftTrigger.Text = item.DisplayName;
                shiftOptions.IsVisible = false;
            };
            shiftOptions.Children.Add(option);
        }

        shiftTrigger.Clicked += (_, _) =>
            shiftOptions.IsVisible = !shiftOptions.IsVisible;

        var fromDate = new DatePicker
        {
            Date = DateTime.Today,
            Format = "yyyy-MM-dd",
            BackgroundColor = Color.FromArgb("#06101D"),
            TextColor = Color.FromArgb("#E6F0FA"),
            HeightRequest = 48
        };
        var toDate = new DatePicker
        {
            Date = DateTime.Today,
            Format = "yyyy-MM-dd",
            BackgroundColor = Color.FromArgb("#06101D"),
            TextColor = Color.FromArgb("#E6F0FA"),
            HeightRequest = 48
        };
        fromDate.DateSelected += (_, _) =>
        {
            var from = fromDate.Date ?? DateTime.Today;
            var to = toDate.Date ?? from;
            if (to < from)
                toDate.Date = from;
        };

        var reason = ServiceEditor("السبب (اختياري)");
        var submit = ServicePrimaryButton("إرسال الطلب");

        ServiceRequestHost.Children.Add(ServiceFieldTitle("المناوبة المطلوبة"));
        ServiceRequestHost.Children.Add(shiftTrigger);
        ServiceRequestHost.Children.Add(shiftOptions);
        ServiceRequestHost.Children.Add(ServiceFieldTitle("من تاريخ"));
        ServiceRequestHost.Children.Add(fromDate);
        ServiceRequestHost.Children.Add(ServiceFieldTitle("إلى تاريخ"));
        ServiceRequestHost.Children.Add(toDate);
        ServiceRequestHost.Children.Add(ServiceFieldTitle("السبب"));
        ServiceRequestHost.Children.Add(reason);
        ServiceRequestHost.Children.Add(submit);
        ServiceRequestHost.Children.Add(status);

        submit.Clicked += async (_, _) =>
        {
            var from = fromDate.Date ?? DateTime.Today;
            var to = toDate.Date ?? from;
            if (to < from)
            {
                status.Text = "تاريخ النهاية لا يمكن أن يسبق تاريخ البداية.";
                return;
            }

            submit.IsEnabled = false;
            status.Text = "جاري إرسال طلب المناوبة...";
            try
            {
                var result = await _api.SubmitShiftAsync(
                    selectedShift.Id,
                    from,
                    to,
                    reason.Text);
                status.Text = result.Message;
                reason.Text = "";
            }
            catch (MobileSessionExpiredException ex)
            {
                CloseServiceRequest();
                ShowLogin();
                Error(ex.Message);
            }
            catch (MobileApiException ex)
            {
                status.Text = ex.Message;
            }
            catch
            {
                status.Text = "تعذر إرسال طلب المناوبة حالياً.";
            }
            finally
            {
                submit.IsEnabled = true;
            }
        };
    }

    private async Task BuildFinancialServiceAsync()
    {
        OpenServiceRequest(
            "طلب مالي",
            "قرض · سُلفة · بدل · استرداد — يُفعّل الأثر المالي فقط بعد اكتمال الموافقات.");

        var status = ServiceStatus();
        status.Text = "جاري تحميل أنواع الطلبات المالية...";
        ServiceRequestHost.Children.Add(status);

        if (!Online())
        {
            status.Text = "اتصل بالإنترنت لفتح الطلب المالي.";
            return;
        }

        FinancialCatalogResponse catalog;
        try
        {
            catalog = await _api.FinancialCatalogAsync();
        }
        catch (MobileSessionExpiredException ex)
        {
            CloseServiceRequest();
            ShowLogin();
            Error(ex.Message);
            return;
        }
        catch (MobileApiException ex)
        {
            status.Text = ex.Message;
            return;
        }
        catch (Exception ex)
        {
            status.Text = $"تعذر تحميل أنواع الطلبات المالية. {ex.GetType().Name}: {ex.Message}";
            return;
        }

        ServiceRequestHost.Children.Clear();
        status = ServiceStatus();

        if (!catalog.Eligible)
        {
            status.Text = catalog.Message ?? "الطلب المالي غير متاح لهذا الحساب.";
            ServiceRequestHost.Children.Add(status);
            return;
        }

        if (catalog.Items.Count == 0)
        {
            status.Text = "لا توجد أنواع طلبات مالية متاحة.";
            ServiceRequestHost.Children.Add(status);
            return;
        }

        var selectedKind = catalog.Items[0];
        var kindTrigger = ServiceSelectorButton(selectedKind.DisplayLabel);
        var kindOptions = ServiceOptionsPanel();

        var amount = ServiceEntry("المبلغ", Keyboard.Numeric);
        var installments = ServiceEntry("1", Keyboard.Numeric);
        installments.Text = "1";
        var installmentsBlock = new VerticalStackLayout { Spacing = 6 };
        installmentsBlock.Children.Add(ServiceFieldTitle("الأقساط"));
        installmentsBlock.Children.Add(installments);

        var selectedMonth = DateTime.Today.Month;
        var monthTrigger = ServiceSelectorButton(selectedMonth.ToString("00"));
        var monthOptions = new FlexLayout
        {
            IsVisible = false,
            Direction = FlexDirection.Row,
            Wrap = FlexWrap.Wrap,
            JustifyContent = FlexJustify.SpaceBetween,
            AlignItems = FlexAlignItems.Center,
            BackgroundColor = Color.FromArgb("#0B2033"),
            Padding = new Thickness(6)
        };

        var year = ServiceEntry("السنة", Keyboard.Numeric);
        year.Text = DateTime.Today.Year.ToString(CultureInfo.InvariantCulture);
        var preview = ServiceHint("");
        var reason = ServiceEntry("سبب الطلب (اختياري)");
        var submit = ServicePrimaryButton("إرسال للموافقة");
        submit.IsEnabled = false;

        void UpdateFinancialUi()
        {
            var installmentBased =
                selectedKind.Key.Equals("Loan", StringComparison.OrdinalIgnoreCase) ||
                selectedKind.Key.Equals("Advance", StringComparison.OrdinalIgnoreCase);
            installmentsBlock.IsVisible = installmentBased;

            if (installmentBased &&
                TryParseDecimal(amount.Text, out var parsedAmount) &&
                int.TryParse(installments.Text, out var count) &&
                count > 0)
            {
                var monthly = Math.Round(parsedAmount / count, 2);
                preview.Text = $"القسط الشهري التقريبي: {monthly:N2} × {count} قسط";
            }
            else
            {
                preview.Text = "";
            }

            var validAmount =
                TryParseDecimal(amount.Text, out var amountValue) &&
                amountValue > 0;
            var validInstallments =
                !installmentBased ||
                (int.TryParse(installments.Text, out var installmentCount) &&
                 installmentCount > 0);
            var validYear =
                int.TryParse(year.Text, out var yearValue) &&
                yearValue >= DateTime.Today.Year - 1 &&
                yearValue <= DateTime.Today.Year + 10;

            submit.IsEnabled =
                Online() && validAmount && validInstallments && validYear;
        }

        foreach (var item in catalog.Items)
        {
            var option = ServiceSelectorButton(item.DisplayLabel);
            option.FontSize = 12;
            option.HeightRequest = 44;
            option.Clicked += (_, _) =>
            {
                selectedKind = item;
                kindTrigger.Text = item.DisplayLabel;
                kindOptions.IsVisible = false;
                UpdateFinancialUi();
            };
            kindOptions.Children.Add(option);
        }

        for (var month = 1; month <= 12; month++)
        {
            var m = month;
            var option = new Button
            {
                Text = m.ToString("00"),
                BackgroundColor = Color.FromArgb("#10263A"),
                TextColor = Color.FromArgb("#CFE0F0"),
                CornerRadius = 10,
                FontSize = 11,
                WidthRequest = 74,
                HeightRequest = 42,
                Margin = new Thickness(2)
            };
            option.Clicked += (_, _) =>
            {
                selectedMonth = m;
                monthTrigger.Text = m.ToString("00");
                monthOptions.IsVisible = false;
            };
            monthOptions.Children.Add(option);
        }

        kindTrigger.Clicked += (_, _) =>
        {
            monthOptions.IsVisible = false;
            kindOptions.IsVisible = !kindOptions.IsVisible;
        };
        monthTrigger.Clicked += (_, _) =>
        {
            kindOptions.IsVisible = false;
            monthOptions.IsVisible = !monthOptions.IsVisible;
        };
        amount.TextChanged += (_, _) => UpdateFinancialUi();
        installments.TextChanged += (_, _) => UpdateFinancialUi();
        year.TextChanged += (_, _) => UpdateFinancialUi();

        ServiceRequestHost.Children.Add(ServiceFieldTitle("نوع الطلب"));
        ServiceRequestHost.Children.Add(kindTrigger);
        ServiceRequestHost.Children.Add(kindOptions);
        ServiceRequestHost.Children.Add(ServiceFieldTitle("المبلغ"));
        ServiceRequestHost.Children.Add(amount);
        ServiceRequestHost.Children.Add(installmentsBlock);
        ServiceRequestHost.Children.Add(ServiceFieldTitle("شهر البدء"));
        ServiceRequestHost.Children.Add(monthTrigger);
        ServiceRequestHost.Children.Add(monthOptions);
        ServiceRequestHost.Children.Add(ServiceFieldTitle("السنة"));
        ServiceRequestHost.Children.Add(year);
        ServiceRequestHost.Children.Add(preview);
        ServiceRequestHost.Children.Add(ServiceFieldTitle("السبب"));
        ServiceRequestHost.Children.Add(reason);
        ServiceRequestHost.Children.Add(submit);
        ServiceRequestHost.Children.Add(status);
        UpdateFinancialUi();

        submit.Clicked += async (_, _) =>
        {
            if (!TryParseDecimal(amount.Text, out var parsedAmount) || parsedAmount <= 0)
            {
                status.Text = "أدخل مبلغاً صحيحاً أكبر من صفر.";
                return;
            }

            var count = 1;
            var installmentBased =
                selectedKind.Key.Equals("Loan", StringComparison.OrdinalIgnoreCase) ||
                selectedKind.Key.Equals("Advance", StringComparison.OrdinalIgnoreCase);
            if (installmentBased &&
                (!int.TryParse(installments.Text, out count) || count < 1))
            {
                status.Text = "عدد الأقساط يجب أن يكون 1 أو أكثر.";
                return;
            }

            if (!int.TryParse(year.Text, out var selectedYear))
            {
                status.Text = "أدخل سنة صحيحة.";
                return;
            }

            submit.IsEnabled = false;
            status.Text = "جاري إرسال الطلب المالي...";
            try
            {
                var result = await _api.SubmitFinancialAsync(
                    selectedKind.Key,
                    parsedAmount,
                    count,
                    selectedYear,
                    selectedMonth,
                    reason.Text);
                status.Text = result.Message;
            }
            catch (MobileSessionExpiredException ex)
            {
                CloseServiceRequest();
                ShowLogin();
                Error(ex.Message);
            }
            catch (MobileApiException ex)
            {
                status.Text = ex.Message;
            }
            catch
            {
                status.Text = "تعذر إرسال الطلب المالي حالياً.";
            }
            finally
            {
                submit.IsEnabled = true;
            }
        };
    }

    private static bool TryParseDecimal(string? value, out decimal result) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result) ||
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);

    private async void OnOpenOvertimeFromRequestSheet(object? sender, EventArgs e)
    {
        RequestSheetOverlay.IsVisible = false;

        if (_overtimeRequestType is null && _requestCatalog.Count == 0 && Online())
            await EnsureRequestCatalogForSheetAsync();

        if (_overtimeRequestType is null)
        {
            await DisplayAlertAsync(
                "عمل إضافي",
                "لا يوجد نوع طلب عمل إضافي متاح لهذا الحساب حالياً.",
                "حسناً");
            return;
        }

        RequestTypePicker.SelectedItem = _overtimeRequestType;
        _requestCategory = _overtimeRequestType.Category ?? _requestCategory;
        RequestTypeDropdownPanel.IsVisible = false;
        ModalDateRangePanel.IsVisible = false;
        ModalRequestStatusLabel.Text = "";
        UpdateRequestTypeFields();
        SyncModalRequestTypeFields();
        RequestTypeOverlay.IsVisible = true;
    }

    private async void OnOpenRequestTypeChooser(object? sender, EventArgs e)
    {
        if (_requestCatalog.Count == 0 && Online() && !_busy)
            await LoadRequestsAsync();

        OpenRequestTypeChooser();
    }

    private void OpenRequestTypeChooser(string? category = null)
    {
        if (!string.IsNullOrWhiteSpace(category))
            _requestCategory = category;

        RefreshRequestTypeChooser();
        MoreOverlay.IsVisible = false;
        RequestSheetOverlay.IsVisible = false;
        RequestTypeDropdownPanel.IsVisible = false;
        ModalDateRangePanel.IsVisible = false;
        ModalRequestStatusLabel.Text = "";
        RequestTypeOverlay.IsVisible = true;
    }

    private void OnRequestCategoryClicked(object? sender, EventArgs e)
    {
        if (sender is Button button &&
            button.CommandParameter is string category &&
            !string.IsNullOrWhiteSpace(category))
        {
            _requestCategory = category;
            RefreshRequestTypeChooser();
        }
    }

    private void OnToggleRequestTypeDropdown(object? sender, EventArgs e) =>
        RequestTypeDropdownPanel.IsVisible = !RequestTypeDropdownPanel.IsVisible;

    private void OnRequestTypeChoiceClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button ||
            button.CommandParameter is not MobileRequestType type)
            return;

        RequestTypePicker.SelectedItem = type;
        RequestTypeDropdownPanel.IsVisible = false;
        UpdateRequestTypeFields();
        SyncModalRequestTypeFields();
    }

    private void OnOvertimeRequestChoice(object? sender, EventArgs e)
    {
        if (_overtimeRequestType is null)
            return;

        RequestTypePicker.SelectedItem = _overtimeRequestType;
        RequestTypeDropdownPanel.IsVisible = false;
        UpdateRequestTypeFields();
        SyncModalRequestTypeFields();
    }

    private void OnCloseRequestTypeChooser(object? sender, EventArgs e) =>
        RequestTypeOverlay.IsVisible = false;

    private void OnCloseRequestTypeChooserTapped(object? sender, TappedEventArgs e) =>
        RequestTypeOverlay.IsVisible = false;

    private void RefreshRequestTypeChooser()
    {
        var categoryItems = _requestCatalog
            .Where(x => string.Equals(
                x.Category?.Trim(),
                _requestCategory,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        RequestTypeChoicesList.ItemsSource = categoryItems;

        var selected = RequestTypePicker.SelectedItem as MobileRequestType;
        if (selected is null || !categoryItems.Any(x => x.Id == selected.Id))
        {
            selected = categoryItems.FirstOrDefault();
            RequestTypePicker.SelectedItem = selected;
        }

        _overtimeRequestType = _requestCatalog.FirstOrDefault(IsOvertimeRequestType);
        OvertimeChoiceButton.IsVisible = false;
        RequestSheetOvertimeButton.IsVisible = _overtimeRequestType is not null;
        if (_overtimeRequestType is not null)
            RequestSheetOvertimeButton.Text = _overtimeRequestType.DisplayTypeName;

        RequestTypeDropdownPanel.IsVisible = false;
        SyncModalRequestTypeFields();

        foreach (var (button, category) in new[]
                 {
                     (LeaveCategoryButton, "الإجازات"),
                     (CasualCategoryButton, "العرضية"),
                     (DepartureCategoryButton, "المغادرات")
                 })
        {
            var active = string.Equals(
                category,
                _requestCategory,
                StringComparison.OrdinalIgnoreCase);

            button.BackgroundColor = Color.FromArgb(active ? "#142F3A" : "#0B1A2A");
            button.TextColor = Color.FromArgb(active ? "#19D3E0" : "#9FB3C7");
            button.BorderColor = Color.FromArgb(active ? "#2F8E98" : "#263E55");
        }
    }

    private void SyncModalRequestTypeFields()
    {
        if (RequestTypePicker.SelectedItem is not MobileRequestType type)
        {
            ModalRequestTitleLabel.Text = "طلب إجازة";
            RequestCategoryTabs.IsVisible = true;
            ModalSelectedTypeButton.Text = "اختر النوع";
            ModalTimeFields.IsVisible = false;
            ModalDurationPanel.IsVisible = false;
            ModalAttachmentHint.Text = "المرفق اختياري";
            return;
        }

        var isOvertime = IsOvertimeRequestType(type);
        ModalRequestTitleLabel.Text = isOvertime ? "طلب عمل إضافي" : "طلب إجازة";
        RequestCategoryTabs.IsVisible = !isOvertime;
        ModalSelectedTypeButton.Text = RequestTypeModalLabel(type);
        ModalTimeFields.IsVisible = type.NeedsTime;
        ModalDurationPanel.IsVisible = type.NeedsTime;

        var attachmentText = type.AttachmentRequired
            ? "المرفق إلزامي"
            : "المرفق اختياري";

        if (!string.IsNullOrWhiteSpace(type.AttachmentLabel))
            attachmentText += $" · {type.AttachmentLabel}";

        ModalAttachmentHint.Text = attachmentText;
        ModalRequestReasonEditor.Placeholder = type.ReasonRequired
            ? "السبب (إلزامي)"
            : "اكتب السبب";

        UpdateModalDuration();
    }

    private string RequestTypeModalLabel(MobileRequestType type)
    {
        var balance = _currentLeave.FirstOrDefault(x =>
            string.Equals(x.Type?.Trim(), type.Name?.Trim(), StringComparison.OrdinalIgnoreCase));

        if (balance is not null)
        {
            var unit = string.Equals(balance.Unit, "Hours", StringComparison.OrdinalIgnoreCase)
                ? "ساعة"
                : "يوم";
            return UiLocalization.T(
                $"{type.DisplayTypeName} — المتبقي {balance.Remaining:0.#} {unit}");
        }

        return type.AllowedDays.HasValue
            ? UiLocalization.T($"{type.DisplayTypeName} — حتى {type.AllowedDays} يوم")
            : type.DisplayTypeName;
    }

    private static bool IsOvertimeRequestType(MobileRequestType type)
    {
        var category = type.Category ?? "";
        var name = type.Name ?? "";
        var nameEn = type.NameEn ?? "";

        return category.Contains("أوفر", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("إضاف", StringComparison.OrdinalIgnoreCase) ||
               nameEn.Contains("Overtime", StringComparison.OrdinalIgnoreCase);
    }

    private void OnOpenModalDateRange(object? sender, EventArgs e)
    {
        RequestTypeDropdownPanel.IsVisible = false;
        ModalDateRangePanel.IsVisible = !ModalDateRangePanel.IsVisible;
    }

    private void OnModalDateChanged(object? sender, DateChangedEventArgs e)
    {
        var from = ModalFromDatePicker.Date ?? DateTime.Today;
        var to = ModalToDatePicker.Date ?? from;

        if (to < from)
            ModalToDatePicker.Date = from;

        UpdateModalDateSummary();
    }

    private void OnApplyModalDateRange(object? sender, EventArgs e)
    {
        UpdateModalDateSummary();
        ModalDateRangePanel.IsVisible = false;
    }

    private void OnModalTimeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == TimePicker.TimeProperty.PropertyName)
            UpdateModalDuration();
    }

    private void UpdateModalDateSummary()
    {
        var from = ModalFromDatePicker.Date ?? DateTime.Today;
        var to = ModalToDatePicker.Date ?? from;

        if (to < from)
            to = from;

        ModalDateTriggerButton.Text = from.Date == to.Date
            ? from.ToString("yyyy-MM-dd")
            : $"{from:yyyy-MM-dd}  ←  {to:yyyy-MM-dd}";

        var days = (to.Date - from.Date).Days + 1;
        ModalDayCountLabel.Text = $"عدد الأيام: {days}";
    }

    private void UpdateModalDuration()
    {
        if (RequestTypePicker.SelectedItem is not MobileRequestType type ||
            !type.NeedsTime)
        {
            ModalDurationPanel.IsVisible = false;
            ModalCrossNote.IsVisible = false;
            ModalPunchBlock.IsVisible = false;
            return;
        }

        ModalDurationPanel.IsVisible = true;
        ModalPunchBlock.IsVisible = true;

        var start = ModalStartTimePicker.Time ?? TimeSpan.Zero;
        var end = ModalEndTimePicker.Time ?? TimeSpan.Zero;
        var crossMidnight = end <= start;

        if (crossMidnight)
            end = end.Add(TimeSpan.FromDays(1));

        var duration = end - start;
        ModalDurationLabel.Text =
            $"المدة المطلوبة: {(int)duration.TotalHours:00}:{duration.Minutes:00}";
        ModalCrossNote.IsVisible = crossMidnight;

        var selectedDate = ModalFromDatePicker.Date ?? DateTime.Today;
        var attendance = _currentAttendance.FirstOrDefault(x =>
            DateTime.TryParse(x.Date, out var d) &&
            d.Date == selectedDate.Date);

        if (attendance is null)
        {
            ModalPunchLabel.Text = "لا توجد بصمات مسجلة لهذا اليوم.";
        }
        else
        {
            var checkInText = attendance.CheckIn ?? "—";
            var checkOutText = attendance.CheckOut ?? "—";
            ModalPunchLabel.Text =
                $"أول دخول: {checkInText}   •   آخر خروج: {checkOutText}";
        }
    }

    private async void OnPickModalRequestAttachment(object? sender, EventArgs e)
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
            ModalAttachmentName.Text = picked.FileName;
            ModalRequestStatusLabel.Text = "";
        }
        catch
        {
            ModalRequestStatusLabel.Text = "تعذر فتح منتقي الملفات.";
        }
    }

    private async void OnSubmitModalRequest(object? sender, EventArgs e)
    {
        if (_busy) return;

        if (!Online())
        {
            ModalRequestStatusLabel.Text = "لا يمكن إرسال الطلب بدون إنترنت.";
            return;
        }

        if (RequestTypePicker.SelectedItem is not MobileRequestType requestType)
        {
            ModalRequestStatusLabel.Text = "اختر نوع الطلب.";
            return;
        }

        var from = ModalFromDatePicker.Date ?? DateTime.Today;
        var to = ModalToDatePicker.Date ?? from;
        if (to < from)
        {
            ModalRequestStatusLabel.Text =
                "تاريخ النهاية لا يمكن أن يسبق تاريخ البداية.";
            return;
        }

        TimeSpan? startTime = null;
        TimeSpan? endTime = null;

        if (requestType.NeedsTime)
        {
            startTime = ModalStartTimePicker.Time;
            endTime = ModalEndTimePicker.Time;

            if (startTime is null || endTime is null)
            {
                ModalRequestStatusLabel.Text =
                    "حدد وقت البداية والنهاية.";
                return;
            }

            if (endTime <= startTime && to.Date == from.Date)
                to = from.AddDays(1);
        }

        if (requestType.AttachmentRequired &&
            _requestAttachment is null)
        {
            ModalRequestStatusLabel.Text =
                "هذا النوع يتطلب مرفقاً قبل الإرسال.";
            return;
        }

        if (requestType.ReasonRequired &&
            string.IsNullOrWhiteSpace(ModalRequestReasonEditor.Text))
        {
            ModalRequestStatusLabel.Text =
                "سبب الطلب إلزامي حسب سياسة الشركة.";
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
                    ModalRequestReasonEditor.Text,
                    _requestAttachment);

                ModalRequestStatusLabel.Text = result.Message;
                ModalRequestReasonEditor.Text = "";
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
                ModalRequestStatusLabel.Text = ex.Message;
            }
            catch
            {
                ModalRequestStatusLabel.Text =
                    "تعذر إرسال الطلب حالياً.";
            }
        });
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

    private void OnLogout(object? sender, EventArgs e)
    {
        ShowLogoutConfirmation();
    }

    private void ShowLogoutConfirmation()
    {
        if (_busy)
            return;

        LogoutConfirmOverlay.IsVisible = true;
    }

    private void HideLogoutConfirmation()
    {
        LogoutConfirmOverlay.IsVisible = false;
    }

    private void OnCancelLogoutClicked(object? sender, EventArgs e)
    {
        HideLogoutConfirmation();
    }

    private void OnCancelLogoutTapped(object? sender, TappedEventArgs e)
    {
        HideLogoutConfirmation();
    }

    private async void OnConfirmLogoutClicked(object? sender, EventArgs e)
    {
        if (_busy)
            return;

        HideLogoutConfirmation();
        await ExecuteLogoutAsync();
    }

    private async Task ExecuteLogoutAsync()
    {
        await Busy("جاري تسجيل الخروج...", async () =>
        {
            try { await _api.LogoutAsync(); }
            catch { }

            ClearHomeCaches();
            try
            {
                if (File.Exists(ProfilePhotoCachePath))
                    File.Delete(ProfilePhotoCachePath);
            }
            catch
            {
            }

            ResetRequestAttachment();
            UsernameEntry.Text = "";
            PasswordEntry.Text = "";
            ShowLogin();
        });
    }

    private async Task LoadAsync()
    {
        if (_busy) return;

        var loadedCache = await LoadHomeCacheAsync();
        if (loadedCache)
        {
            AuthNav.IsVisible = true;
            LoginCard.IsVisible = false;
            ShowActiveSection();

            if (!Online())
            {
                SetPunchMessage("وضع عدم الاتصال — يتم عرض آخر بيانات محفوظة على الجهاز.");
                return;
            }

            // Show cached data immediately, then refresh silently in the background.
            _ = RefreshHomeSilentlyAsync();
            return;
        }

        if (!Online())
        {
            AuthNav.IsVisible = true;
            LoginCard.IsVisible = false;
            ShowActiveSection();
            SetPunchMessage("أنت غير متصل بالإنترنت ولا توجد بيانات محفوظة بعد.");
            return;
        }

        // First successful load only: no cache exists yet, so a blocking loader is appropriate.
        await Busy("جاري تحميل بياناتك...", LoadCoreAsync);
    }

    private async Task RefreshHomeSilentlyAsync()
    {
        try
        {
            await LoadCoreAsync();
        }
        catch
        {
            // LoadCoreAsync already handles API/session failures.
        }
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

            var identityDocuments = new List<MobileIdentityDocument>();
            try
            {
                identityDocuments = await _api.IdentityDocumentsAsync();
            }
            catch (MobileApiException)
            {
                // الوثائق إضافة للملف الشخصي؛ لا نعطّل الصفحة عند تعذرها.
            }

            byte[]? profilePhoto = null;
            if (profile.HasPhoto)
            {
                try
                {
                    profilePhoto = await _api.ProfilePhotoAsync();
                }
                catch (MobileApiException)
                {
                    // الصورة اختيارية؛ يبقى رمز Z كبديل عند تعذرها.
                }
            }

            if (profile.HasPhoto &&
                profilePhoto is null &&
                File.Exists(ProfilePhotoCachePath))
            {
                try
                {
                    profilePhoto = await File.ReadAllBytesAsync(ProfilePhotoCachePath);
                }
                catch
                {
                }
            }

            RenderHome(
                profile,
                attendance,
                leave,
                announcements,
                compensation,
                identityDocuments,
                profilePhoto);
            await SaveHomeCacheAsync(
                profile,
                attendance,
                leave,
                announcements,
                compensation,
                identityDocuments,
                profilePhoto);

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

            _requestCatalog = catalog.Items ?? new();

            // Request creation lives under the dedicated "طلب جديد" sheet.
            // "طلباتي" is tracking/history only and must not show the legacy correction form.
            MissingPunchRequestCard.IsVisible = false;

            RequestTypePicker.ItemsSource = _requestCatalog;
            RequestTypePicker.IsEnabled =
                catalog.Eligible && _requestCatalog.Count > 0;

            if (!catalog.Eligible)
            {
                RequestStatusLabel.Text =
                    catalog.Message ?? "لا يمكنك تقديم طلب حالياً.";
                RequestTypePicker.SelectedIndex = -1;
            }
            else if (_requestCatalog.Count == 0)
            {
                RequestStatusLabel.Text =
                    "لا توجد أنواع طلبات متاحة لهذا الحساب.";
                RequestTypePicker.SelectedIndex = -1;
            }
            else
            {
                var selectedIndex = currentId is int id
                    ? _requestCatalog.FindIndex(x => x.Id == id)
                    : -1;

                RequestTypePicker.SelectedIndex =
                    selectedIndex >= 0 ? selectedIndex : -1;

                if (selectedIndex < 0)
                    RequestStatusLabel.Text = "";
            }

            RefreshRequestTypeChooser();
            UpdateRequestTypeFields();
            _requestsLoaded = true;
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
        MobileCompensation? compensation = null,
        List<MobileIdentityDocument>? identityDocuments = null,
        byte[]? profilePhoto = null)
    {
        var now = DateTime.Now;
        GreetingLabel.Text = "EMPLOYEE EXPERIENCE PORTAL";
        TodayLabel.Text = now.ToString(
            "dddd، d MMMM",
            CultureInfo.CurrentCulture);
        AttendancePageTodayLabel.Text = TodayLabel.Text;

        _currentProfile = profile;
        _currentAttendance = attendance;
        _currentLeave = leave;

        var employeeName =
            string.IsNullOrWhiteSpace(profile.DisplayName)
                ? UiLocalization.T("موظف ZYNORA")
                : profile.DisplayName;

        NameLabel.Text = UiLocalization.T($"أهلاً {employeeName}");
        IdentityNameLabel.Text = employeeName;

        PositionLabel.Text =
            string.IsNullOrWhiteSpace(profile.DisplayPosition)
                ? UiLocalization.T("بدون منصب")
                : profile.DisplayPosition;

        OrgLabel.Text =
            string.Join(
                " · ",
                new[] { profile.DisplayDepartment, profile.DisplayBranch }
                    .Where(x => !string.IsNullOrWhiteSpace(x)));

        EmployeeNoLabel.Text = profile.EmployeeNo;

        ProfileNameLabel.Text =
            string.IsNullOrWhiteSpace(profile.DisplayName)
                ? UiLocalization.T("موظف ZYNORA")
                : profile.DisplayName;
        ProfilePositionLabel.Text =
            string.IsNullOrWhiteSpace(profile.DisplayPosition)
                ? UiLocalization.T("بدون منصب")
                : profile.DisplayPosition;
        ProfileEmployeeNoLabel.Text =
            string.IsNullOrWhiteSpace(profile.EmployeeNo)
                ? "—"
                : profile.EmployeeNo;
        ProfileDepartmentLabel.Text =
            string.IsNullOrWhiteSpace(profile.DisplayDepartment)
                ? "—"
                : profile.DisplayDepartment;
        ProfileBranchLabel.Text =
            string.IsNullOrWhiteSpace(profile.DisplayBranch)
                ? "—"
                : profile.DisplayBranch;
        ProfileStatusLabel.Text = UiLocalization.T(
            profile.IsActive ? "نشط" : "غير نشط");
        ProfileStatusLabel.TextColor = profile.IsActive
            ? Color.FromArgb("#3FD49B")
            : Color.FromArgb("#FF7383");

        ProfileEnglishNameLabel.Text = DisplayValue(profile.EnglishName);
        ProfilePhoneLabel.Text = DisplayValue(profile.Phone);
        ProfileEmailLabel.Text = DisplayValue(profile.PreferredEmail);
        ProfileNationalIdLabel.Text = DisplayValue(profile.NationalId);
        ProfileBirthDateLabel.Text = DisplayValue(profile.BirthDate);
        ProfileGenderNationalityLabel.Text = DisplayValue(
            string.Join(
                " · ",
                new[] { profile.DisplayGender, profile.DisplayNationality }
                    .Where(value => !string.IsNullOrWhiteSpace(value))));
        ProfileJoiningDateLabel.Text = DisplayValue(profile.JoiningDate ?? profile.HireDate);
        ProfileWorkTypeLabel.Text = DisplayValue(profile.DisplayWorkType);
        ProfileJobGradeLabel.Text = DisplayValue(profile.DisplayJobGrade);
        ProfileEmploymentStatusLabel.Text = DisplayValue(profile.DisplayEmploymentStatus);

        var documents = identityDocuments ?? new List<MobileIdentityDocument>();
        ProfileIdentityDocumentsContainer.BindingContext = documents;
        ProfileIdentityDocumentsEmptyLabel.IsVisible = documents.Count == 0;

        RenderProfilePhoto(profilePhoto);

        var todayKey = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var todayAttendance = attendance.FirstOrDefault(row =>
            string.Equals(row.Date, todayKey, StringComparison.OrdinalIgnoreCase));
        AttendanceCheckInButton.IsEnabled =
            todayAttendance is null ||
            string.IsNullOrWhiteSpace(todayAttendance.CheckIn);
        AttendanceCheckOutButton.IsEnabled =
            todayAttendance is not null &&
            !string.IsNullOrWhiteSpace(todayAttendance.CheckIn) &&
            string.IsNullOrWhiteSpace(todayAttendance.CheckOut);

        AttendanceHistoryContainer.BindingContext = attendance;
        AttendanceEmptyLabel.IsVisible = attendance.Count == 0;
        ProfileLeaveBalancesContainer.BindingContext = leave;

        HomeLeaveBalancesContainer.BindingContext = leave;
        HomeLeaveEmptyLabel.IsVisible = leave.Count == 0;
        BalanceYearLabel.Text = now.Year.ToString(
            CultureInfo.InvariantCulture);

        var homeAnnouncements = announcements ?? new List<MobileAnnouncement>();
        AnnouncementsList.ItemsSource = homeAnnouncements;
        AnnouncementsEmptyLabel.IsVisible = homeAnnouncements.Count == 0;
        AnnouncementCountLabel.Text =
            $"{homeAnnouncements.Count} منشور";

        EmployeeInsightLabel.Text =
            attendance.Any(row =>
                string.IsNullOrWhiteSpace(row.CheckOut) &&
                !string.IsNullOrWhiteSpace(row.CheckIn))
                ? "لديك حركة حضور تحتاج إلى مراجعة"
                : "لا توجد تنبيهات مهمة حالياً";

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
            CompensationPayrollPeriodLabel.Text = "—";
            CompensationPayrollStatusLabel.Text = "";
            CompensationBasicLabel.Text = "—";
            CompensationAllowancesLabel.Text = "—";
            CompensationDeductionsLabel.Text = "—";
            CompensationGrossLabel.Text = "—";
            CompensationTaxLabel.Text = "—";
            CompensationGosiLabel.Text = "—";
            CompensationWorkDaysLabel.Text = "—";
            CompensationAbsentDaysLabel.Text = "—";
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
        CompensationPayrollPeriodLabel.Text = compensation.PayrollRunId is int runId
            ? $"{compensation.PayrollPeriod} · Run #{runId}"
            : "لا يوجد احتساب Payroll بعد";
        CompensationPayrollStatusLabel.Text = string.IsNullOrWhiteSpace(compensation.PayrollStatus)
            ? ""
            : $"حالة الاحتساب: {compensation.PayrollStatus}";
        CompensationBasicLabel.Text = compensation.BasicSalary.ToString("N0");
        CompensationAllowancesLabel.Text = compensation.Allowances.ToString("N0");
        CompensationDeductionsLabel.Text = compensation.Deductions.ToString("N0");
        CompensationGrossLabel.Text = compensation.Gross.ToString("N0");
        CompensationTaxLabel.Text = compensation.TaxAmount.ToString("N0");
        CompensationGosiLabel.Text = compensation.GosiEmployee.ToString("N0");
        CompensationWorkDaysLabel.Text = compensation.WorkDays.ToString("0.##");
        CompensationAbsentDaysLabel.Text = compensation.AbsentDays.ToString("0.##");
        CompensationPaymentMethodLabel.Text = DisplayValue(compensation.PaymentMethod);
        CompensationBankLabel.Text = DisplayValue(compensation.BankName);
        CompensationAccountLabel.Text = DisplayValue(compensation.BankAccount);
    }

    private static string DisplayValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

    private void RenderProfilePhoto(byte[]? photoBytes)
    {
        var hasPhoto = photoBytes is { Length: > 0 };

        HomePhotoImage.IsVisible = hasPhoto;
        ProfilePhotoImage.IsVisible = hasPhoto;
        HomePhotoFallbackLabel.IsVisible = !hasPhoto;
        ProfilePhotoFallbackLabel.IsVisible = !hasPhoto;

        if (!hasPhoto)
        {
            HomePhotoImage.Source = null;
            ProfilePhotoImage.Source = null;
            return;
        }

        HomePhotoImage.Source = ImageSource.FromStream(
            () => new MemoryStream(photoBytes!, writable: false));
        ProfilePhotoImage.Source = ImageSource.FromStream(
            () => new MemoryStream(photoBytes!, writable: false));
    }

    private static string ProfilePhotoCachePath =>
        Path.Combine(FileSystem.AppDataDirectory, ProfilePhotoCacheFileName);

    private static void ClearHomeCaches()
    {
        SecureStorage.Default.Remove(LegacyHomeCacheKey);
        foreach (var language in new[]
                 {
                     UiLocalization.Arabic,
                     UiLocalization.English,
                     UiLocalization.Kurdish
                 })
        {
            SecureStorage.Default.Remove($"{HomeCacheKeyPrefix}.{language}");
        }
    }

    private async Task SaveHomeCacheAsync(
        EmployeeProfile profile,
        List<AttendanceDay> attendance,
        List<LeaveBalance> leave,
        List<MobileAnnouncement> announcements,
        MobileCompensation compensation,
        List<MobileIdentityDocument> identityDocuments,
        byte[]? profilePhoto)
    {
        try
        {
            var json = JsonSerializer.Serialize(
                new HomeCacheSnapshot
                {
                    Profile = profile,
                    Attendance = attendance,
                    Leave = leave,
                    Announcements = announcements,
                    Compensation = compensation,
                    IdentityDocuments = identityDocuments,
                    CachedAtUtc = DateTime.UtcNow
                },
                CacheJson);

            await SecureStorage.Default.SetAsync(HomeCacheKey, json);

            if (profile.HasPhoto && profilePhoto is { Length: > 0 })
            {
                await File.WriteAllBytesAsync(ProfilePhotoCachePath, profilePhoto);
            }
            else if (!profile.HasPhoto && File.Exists(ProfilePhotoCachePath))
            {
                File.Delete(ProfilePhotoCachePath);
            }
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

            byte[]? cachedPhoto = null;
            if (cache.Profile.HasPhoto && File.Exists(ProfilePhotoCachePath))
            {
                try
                {
                    cachedPhoto = await File.ReadAllBytesAsync(ProfilePhotoCachePath);
                }
                catch
                {
                }
            }

            // One-time migration for existing installations: if the old home cache
            // says a photo exists but no local photo file has been created yet,
            // fetch it before rendering the cached home instead of flashing the Z fallback.
            if (cache.Profile.HasPhoto && cachedPhoto is null && Online())
            {
                try
                {
                    cachedPhoto = await _api.ProfilePhotoAsync();
                    if (cachedPhoto is { Length: > 0 })
                        await File.WriteAllBytesAsync(ProfilePhotoCachePath, cachedPhoto);
                }
                catch
                {
                }
            }

            RenderHome(
                cache.Profile,
                cache.Attendance ?? new(),
                cache.Leave ?? new(),
                cache.Announcements ?? new(),
                cache.Compensation,
                cache.IdentityDocuments ?? new(),
                cachedPhoto);

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
            SelectedRequestTypeButton.Text = "اختر نوع الطلب";
            SelectedRequestTypeCategoryLabel.Text = "اضغط لاختيار القسم ونوع الطلب";
            RequestTimeFields.IsVisible = false;
            RequestAttachmentFields.IsVisible = false;
            return;
        }

        SelectedRequestTypeButton.Text = type.DisplayTypeName;
        SelectedRequestTypeCategoryLabel.Text =
            string.IsNullOrWhiteSpace(type.DisplayCategory)
                ? UiLocalization.T("نوع الطلب المحدد")
                : type.DisplayCategory;

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
        ModalAttachmentName.Text = "لم يتم اختيار ملف";
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

    private async Task SwitchSectionAsync(string targetSection, Action showSection)
    {
        showSection();

        // All authenticated sections share one ScrollView. Always start the destination
        // section at the top, but without animation so there is no visible upward scroll.
        await Task.Yield();
        await MainScrollView.ScrollToAsync(0, 0, false);
    }

    private void ShowLogin(bool allowBiometricRetry = false)
    {
        _activeSection = "Login";
        LoginCard.IsVisible = true;
        BiometricRetryButton.IsVisible = allowBiometricRetry;
        _ = RefreshBiometricRetryVisibilityAsync(allowBiometricRetry);
        AuthenticatedHeaderActions.IsVisible = false;
        TwoFactorLoginPanel.IsVisible = false;
        TwoFactorCodeEntry.Text = "";
        LoginButton.Text = "دخول إلى ZYNORA";
        AuthNav.IsVisible = false;
        RequestFab.IsVisible = false;
        MoreOverlay.IsVisible = false;
        RequestSheetOverlay.IsVisible = false;
        RequestTypeOverlay.IsVisible = false;
        HomePanel.IsVisible = false;
        AttendancePanel.IsVisible = false;
        RequestsPanel.IsVisible = false;
        ProfilePanel.IsVisible = false;
        CompensationPanel.IsVisible = false;
        _requestsLoaded = false;
        _requestCatalog = new();
        _overtimeRequestType = null;
        RequestsList.ItemsSource = null;
        MissingPunchesList.ItemsSource = null;
    }

    private async Task RefreshBiometricRetryVisibilityAsync(bool forceVisible)
    {
        if (forceVisible)
        {
            BiometricRetryButton.IsVisible = true;
            return;
        }

        if (!Preferences.Default.Get(BiometricLockKey, false) ||
            !DeviceBiometricAuth.IsAvailable())
        {
            BiometricRetryButton.IsVisible = false;
            return;
        }

        var hasSession = await _api.HasSessionAsync();
        if (string.Equals(_activeSection, "Login", StringComparison.Ordinal))
            BiometricRetryButton.IsVisible = hasSession;
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
        ShowAuthenticatedPanel(CompensationPanel, CompensationTabButton);
    }

    private void ShowActiveSection()
    {
        switch (_activeSection)
        {
            case "Login":
                ShowLogin();
                break;
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
        BiometricRetryButton.IsVisible = false;
        AuthenticatedHeaderActions.IsVisible = true;
        AuthNav.IsVisible = true;
        RequestFab.IsVisible = true;

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
                     CompensationTabButton,
                     ProfileTabButton
                 })
        {
            button.BackgroundColor = Colors.Transparent;
            button.TextColor = Color.FromArgb("#7F95AB");
        }

        activeButton.BackgroundColor = Color.FromArgb("#1119D3E0");
        activeButton.TextColor = Color.FromArgb("#19D3E0");
    }

    private void SetPunchMessage(string message)
    {
        AttendancePunchLabel.Text = UiLocalization.T(message);
    }

    private void Error(string message)
    {
        ErrorLabel.Text = UiLocalization.T(message);
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
        public List<MobileAnnouncement>? Announcements { get; set; }
        public MobileCompensation? Compensation { get; set; }
        public List<MobileIdentityDocument>? IdentityDocuments { get; set; }
        public DateTime CachedAtUtc { get; set; }
    }
}
