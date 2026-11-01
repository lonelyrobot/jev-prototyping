using System.Text;
using ImageMagick;

public class ImagePreparerTests
{
    [Fact]
    public void Image_within_limits_keeps_its_size_and_becomes_png()
    {
        var output = TestImages.Read(ImagePreparer.Prepare(TestImages.Create(400, 300, MagickFormat.Jpeg)));

        Assert.Equal(MagickFormat.Png, output.Format);
        Assert.Equal(400u, output.Width);
        Assert.Equal(300u, output.Height);
    }

    [Fact]
    public void Large_image_is_scaled_down_to_the_maximum_side()
    {
        var output = TestImages.Read(ImagePreparer.Prepare(TestImages.Create(3136, 2000)));

        Assert.Equal(1568u, output.Width);
        Assert.Equal(1000u, output.Height);
    }

    [Fact]
    public void Small_image_is_scaled_up_to_the_minimum_side()
    {
        var output = TestImages.Read(ImagePreparer.Prepare(TestImages.Create(280, 140)));

        Assert.Equal(560u, output.Width);
        Assert.Equal(280u, output.Height);
    }

    [Fact]
    public void Exif_rotated_image_is_auto_oriented()
    {
        var input = TestImages.Create(400, 300, MagickFormat.Jpeg, Orientation.RightTop);
        using (var source = new MagickImage(input))
            Assert.Equal(Orientation.RightTop, source.Orientation); // the test image really is rotated

        var output = TestImages.Read(ImagePreparer.Prepare(input));

        Assert.Equal(300u, output.Width);
        Assert.Equal(400u, output.Height);
    }

    [Theory]
    [InlineData(2000u, 100u)]
    [InlineData(100u, 2000u)]
    public void Extreme_aspect_ratio_is_rejected(uint width, uint height)
    {
        var ex = Assert.Throws<BadImageException>(() => ImagePreparer.Prepare(TestImages.Create(width, height)));

        Assert.Contains("Aspect ratio", ex.Message);
    }

    [Theory]
    [InlineData(MagickFormat.Png)]
    [InlineData(MagickFormat.Jpeg)]
    [InlineData(MagickFormat.WebP)]
    [InlineData(MagickFormat.Gif)]
    [InlineData(MagickFormat.Bmp)]
    public void Allowed_formats_are_accepted(MagickFormat format)
    {
        var output = TestImages.Read(ImagePreparer.Prepare(TestImages.Create(400, 300, format)));

        Assert.Equal(MagickFormat.Png, output.Format);
    }

    [Fact]
    public void Disallowed_format_is_rejected()
    {
        var ex = Assert.Throws<BadImageException>(
            () => ImagePreparer.Prepare(TestImages.Create(400, 300, MagickFormat.Tiff)));

        Assert.Contains("Unsupported image format", ex.Message);
    }

    [Fact]
    public void Too_many_pixels_is_rejected()
    {
        var ex = Assert.Throws<BadImageException>(
            () => ImagePreparer.Prepare(TestImages.Create(400, 300), maxPixels: 119_999));

        Assert.Contains("too many pixels", ex.Message);
    }

    [Fact]
    public void Exactly_the_maximum_pixels_is_accepted()
    {
        ImagePreparer.Prepare(TestImages.Create(400, 300), maxPixels: 120_000);
    }

    [Fact]
    public void Non_image_bytes_are_rejected()
    {
        Assert.ThrowsAny<MagickException>(() => ImagePreparer.Prepare(Encoding.UTF8.GetBytes("not an image")));
    }
}
