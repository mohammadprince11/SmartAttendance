using System.Globalization;
using System.Text.Json;
using Microsoft.Maui.Layouts;

namespace ZynoraHR.Mobile;

public partial class MainPage : ContentPage
{
    private const string HomeCacheKey = "zynora.mobile.home_cache.v1";
    private const string BiometricLockKey = "zynora.mobile.biometric_lock.v1";
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
    private MobileRequestType? _overtimeRequestType;
    private string _requestCategory = "الإجازات";

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
                "فتح ZYNORA HR",
                "تحقق ببصمة الوجه أو الأصبع للمتابعة.");

            if (!unlocked)
            {
                ShowLogin();
                Error("تم إلغاء التحقق البيومتري. يمكنك تسجيل الدخول بكلمة المرور.");
                return;
            }
        }

        if (hasSession) await LoadAsync();
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

    private void OnMoreTab(object? sender, EventArgs e)
    {
        MoreOverlay.IsVisible = true;
        SetNavState(ProfileTabButton);
    }

    private async void OnProfileFromMore(object? sender, EventArgs e)
    {
        MoreOverlay.IsVisible = false;
        ShowProfile();
        await MainScrollView.ScrollToAsync(0, 0, true);

        if (Online() && !_busy)
            await LoadAsync();
    }

    private void OnCloseMoreTapped(object? sender, TappedEventArgs e)
    {
        MoreOverlay.IsVisible = false;
        ShowActiveSection();
    }

    private async void OnCompensationTab(object? sender, EventArgs e)
    {
        ShowCompensation();
        await MainScrollView.ScrollToAsync(0, 0, true);

        if (Online() && !_busy)
            await LoadAsync();
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
                RequestSheetOvertimeButton.Text = _overtimeRequestType.Name;
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
    }

    private void OpenServiceRequest(string title, string subtitle)
    {
        MoreOverlay.IsVisible = false;
        RequestSheetOverlay.IsVisible = false;
        RequestTypeOverlay.IsVisible = false;
        ServiceRequestHost.Children.Clear();
        ServiceRequestTitle.Text = title;
        ServiceRequestSubtitle.Text = subtitle;
        ServiceRequestOverlay.IsVisible = true;
    }

    private static Label ServiceFieldTitle(string text) => new()
    {
        Text = text,
        TextColor = Color.FromArgb("#CFE0F0"),
        FontSize = 12.5,
        FontAttributes = FontAttributes.Bold
    };

    private static Label ServiceHint(string text) => new()
    {
        Text = text,
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
        Placeholder = placeholder,
        PlaceholderColor = Color.FromArgb("#6F8498"),
        TextColor = Color.FromArgb("#E6F0FA"),
        BackgroundColor = Color.FromArgb("#06101D"),
        FontSize = 13,
        HeightRequest = 48,
        Keyboard = keyboard ?? Keyboard.Default
    };

    private static Editor ServiceEditor(string placeholder) => new()
    {
        Placeholder = placeholder,
        PlaceholderColor = Color.FromArgb("#6F8498"),
        TextColor = Color.FromArgb("#E6F0FA"),
        BackgroundColor = Color.FromArgb("#06101D"),
        FontSize = 12.5,
        AutoSize = EditorAutoSizeOption.TextChanges,
        HeightRequest = 82
    };

    private static Button ServiceSelectorButton(string text) => new()
    {
        Text = text,
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
        Text = text,
        BackgroundColor = Color.FromArgb("#19CFE0"),
        TextColor = Color.FromArgb("#041018"),
        FontAttributes = FontAttributes.Bold,
        FontSize = 14,
        CornerRadius = 14,
        HeightRequest = 52
    };

    private static VerticalStackLayout ServiceOptionsPanel() => new()
    {
        IsVisible = false,
        Spacing = 3,
        Padding = new Thickness(6),
        BackgroundColor = Color.FromArgb("#0B2033")
    };

    private async void OnNotificationsTapped(object? sender, TappedEventArgs e)
    {
        await BuildNotificationsServiceAsync();
    }

    private void OnSettingsTapped(object? sender, TappedEventArgs e)
    {
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

        var profileName = string.IsNullOrWhiteSpace(_currentProfile?.FullName)
            ? "غير مسجل"
            : _currentProfile!.FullName;
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

        var languageButton = ServiceSelectorButton("لغة التطبيق  ·  العربية");
        languageButton.Clicked += (_, _) => BuildLanguageSettingsService();
        ServiceRequestHost.Children.Add(languageButton);

        var securityButton = ServiceSelectorButton("الأمان والخصوصية");
        securityButton.Clicked += (_, _) => BuildSecurityPrivacyService();
        ServiceRequestHost.Children.Add(securityButton);

        ServiceRequestHost.Children.Add(ServiceFieldTitle("التطبيق"));
        ServiceRequestHost.Children.Add(ServiceHint(
            $"ZYNORA HR · الإصدار {AppInfo.Current.VersionString}\n" +
            $"حالة الاتصال: {(Online() ? "متصل" : "غير متصل")}"));

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
        logoutButton.Clicked += async (_, _) =>
        {
            var confirmed = await DisplayAlertAsync(
                "تسجيل الخروج",
                "سيتم إنهاء الجلسة على هذا الجهاز. هل تريد المتابعة؟",
                "تسجيل الخروج",
                "إلغاء");
            if (confirmed)
            {
                CloseServiceRequest();
                OnLogout(logoutButton, EventArgs.Empty);
            }
        };
        ServiceRequestHost.Children.Add(logoutButton);
    }

    private void BuildLanguageSettingsService()
    {
        OpenServiceRequest(
            "لغة التطبيق",
            "لغة واجهة ZYNORA HR على هذا الجهاز.");

        ServiceRequestHost.Children.Add(ServiceFieldTitle("اللغة الحالية"));
        ServiceRequestHost.Children.Add(ServiceHint("العربية · العراق"));

        var arabic = ServiceSelectorButton("العربية  ✓");
        arabic.IsEnabled = false;
        ServiceRequestHost.Children.Add(arabic);

        var english = ServiceSelectorButton("English  ·  قريباً");
        english.IsEnabled = false;
        ServiceRequestHost.Children.Add(english);

        var kurdish = ServiceSelectorButton("کوردی  ·  قريباً");
        kurdish.IsEnabled = false;
        ServiceRequestHost.Children.Add(kurdish);

        ServiceRequestHost.Children.Add(ServiceHint(
            "التبديل الكامل للغة يحتاج تحويل النصوص الحالية إلى Localization Resources. " +
            "لن يتم عرض لغة جزئية أو مختلطة للمستخدم."));
    }

    private void BuildSecurityPrivacyService()
    {
        OpenServiceRequest(
            "الأمان والخصوصية",
            "إدارة حماية الحساب والجهاز والجلسة.");

        ServiceRequestHost.Children.Add(ServiceFieldTitle("المصادقة الثنائية (2FA)"));
        ServiceRequestHost.Children.Add(ServiceHint(
            "الحالة: غير مفعلة في تطبيق الموبايل حالياً. " +
            "سيتم دعم Authenticator/TOTP كعامل ثانٍ مستقل بدون الاعتماد على SMS."));

        var twoFactor = ServiceSelectorButton("إعداد المصادقة الثنائية  ·  غير متاح حالياً");
        twoFactor.IsEnabled = false;
        ServiceRequestHost.Children.Add(twoFactor);

        ServiceRequestHost.Children.Add(ServiceFieldTitle("قفل التطبيق بالبصمة / الوجه"));
        var biometricLockEnabled =
            Preferences.Default.Get(BiometricLockKey, false);
        var biometricAvailable = DeviceBiometricAuth.IsAvailable();

        ServiceRequestHost.Children.Add(ServiceHint(
            biometricAvailable
                ? $"الحالة: {(biometricLockEnabled ? "مفعّل" : "غير مفعّل")}. " +
                  "عند التفعيل سيطلب ZYNORA تحققاً بيومترياً قبل فتح الجلسة المحفوظة."
                : "لا توجد بصمة/وجه مسجلة أو مدعومة على هذا الجهاز."));

        var biometricLock = ServiceSelectorButton(
            biometricLockEnabled
                ? "إلغاء قفل التطبيق بالبصمة/الوجه"
                : "تفعيل قفل التطبيق بالبصمة/الوجه");
        biometricLock.IsEnabled = biometricAvailable;
        biometricLock.Clicked += async (_, _) =>
        {
            var verified = await DeviceBiometricAuth.AuthenticateAsync(
                biometricLockEnabled ? "إلغاء القفل البيومتري" : "تفعيل القفل البيومتري",
                "تحقق ببصمة الوجه أو الأصبع للمتابعة.");

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
            "مفتاح WebAuthn/Passkey يستخدم بصمة أو وجه الجهاز لتأكيد الحضور. " +
            "بعد التسجيل يبقى المفتاح معلّقاً حتى يعتمد من الموارد البشرية."));

        var webAuthn = ServiceSelectorButton("إدارة مفتاح بصمة/وجه الحضور");
        webAuthn.Clicked += async (_, _) =>
            await BuildBiometricKeyServiceAsync();
        ServiceRequestHost.Children.Add(webAuthn);

        ServiceRequestHost.Children.Add(ServiceFieldTitle("حماية الجلسة والبيانات"));
        ServiceRequestHost.Children.Add(ServiceHint(
            "جلسة الموبايل محفوظة في SecureStorage، ولقطة البيانات Offline مشفرة، " +
            "وتُمسح عند تسجيل الخروج. تغيير كلمة المرور يبطل الجلسات القديمة عبر SecurityStamp."));

        var password = ServiceSelectorButton("تغيير كلمة المرور");
        password.Clicked += (_, _) => BuildChangePasswordService();
        ServiceRequestHost.Children.Add(password);
    }

    private void BuildChangePasswordService()
    {
        OpenServiceRequest(
            "تغيير كلمة المرور",
            "إدارة كلمة مرور حساب ZYNORA من داخل التطبيق.");

        ServiceRequestHost.Children.Add(ServiceHint(
            "لن يتم فتح متصفح خارجي. نموذج التغيير Native، " +
            "وسيتم تفعيل الحفظ بعد ربط خدمة تغيير كلمة المرور الآمنة بالموبايل."));

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
            "إدارة حالة مفاتيح WebAuthn الخاصة بحسابك من داخل ZYNORA.");

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
                    $"الحالة: {item.StatusText}",
                    $"تاريخ التسجيل: {item.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}"
                };

                if (item.ApprovedAt is { } approved)
                    details.Add($"تاريخ الاعتماد: {approved.ToLocalTime():yyyy-MM-dd HH:mm}");
                if (item.LastUsedAt is { } lastUsed)
                    details.Add($"آخر استخدام: {lastUsed.ToLocalTime():yyyy-MM-dd HH:mm}");

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
                "تسجيل مفتاح جديد أو استبداله يحتاج عملية WebAuthn آمنة؛ " +
                "هذه الشاشة تعرض الحالة داخل التطبيق ولا تفتح المتصفح تلقائياً."));
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
                    var type = i % 2 == 0 ? "دخول" : "خروج";
                    var marker = points[i].Proposed ? "  ← البصمة المقترحة" : "";
                    lines.Add($"{i + 1}. {points[i].Time.ToString(@"hh\:mm")} · {type}{marker}");

                    if (i % 2 == 1)
                        total += points[i].Time - points[i - 1].Time;
                }

                var completeness = points.Count % 2 == 0
                    ? "تسلسل البصمات مكتمل بعد الإضافة."
                    : "يبقى تسلسل البصمات غير مكتمل بعد الإضافة.";

                preview.Text =
                    string.Join(Environment.NewLine, lines) +
                    Environment.NewLine +
                    $"ساعات العمل الناتجة: {(int)total.TotalHours:00}:{total.Minutes:00}" +
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
                preview.Text = ex.Message;
            }
            catch
            {
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
                submit.IsEnabled = true;
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
            if (string.Equals(field.Kind, "select", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(field.CurrentValue))
            {
                currentDisplay = field.Options.FirstOrDefault(option =>
                    string.Equals(
                        option.Value,
                        field.CurrentValue,
                        StringComparison.OrdinalIgnoreCase))?.Label
                    ?? field.CurrentValue;
            }

            ServiceRequestHost.Children.Add(
                ServiceFieldTitle(
                    $"{field.Label}  (الحالي: {(string.IsNullOrWhiteSpace(currentDisplay) ? "—" : currentDisplay)})"));

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
                    trigger.Text = "— لا تغيير —";
                    optionsPanel.IsVisible = false;
                };
                optionsPanel.Children.Add(noChange);

                foreach (var optionItem in field.Options)
                {
                    var item = optionItem;
                    var optionButton = ServiceSelectorButton(item.Label);
                    optionButton.FontSize = 12;
                    optionButton.HeightRequest = 42;
                    optionButton.Clicked += (_, _) =>
                    {
                        selectedValues[field.Key] = item.Value;
                        trigger.Text = item.Label;
                        optionsPanel.IsVisible = false;
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
                    Format = "yyyy-MM-dd",
                    BackgroundColor = Color.FromArgb("#06101D"),
                    TextColor = Color.FromArgb("#E6F0FA"),
                    HeightRequest = 48
                };
                datePicker.DateSelected += (_, _) =>
                {
                    var selected = datePicker.Date ?? DateTime.Today;
                    selectedValues[field.Key] = selected.ToString("yyyy-MM-dd");
                    trigger.Text = selected.ToString("yyyy-MM-dd");
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
            ServiceRequestHost.Children.Add(entry);
        }

        var reason = ServiceEditor("سبب التعديل (اختياري)");
        var submit = ServicePrimaryButton("إرسال طلب التعديل");

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
        var shiftTrigger = ServiceSelectorButton(selectedShift.Name);
        var shiftOptions = ServiceOptionsPanel();

        foreach (var shiftItem in catalog.Items)
        {
            var item = shiftItem;
            var option = ServiceSelectorButton(item.Name);
            option.FontSize = 12;
            option.HeightRequest = 44;
            option.Clicked += (_, _) =>
            {
                selectedShift = item;
                shiftTrigger.Text = item.Name;
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
        var kindTrigger = ServiceSelectorButton(selectedKind.Label);
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
        }

        foreach (var item in catalog.Items)
        {
            var option = ServiceSelectorButton(item.Label);
            option.FontSize = 12;
            option.HeightRequest = 44;
            option.Clicked += (_, _) =>
            {
                selectedKind = item;
                kindTrigger.Text = item.Label;
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
        ShowRequests();

        if (_overtimeRequestType is null)
            return;

        RequestTypePicker.SelectedItem = _overtimeRequestType;
        UpdateRequestTypeFields();
        await MainScrollView.ScrollToAsync(SelectedRequestTypeButton, ScrollToPosition.Center, true);
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
            RequestSheetOvertimeButton.Text = _overtimeRequestType.Name;

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
            ModalSelectedTypeButton.Text = "اختر النوع";
            ModalTimeFields.IsVisible = false;
            ModalDurationPanel.IsVisible = false;
            ModalAttachmentHint.Text = "المرفق اختياري";
            return;
        }

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
            return $"{type.Name} — المتبقي {balance.Remaining:0.#} {unit}";
        }

        return type.AllowedDays.HasValue
            ? $"{type.Name} — حتى {type.AllowedDays} يوم"
            : type.Name;
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
            ModalPunchLabel.Text =
                $"أول دخول: {attendance.CheckIn ?? "—"}   •   آخر خروج: {attendance.CheckOut ?? "—"}";
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
        GreetingLabel.Text = "EMPLOYEE EXPERIENCE PORTAL";
        TodayLabel.Text = now.ToString(
            "dddd، d MMMM",
            CultureInfo.GetCultureInfo("ar-IQ"));
        AttendancePageTodayLabel.Text = TodayLabel.Text;

        _currentProfile = profile;
        _currentAttendance = attendance;
        _currentLeave = leave;

        var employeeName =
            string.IsNullOrWhiteSpace(profile.FullName)
                ? "موظف ZYNORA"
                : profile.FullName;

        NameLabel.Text = $"أهلاً {employeeName}";
        IdentityNameLabel.Text = employeeName;

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
            SelectedRequestTypeButton.Text = "اختر نوع الطلب";
            SelectedRequestTypeCategoryLabel.Text = "اضغط لاختيار القسم ونوع الطلب";
            RequestTimeFields.IsVisible = false;
            RequestAttachmentFields.IsVisible = false;
            return;
        }

        SelectedRequestTypeButton.Text = type.Name;
        SelectedRequestTypeCategoryLabel.Text =
            string.IsNullOrWhiteSpace(type.Category)
                ? "نوع الطلب المحدد"
                : type.Category;

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

    private void ShowLogin()
    {
        _activeSection = "Login";
        LoginCard.IsVisible = true;
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