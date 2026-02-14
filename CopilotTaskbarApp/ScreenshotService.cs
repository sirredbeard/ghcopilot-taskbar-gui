using CopilotTaskbarApp.Native;
using System.Drawing;
using System.Drawing.Imaging;

namespace CopilotTaskbarApp;

public class ScreenshotService
{
    // Captures the primary screen and returns it as a Base64 JPEG string
    // Returns null on failure
    public string? CaptureScreenBase64()
    {
        try
        {
            var bounds = NativeHelpers.GetVirtualScreenBounds();

            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using var g = Graphics.FromImage(bitmap);

            // Efficiently copy screen content
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size);
            // Resize if too large to save tokens/bandwidth
            // Target max dimension ~1024px to keep payload size reasonable for LLM
            var resized = ResizeBitmap(bitmap, 1024, 1024);
            
            using var ms = new MemoryStream();
            // Save as JPEG with quality 75 to compress
            // PNG is lossless but often results in very large Base64 strings for screenshots
            var encoder = GetEncoder(ImageFormat.Jpeg);
            var qualityParam = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 75L);
            var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = qualityParam;

            resized.Save(ms, encoder, encoderParams);
            
            byte[] byteImage = ms.ToArray();
            return Convert.ToBase64String(byteImage);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Screenshot failed: {ex.Message}");
            return null;
        }
    }

    private Bitmap ResizeBitmap(Bitmap original, int maxWidth, int maxHeight)
    {
        // Calculate new size
        var ratioX = (double)maxWidth / original.Width;
        var ratioY = (double)maxHeight / original.Height;
        var ratio = Math.Min(ratioX, ratioY);

        // If already smaller, return original clone
        if (ratio >= 1.0) return new Bitmap(original);

        var newWidth = (int)(original.Width * ratio);
        var newHeight = (int)(original.Height * ratio);

        var newBitmap = new Bitmap(newWidth, newHeight);
        using var g = Graphics.FromImage(newBitmap);
        
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(original, 0, 0, newWidth, newHeight);

        return newBitmap;
    }

    private ImageCodecInfo GetEncoder(ImageFormat format)
    {
        var codecs = ImageCodecInfo.GetImageDecoders();
        foreach (var codec in codecs)
        {
            if (codec.FormatID == format.Guid)
            {
                return codec;
            }
        }
        return codecs[0]; // Fallback
    }
}
