namespace ZynoraHR.Mobile;

public partial class MainPage : ContentPage
{
    private CancellationTokenSource? _loadingGuard;

    public MainPage()
    {
        InitializeComponent();
        PortalWebView.Source = AppSettings.EmployeePortalUrl;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        RefreshConnectionState(reloadWhenOnline: false);
        if (LoadingLayer.IsVisible)
            StartLoadingGuard();
    }

    protected override void OnDisappearing()
    {
        _loadingGuard?.Cancel();
        Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed()
    {
        if (PortalWebView.CanGoBack)
        {
            PortalWebView.GoBack();
            return true;
        }

        return base.OnBackButtonPressed();
    }

    private async void OnPortalNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (!Uri.TryCreate(e.Url, UriKind.Absolute, out var uri))
            return;

        if (!TrustedNavigationPolicy.IsAllowed(uri))
        {
            e.Cancel = true;
            if (TrustedNavigationPolicy.CanOpenExternally(uri))
                await Launcher.Default.OpenAsync(uri);
            return;
        }

        LoadingLayer.IsVisible = true;
        OfflineLayer.IsVisible = false;
        StartLoadingGuard();
    }

    private void OnPortalNavigated(object? sender, WebNavigatedEventArgs e)
    {
        _loadingGuard?.Cancel();
        LoadingLayer.IsVisible = false;
        OfflineLayer.IsVisible = e.Result != WebNavigationResult.Success || !HasInternetAccess();
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e) =>
        MainThread.BeginInvokeOnMainThread(() => RefreshConnectionState(reloadWhenOnline: true));

    private void OnRetryClicked(object? sender, EventArgs e) =>
        RefreshConnectionState(reloadWhenOnline: true);

    private void RefreshConnectionState(bool reloadWhenOnline)
    {
        var online = HasInternetAccess();
        OfflineLayer.IsVisible = !online;
        if (!online)
        {
            LoadingLayer.IsVisible = false;
            return;
        }

        if (reloadWhenOnline)
        {
            LoadingLayer.IsVisible = true;
            PortalWebView.Reload();
            StartLoadingGuard();
        }
    }

    // Some Android WebView builds do not raise MAUI's Navigated event after an
    // HTTP redirect. Polling document.readyState prevents the branded loading
    // layer from covering a login page that has already finished rendering.
    private void StartLoadingGuard()
    {
        _loadingGuard?.Cancel();
        var guard = new CancellationTokenSource();
        _loadingGuard = guard;
        _ = RevealPortalWhenReadyAsync(guard.Token);
    }

    private async Task RevealPortalWhenReadyAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                await Task.Delay(500, cancellationToken);
                var state = (await PortalWebView.EvaluateJavaScriptAsync("document.readyState"))?.Trim('"');
                if (state is "interactive" or "complete")
                {
                    RevealPortal();
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // The JavaScript bridge is unavailable until WebView creates a document.
            }
        }

        if (!cancellationToken.IsCancellationRequested)
            RevealPortal();
    }

    private void RevealPortal() => MainThread.BeginInvokeOnMainThread(() =>
    {
        LoadingLayer.IsVisible = false;
        OfflineLayer.IsVisible = !HasInternetAccess();
    });

    private static bool HasInternetAccess() =>
        Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
}
