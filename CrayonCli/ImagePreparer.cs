using ImageMagick;

public class BadImageException(string message) : Exception(message);

public static class ImagePreparer
{
    private const int MinSide = 280;   // docs disagree (256 vs 280); 280 is the safe floor
    private const int MaxSide = 1568;

    // Validates an untrusted upload, fixes EXIF rotation, fits it within Jev's size limits
    // and re-encodes it as PNG.
    public static byte[] Prepare(byte[] input, ulong maxPixels = 50_000_000)
    {
        var info = new MagickImageInfo(input); // reads the header only
        if (info.Format is not (MagickFormat.Png or MagickFormat.Jpeg or MagickFormat.WebP
                                or MagickFormat.Gif or MagickFormat.Bmp))
            throw new BadImageException($"Unsupported image format: {info.Format}.");
        if ((ulong)info.Width * info.Height > maxPixels)
            throw new BadImageException("Image has too many pixels.");

        using var image = new MagickImage(input);
        image.AutoOrient(); // Jev ignores EXIF orientation

        uint w = image.Width, h = image.Height;
        double scale = 1.0;
        if (Math.Max(w, h) > MaxSide) scale = (double)MaxSide / Math.Max(w, h);
        else if (Math.Min(w, h) < MinSide) scale = (double)MinSide / Math.Min(w, h);

        if (scale != 1.0)
        {
            w = (uint)Math.Max(1, Math.Round(w * scale));
            h = (uint)Math.Max(1, Math.Round(h * scale));
        }

        if (Math.Min(w, h) < MinSide || Math.Max(w, h) > MaxSide)
            throw new BadImageException(
                $"Aspect ratio too extreme to fit {MinSide}–{MaxSide}px on both sides ({w}x{h}). Crop it first.");

        if (scale != 1.0)
            image.Resize(w, h);

        // PNG avoids adding JPEG artifacts on re-encode; the 16 MiB cap is far away at these sizes.
        image.Format = MagickFormat.Png;
        return image.ToByteArray();
    }
}
