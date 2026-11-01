using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ImageMagick;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

public class FakeClassifier : IColorClassifier
{
    public byte[]? Received { get; private set; }
    public Exception? ThrowOnCall { get; set; }

    public ColorResult Result { get; set; } = new("red", 0.9,
    [
        new("blue", 0.02),
        new("red", 0.9),
        new("orange", 0.08),
    ]);

    public void Reset()
    {
        Received = null;
        ThrowOnCall = null;
    }

    public Task<ColorResult> ClassifyAsync(byte[] png, CancellationToken cancellationToken)
    {
        Received = png;
        return ThrowOnCall is null ? Task.FromResult(Result) : throw ThrowOnCall;
    }
}

public class ColorApiFactory : WebApplicationFactory<Program>
{
    public FakeClassifier Classifier { get; } = new();

    // .env values don't overwrite existing variables, so a real key in .env is never used.
    public ColorApiFactory() =>
        Environment.SetEnvironmentVariable("TYPESAFE_API_KEY", "test-key");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(s => s.AddSingleton<IColorClassifier>(Classifier));
    }
}

public class ColorEndpointTests : IClassFixture<ColorApiFactory>
{
    private readonly FakeClassifier _classifier;
    private readonly HttpClient _client;

    public ColorEndpointTests(ColorApiFactory factory)
    {
        _classifier = factory.Classifier;
        _classifier.Reset();
        _client = factory.CreateClient();
    }

    private Task<HttpResponseMessage> Upload(byte[] bytes, string fieldName = "image")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var form = new MultipartFormDataContent { { file, fieldName, "upload.bin" } };
        return _client.PostAsync("/color", form);
    }

    [Fact]
    public async Task Valid_image_returns_the_result_as_json()
    {
        var response = await Upload(TestImages.Create(400, 300));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("red", json.RootElement.GetProperty("closest").GetString());
        Assert.Equal(0.9, json.RootElement.GetProperty("confidence").GetDouble());
        var first = json.RootElement.GetProperty("probabilities")[0];
        Assert.Equal("red", first.GetProperty("color").GetString());
        Assert.Equal(0.9, first.GetProperty("probability").GetDouble());
    }

    [Fact]
    public async Task Probabilities_are_sorted_highest_first()
    {
        var response = await Upload(TestImages.Create(400, 300));

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var colors = json.RootElement.GetProperty("probabilities").EnumerateArray()
            .Select(p => p.GetProperty("color").GetString());
        Assert.Equal(["red", "orange", "blue"], colors);
    }

    [Fact]
    public async Task Classifier_receives_a_png()
    {
        await Upload(TestImages.Create(400, 300, MagickFormat.Jpeg));

        Assert.NotNull(_classifier.Received);
        Assert.Equal(MagickFormat.Png, TestImages.Read(_classifier.Received).Format);
    }

    [Fact]
    public async Task Non_image_file_returns_400()
    {
        var response = await Upload(Encoding.UTF8.GetBytes("not an image"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(_classifier.Received);
    }

    [Fact]
    public async Task Svg_file_returns_400()
    {
        var svg = """<svg xmlns="http://www.w3.org/2000/svg" width="400" height="300"><rect width="400" height="300" fill="red"/></svg>""";

        var response = await Upload(Encoding.UTF8.GetBytes(svg));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(_classifier.Received);
    }

    [Fact]
    public async Task Extreme_aspect_ratio_returns_400_with_the_message()
    {
        var response = await Upload(TestImages.Create(2000, 100));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Aspect ratio", await response.Content.ReadAsStringAsync());
        Assert.Null(_classifier.Received);
    }

    [Fact]
    public async Task Missing_image_field_returns_400()
    {
        var response = await Upload(TestImages.Create(400, 300), fieldName: "wrong");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(_classifier.Received);
    }

    [Fact]
    public async Task Classifier_failure_returns_502_without_the_inner_error()
    {
        _classifier.ThrowOnCall = new ClassifierException(new Exception("secret upstream detail"));

        var response = await Upload(TestImages.Create(400, 300));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("secret upstream detail", await response.Content.ReadAsStringAsync());
    }
}
