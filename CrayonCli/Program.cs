// CrayonCli — an API that asks Jev which of the 8 classic crayon colors an image is closest to.
// Usage:   dotnet run, then: curl -F "image=@path/to/image.jpg" <server-url>/crayon
// Env:     TYPESAFE_API_KEY (from the environment or a .env file)

using DotNetEnv;
using ImageMagick;
using TypeSafeAI;

Env.TraversePath().NoClobber().Load(); // real environment variables win over .env

var apiKey = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException("Set TYPESAFE_API_KEY first (in the environment or a .env file).");

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 20 * 1024 * 1024);
builder.Services.AddSingleton(_ => new TypeSafeClient(apiKey));
builder.Services.AddSingleton<ICrayonClassifier, JevClassifier>();

var app = builder.Build();

app.MapPost("/crayon", async (IFormFile image, ICrayonClassifier classifier, ILogger<Program> log, CancellationToken cancellationToken) =>
{
    try
    {
        using var buffer = new MemoryStream();
        await image.CopyToAsync(buffer, cancellationToken);

        var png = ImagePreparer.Prepare(buffer.ToArray());
        var result = await classifier.ClassifyAsync(png, cancellationToken);

        return Results.Ok(result with
        {
            Probabilities = result.Probabilities.OrderByDescending(p => p.Probability).ToList(),
        });
    }
    catch (Exception ex) when (ex is MagickException or BadImageException)
    {
        return Results.Problem(ex.Message, statusCode: 400);
    }
    catch (ClassifierException ex)
    {
        log.LogError(ex, "TypeSafe API call failed");
        return Results.Problem("Upstream model error.", statusCode: 502);
    }
}).DisableAntiforgery();

app.Run();

public partial class Program; // lets WebApplicationFactory<Program> reference it
