using Android.OS;
using AndroidX.Credentials;
using AndroidX.Credentials.Exceptions;

#pragma warning disable CA1416

namespace ZynoraHR.Mobile;

public static class AndroidPasskeyRegistration
{
    public static Task<string> CreateAsync(string requestJson)
    {
        if (string.IsNullOrWhiteSpace(requestJson))
            throw new ArgumentException(
                "بيانات تسجيل المفتاح غير صالحة.",
                nameof(requestJson));

        var activity = Platform.CurrentActivity
            ?? throw new InvalidOperationException(
                "لا توجد شاشة Android نشطة.");

        var manager = CredentialManager.Create(activity);
        var request = new CreatePublicKeyCredentialRequest(requestJson);
        var completion = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var callback = new CreateCallback(completion);
        manager.CreateCredentialAsync(
            activity,
            request,
            new CancellationSignal(),
            activity.MainExecutor!,
            callback);

        return completion.Task;
    }

    private sealed class CreateCallback(
        TaskCompletionSource<string> completion)
        : Java.Lang.Object, ICredentialManagerCallback
    {
        public void OnResult(Java.Lang.Object? result)
        {
            if (result is CreatePublicKeyCredentialResponse response &&
                !string.IsNullOrWhiteSpace(response.RegistrationResponseJson))
            {
                completion.TrySetResult(response.RegistrationResponseJson);
                return;
            }

            completion.TrySetException(
                new InvalidOperationException(
                    "لم يرجع Android استجابة تسجيل Passkey صالحة."));
        }

        public void OnError(Java.Lang.Object error)
        {
            var detail = error?.ToString();
            completion.TrySetException(
                new InvalidOperationException(
                    string.IsNullOrWhiteSpace(detail)
                        ? "تعذر إنشاء مفتاح البصمة/الوجه."
                        : detail));
        }
    }
}
