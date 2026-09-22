using Android.App;
using Android.Content;
using Android.OS;

#pragma warning disable CA1416
#pragma warning disable CA1422

namespace ZynoraHR.Mobile;

public static class DeviceBiometricAuth
{
    public static bool IsAvailable()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.P)
            return false;

        var activity = Platform.CurrentActivity;
        if (activity is null)
            return false;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
        {
            var manager = activity.GetSystemService(Context.BiometricService)
                as Android.Hardware.Biometrics.BiometricManager;
            return manager?.CanAuthenticate() ==
                Android.Hardware.Biometrics.BiometricCode.Success;
        }

        return true;
    }

    public static Task<bool> AuthenticateAsync(
        string title,
        string subtitle)
    {
        if (!IsAvailable())
            return Task.FromResult(false);

        var activity = Platform.CurrentActivity;
        if (activity is null)
            return Task.FromResult(false);

        var tcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        activity.RunOnUiThread(() =>
        {
            try
            {
                var executor = activity.MainExecutor!;
                var negative = new NegativeClickListener(tcs);
                var prompt = new Android.Hardware.Biometrics.BiometricPrompt.Builder(activity)
                    .SetTitle(title)
                    .SetSubtitle(subtitle)
                    .SetNegativeButton("إلغاء", executor, negative)
                    .Build();

                prompt.Authenticate(
                    new CancellationSignal(),
                    executor,
                    new AuthenticationCallback(tcs));
            }
            catch
            {
                tcs.TrySetResult(false);
            }
        });

        return tcs.Task;
    }

    private sealed class AuthenticationCallback(
        TaskCompletionSource<bool> tcs)
        : Android.Hardware.Biometrics.BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(
            Android.Hardware.Biometrics.BiometricPrompt.AuthenticationResult? result)
        {
            tcs.TrySetResult(true);
        }

        public override void OnAuthenticationError(
            Android.Hardware.Biometrics.BiometricErrorCode errorCode,
            Java.Lang.ICharSequence? errString)
        {
            tcs.TrySetResult(false);
        }
    }

    private sealed class NegativeClickListener(
        TaskCompletionSource<bool> tcs)
        : Java.Lang.Object, IDialogInterfaceOnClickListener
    {
        public void OnClick(IDialogInterface? dialog, int which)
        {
            tcs.TrySetResult(false);
        }
    }
}
