using Android.OS;
using AndroidX.Credentials;

#pragma warning disable CA1416

namespace ZynoraHR.Mobile;

public static class AndroidPasskeyAuthentication
{
    public static Task<string> GetAsync(string requestJson)
    {
        if (string.IsNullOrWhiteSpace(requestJson))
            throw new ArgumentException(
                "بيانات تأكيد المفتاح غير صالحة.",
                nameof(requestJson));

        var activity = Platform.CurrentActivity
            ?? throw new InvalidOperationException(
                "لا توجد شاشة Android نشطة.");

        var manager = CredentialManager.Create(activity);
        var option = new GetPublicKeyCredentialOption(requestJson);
        var request = new GetCredentialRequest(
            new List<CredentialOption> { option });

        var completion = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var callback = new GetCallback(completion);
        manager.GetCredentialAsync(
            activity,
            request,
            new CancellationSignal(),
            activity.MainExecutor!,
            callback);

        return completion.Task;
    }

    private sealed class GetCallback(
        TaskCompletionSource<string> completion)
        : Java.Lang.Object, ICredentialManagerCallback
    {
        public void OnResult(Java.Lang.Object? result)
        {
            if (result is GetCredentialResponse response &&
                response.Credential is PublicKeyCredential credential &&
                !string.IsNullOrWhiteSpace(credential.AuthenticationResponseJson))
            {
                completion.TrySetResult(credential.AuthenticationResponseJson);
                return;
            }

            completion.TrySetException(
                new InvalidOperationException(
                    "لم يرجع Android استجابة Passkey صالحة."));
        }

        public void OnError(Java.Lang.Object error)
        {
            var detail = error?.ToString();
            completion.TrySetException(
                new InvalidOperationException(
                    string.IsNullOrWhiteSpace(detail)
                        ? "تعذر تأكيد بصمة الوجه أو الأصبع."
                        : detail));
        }
    }
}
