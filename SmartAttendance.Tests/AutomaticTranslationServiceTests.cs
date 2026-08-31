using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SmartAttendance.Web.Infrastructure.Localization;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class AutomaticTranslationServiceTests
{
    [Fact]
    public void TranslatorLanguage_MapsSoraniToAzureCentralKurdish()
    {
        Assert.Equal("ku", AzureAutomaticTextTranslator.ResolveTranslatorLanguage("ckb-IQ"));
        Assert.Equal("en", AzureAutomaticTextTranslator.ResolveTranslatorLanguage("en-US"));
    }

    [Fact]
    public async Task AzureTranslator_SendsConfiguredHeadersAndPreservesRuntimeTokens()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(async request =>
        {
            captured = request;
            var body = await request.Content!.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);
            var masked = document.RootElement[0].GetProperty("Text").GetString()!;
            Assert.Contains("__ZYNORA_TOKEN_0000__", masked, StringComparison.Ordinal);
            Assert.Contains("__ZYNORA_TOKEN_0001__", masked, StringComparison.Ordinal);

            var translated = masked.Replace("الموظف", "Employee", StringComparison.Ordinal);
            var json = JsonSerializer.Serialize(new[]
            {
                new { translations = new[] { new { text = translated, to = "en" } } }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });
        var options = Options.Create(new AzureTranslatorOptions
        {
            Endpoint = "https://api.cognitive.microsofttranslator.com",
            SubscriptionKey = "unit-test-key",
            Region = "unit-test-region"
        });
        var service = new AzureAutomaticTextTranslator(new HttpClient(handler), options);

        var result = await service.TranslateAsync(
            ["الموظف {0} في https://example.test/profile"],
            "en-US");

        Assert.Single(result);
        Assert.Equal("Employee {0} في https://example.test/profile", result[0]);
        Assert.NotNull(captured);
        Assert.Contains("api-version=3.0", captured!.RequestUri!.Query, StringComparison.Ordinal);
        Assert.Contains("to=en", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.True(captured.Headers.TryGetValues("Ocp-Apim-Subscription-Key", out var keys));
        Assert.Equal("unit-test-key", Assert.Single(keys));
        Assert.True(captured.Headers.TryGetValues("Ocp-Apim-Subscription-Region", out var regions));
        Assert.Equal("unit-test-region", Assert.Single(regions));
    }

    [Fact]
    public async Task AzureTranslator_RefusesUseWithoutASecret()
    {
        var service = new AzureAutomaticTextTranslator(
            new HttpClient(new StubHandler(_ => throw new InvalidOperationException("HTTP must not run"))),
            Options.Create(new AzureTranslatorOptions()));

        Assert.False(service.IsConfigured);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.TranslateAsync(["اختبار"], "en-US"));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => _handler(request);
    }
}
