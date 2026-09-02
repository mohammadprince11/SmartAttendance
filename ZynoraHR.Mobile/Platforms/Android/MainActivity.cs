using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Webkit;

namespace ZynoraHR.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
	ScreenOrientation = ScreenOrientation.Portrait,
	ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);
		CookieManager.Instance?.SetAcceptCookie(true);
	}

	protected override void OnPause()
	{
		// Android writes WebView cookies asynchronously. Flush the persistent
		// authentication cookie before the app can be killed in the background.
		CookieManager.Instance?.Flush();
		base.OnPause();
	}
}
