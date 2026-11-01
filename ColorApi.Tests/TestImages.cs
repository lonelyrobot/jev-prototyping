using ImageMagick;

static class TestImages
{
    public static byte[] Create(uint width, uint height, MagickFormat format = MagickFormat.Png,
        Orientation orientation = Orientation.TopLeft)
    {
        using var image = new MagickImage(MagickColors.Red, width, height);
        if (orientation != Orientation.TopLeft)
        {
            // JPEG stores orientation in EXIF, so the image needs a profile to carry it.
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)orientation);
            image.SetProfile(exif);
            image.Orientation = orientation;
        }
        image.Format = format;
        return image.ToByteArray();
    }

    public static MagickImageInfo Read(byte[] bytes) => new(bytes);
}
