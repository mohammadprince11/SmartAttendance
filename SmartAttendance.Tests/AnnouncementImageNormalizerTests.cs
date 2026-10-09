using SkiaSharp;
using SmartAttendance.Web.Infrastructure;

namespace SmartAttendance.Tests;

public sealed class AnnouncementImageNormalizerTests
{
    private static byte[] Make(int width, int height, SKEncodedImageFormat format, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }
    [Fact] public async Task LargeJpegIsReducedWithoutCropping()
    {
        var result = await AnnouncementImageNormalizer.NormalizeAsync(Make(9000, 4500, SKEncodedImageFormat.Jpeg, SKColors.Coral));
        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Equal(2000, result.Width); Assert.Equal(1000, result.Height);
        Assert.True(result.Data.Length < AnnouncementImageNormalizer.MaxFileBytes);
        Assert.Equal("image/jpeg", SmartAttendance.Web.Pages.Engagement.StudioModel.DetectImage(result.Data));
    }
    [Fact] public async Task SmallPngKeepsDimensionsAndTransparency()
    {
        var result = await AnnouncementImageNormalizer.NormalizeAsync(Make(100, 50, SKEncodedImageFormat.Png, SKColors.Transparent));
        Assert.Equal(100, result.Width); Assert.Equal(50, result.Height);
        Assert.Equal("image/png", result.ContentType);
        using var decoded = SKBitmap.Decode(result.Data);
        Assert.Equal(0, decoded.GetPixel(0, 0).Alpha);
    }
    [Fact] public async Task TallImageKeepsAspectRatio()
    {
        var result = await AnnouncementImageNormalizer.NormalizeAsync(Make(1200, 3600, SKEncodedImageFormat.Png, SKColors.Blue));
        Assert.Equal(667, result.Width); Assert.Equal(2000, result.Height);
    }
    [Fact] public async Task RejectsMalformedOrOversizedFiles()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => AnnouncementImageNormalizer.NormalizeAsync(new byte[100]));
        await Assert.ThrowsAsync<InvalidDataException>(() => AnnouncementImageNormalizer.NormalizeAsync(new byte[AnnouncementImageNormalizer.MaxFileBytes + 1]));
        var valid = Make(100, 100, SKEncodedImageFormat.Jpeg, SKColors.Red);
        await Assert.ThrowsAsync<InvalidDataException>(() => AnnouncementImageNormalizer.NormalizeAsync(valid[..(valid.Length / 2)]));
    }
    [Fact] public async Task CancellationDoesNotHoldDecodeGate()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AnnouncementImageNormalizer.NormalizeAsync(Make(100, 100, SKEncodedImageFormat.Jpeg, SKColors.Red), cancellation.Token));
        Assert.NotNull(await AnnouncementImageNormalizer.NormalizeAsync(Make(100, 100, SKEncodedImageFormat.Jpeg, SKColors.Red)));
    }
    [Fact] public async Task SuppliedImageOptionalVerification()
    {
        var path = Environment.GetEnvironmentVariable("ZYNORA_IMAGE_UPLOAD_TEST");
        if (string.IsNullOrWhiteSpace(path)) return;
        var original = await File.ReadAllBytesAsync(path);
        var result = await AnnouncementImageNormalizer.NormalizeAsync(original);
        Assert.Equal(2000, result.Width); Assert.Equal(2000, result.Height);
        Assert.Equal("image/jpeg", result.ContentType);
        Assert.True(result.Data.Length < original.Length);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Console.WriteLine($"Verified user image: {original.Length} bytes -> {result.Data.Length} bytes, {result.Width}x{result.Height}");
    }
}
