using SkiaSharp;

namespace SmartAttendance.Web.Infrastructure;

/// <summary>Decodes and re-encodes design uploads; never stores unchecked source bytes.</summary>
public static class AnnouncementImageNormalizer
{
    public const int MaxFileBytes = 5 * 1024 * 1024;
    public const int MaxDisplayEdge = 2000;
    private static readonly SemaphoreSlim DecodeGate = new(1, 1);
    public sealed record Result(byte[] Data, string ContentType, int Width, int Height);

    public static async Task<Result> NormalizeAsync(byte[] source, CancellationToken cancellationToken = default)
    {
        if (source.Length is < 24 or > MaxFileBytes)
            throw new InvalidDataException("اختر صورة PNG أو JPEG بحد أقصى 5 ميغابايت.");
        await DecodeGate.WaitAsync(cancellationToken);
        try
        {
            using var data = SKData.CreateCopy(source);
            using var codec = SKCodec.Create(data);
            if (codec == null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png))
                throw new InvalidDataException("الصورة غير صالحة. المسموح PNG أو JPEG فقط.");
            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0 || info.Width > 20000 || info.Height > 20000 || (long)info.Width * info.Height > 100_000_000 || codec.FrameCount > 1)
                throw new InvalidDataException("أبعاد الصورة أكبر من الحد الآمن (100 مليون بكسل)، أو الصورة متحركة. صغّرها وأعد رفعها.");
            cancellationToken.ThrowIfCancellationRequested();
            // JPEG codecs can downsample while decoding, reducing peak memory for large photographs.
            var scale = Math.Min(1f, (float)MaxDisplayEdge / Math.Max(info.Width, info.Height));
            var decodedSize = codec.GetScaledDimensions(scale);
            using var bitmap = new SKBitmap(new SKImageInfo(decodedSize.Width, decodedSize.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
                throw new InvalidDataException("تعذّر قراءة الصورة كاملة. تأكد أن الملف غير تالف وأعد رفعه.");
            cancellationToken.ThrowIfCancellationRequested();
            var rotated = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
            var edge = MaxDisplayEdge;
            while (true)
            {
                var factor = Math.Min(1d, (double)edge / Math.Max(bitmap.Width, bitmap.Height));
                var width = Math.Max(1, (int)Math.Round(bitmap.Width * factor));
                var height = Math.Max(1, (int)Math.Round(bitmap.Height * factor));
                var outputWidth = rotated ? height : width;
                var outputHeight = rotated ? width : height;
                using var output = new SKBitmap(outputWidth, outputHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
                using (var canvas = new SKCanvas(output))
                using (var paint = new SKPaint { IsAntialias = true })
                {
                    canvas.Clear(SKColors.Transparent);
                    switch (codec.EncodedOrigin)
                    {
                        case SKEncodedOrigin.TopRight: canvas.Translate(width, 0); canvas.Scale(-1, 1); break;
                        case SKEncodedOrigin.BottomRight: canvas.Translate(width, height); canvas.RotateDegrees(180); break;
                        case SKEncodedOrigin.BottomLeft: canvas.Translate(0, height); canvas.Scale(1, -1); break;
                        case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
                        case SKEncodedOrigin.RightTop: canvas.Translate(height, 0); canvas.RotateDegrees(90); break;
                        case SKEncodedOrigin.RightBottom: canvas.Translate(height, width); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
                        case SKEncodedOrigin.LeftBottom: canvas.Translate(0, width); canvas.RotateDegrees(270); break;
                    }
                    using var sourceImage = SKImage.FromBitmap(bitmap);
                    canvas.DrawImage(sourceImage, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), paint);
                }
                using var image = SKImage.FromBitmap(output);
                using var encoded = image.Encode(codec.EncodedFormat, 88);
                if (encoded == null) throw new InvalidDataException("تعذّر تجهيز الصورة للعرض. أعد حفظها كـ PNG أو JPEG.");
                if (encoded.Size <= MaxFileBytes)
                    return new(encoded.ToArray(), codec.EncodedFormat == SKEncodedImageFormat.Png ? "image/png" : "image/jpeg", outputWidth, outputHeight);
                edge /= 2;
                if (edge < 250) throw new InvalidDataException("حجم الصورة بعد المعالجة أكبر من الحد المسموح.");
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        finally { DecodeGate.Release(); }
    }
}
