# Convert CrayonCli to a .NET API

**Status:** Implemented, not yet committed. The code below is the original proposal, so where they differ, the source is correct. Differences:

- `ClassifyAsync` also takes a `CancellationToken`, which the endpoint passes from the request.
- `JevClassifier.cs` aliases `JsonContent` to the TypeSafe type, because the Web SDK also imports `System.Net.Http.Json`.
- `test-images.sh` prints any server errors after its summary.

Outcomes of the checks listed under "To check during implementation":

- **Client sharing:** four concurrent requests through the singleton all succeeded. The SDK docs don't state that the client is thread-safe.
- **Response values:** confirmed in live runs, so the `VERIFY` markers are gone.
- **Cancellation:** `SystemOneAsync` accepts a token, and it's now passed through. Cancelling on client disconnect hasn't been tested.
- **Error statuses:** all TypeSafe errors stay `502`, even though `TypeSafeApiException` exposes `StatusCode`. TypeSafe's `400` can also mean a missing opt-in or context overflow, which aren't the caller's fault.

## Goal

Replace the CLI with an ASP.NET Core Minimal API and add a unit test project. The API has one endpoint, `POST /crayon`, which accepts an image upload and returns the closest crayon color and the probability for each color as JSON.

The Jev call stays largely the same. The input, output and error handling change, and the code is split up so it can be tested without calling TypeSafe.

## Decisions

- **Convert in place:** the API replaces the CLI. If the CLI needs to stay, move the image and Jev code into a shared class library, with the CLI and API as thin wrappers around it.
- **Upload format:** `multipart/form-data` with the file in a field named `image`.
- **Untrusted input:** the API accepts files from anyone, so uploads are limited by request size, image format and pixel count.
- **Upstream errors:** the full TypeSafe error goes to the log, and the caller gets a generic `502`.
- **Test boundary:** the Jev call sits behind `ICrayonClassifier`, so automated tests replace it with a fake and never spend API credits.
- **Test tools:** xUnit, plus `WebApplicationFactory` for endpoint tests.
- **Test images:** tests generate images in memory, so they don't depend on `test-files/`.
- **Live test:** `test-images.sh` stays as the only check of real Jev behavior.

## API contract

```
POST /crayon
Content-Type: multipart/form-data
Field: image
```

Example success response, with illustrative numbers:

```json
{
  "closest": "red",
  "confidence": 0.94,
  "probabilities": [
    { "color": "red", "probability": 0.94 },
    { "color": "orange", "probability": 0.03 }
  ]
}
```

| Status | Meaning | Replaces |
|---|---|---|
| 200 | Color picked | Exit code 0 |
| 400 | Invalid, unsupported or oversized image | Exit code 1 |
| 502 | TypeSafe API error | Exit code 2 |

A missing `TYPESAFE_API_KEY` stops the app at startup instead of returning an error per request.

## Project layout

```
CrayonCli/
  Program.cs          # startup and the endpoint
  ImagePreparer.cs    # validation and normalization
  JevClassifier.cs    # ICrayonClassifier, result types and the Jev call
CrayonCli.Tests/      # a sibling, so the API project doesn't compile the test files
  ImagePreparerTests.cs
  CrayonEndpointTests.cs
```

## Steps

### 1. Update the project

In `CrayonCli.csproj`, change the SDK to `Microsoft.NET.Sdk.Web` and remove the `AssemblyName` line. Optionally, rename the project to `CrayonApi`.

### 2. Move image handling to `ImagePreparer.cs`

Take bytes instead of a path, and check the file before decoding it. The pixel limit is a parameter so a test can trigger it without allocating a huge image.

```csharp
using ImageMagick;

public class BadImageException(string message) : Exception(message);

public static class ImagePreparer
{
    private const int MinSide = 280;
    private const int MaxSide = 1568;

    public static byte[] Prepare(byte[] input, ulong maxPixels = 50_000_000)
    {
        var info = new MagickImageInfo(input); // reads the header only
        if (info.Format is not (MagickFormat.Png or MagickFormat.Jpeg or MagickFormat.WebP
                                or MagickFormat.Gif or MagickFormat.Bmp))
            throw new BadImageException($"Unsupported image format: {info.Format}.");
        if ((ulong)info.Width * info.Height > maxPixels)
            throw new BadImageException("Image has too many pixels.");

        using var image = new MagickImage(input);
        // The rest matches the current PrepareImage, except that the aspect-ratio
        // error now throws BadImageException instead of InvalidOperationException.
    }
}
```

The format allowlist stops ImageMagick from parsing riskier formats like SVG. The pixel cap blocks small files that expand into huge images.

### 3. Move the Jev call to `JevClassifier.cs`

The classifier converts `TypeSafeApiException` to its own exception type, so the endpoint and its tests don't depend on the SDK's exception constructor.

```csharp
using System.Text.Json.Nodes;
using TypeSafeAI;

public record ColorProbability(string Color, double Probability);
public record CrayonResult(string Closest, double Confidence, IReadOnlyList<ColorProbability> Probabilities);

public class ClassifierException(Exception inner) : Exception("Classifier call failed.", inner);

public interface ICrayonClassifier
{
    Task<CrayonResult> ClassifyAsync(byte[] png);
}

public class JevClassifier(TypeSafeClient client) : ICrayonClassifier
{
    private const string Model = "jev-1.14.0";

    public async Task<CrayonResult> ClassifyAsync(byte[] png)
    {
        var swatch = ImageAsset.FromBytes(png, "png");

        var options = new RequestOptions();
        options.Images.Add(swatch.Reference, swatch);

        var state = JsonContent.FromNode(new JsonObject
        {
            ["task"] = "Pick the crayon color closest to the dominant color of `swatch`.",
            ["swatch"] = swatch.Reference,
        });

        var questions = new QuestionSet();
        var crayon = questions.AddChoice("closest_crayon", new ChoiceQuestion(
            new Dictionary<string, JsonContent?>
            {
                ["red"] = "Red",
                ["orange"] = "Orange",
                ["yellow"] = "Yellow",
                ["green"] = "Green",
                ["blue"] = "Blue",
                ["violet"] = "Violet / purple",
                ["brown"] = "Brown",
                ["black"] = "Black",
            }));

        try
        {
            var result = await client.SystemOneAsync(state, questions, Model, options);
            var answer = result.Get(crayon);

            return new CrayonResult(
                answer.Choice,
                answer.Confidence,
                answer.Probabilities.Select(kv => new ColorProbability(kv.Key, kv.Value)).ToList());
        }
        catch (TypeSafeApiException ex)
        {
            throw new ClassifierException(ex);
        }
    }
}
```

### 4. Replace `Program.cs`

```csharp
using DotNetEnv;
using ImageMagick;
using TypeSafeAI;

Env.TraversePath().NoClobber().Load();

var apiKey = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException("Set TYPESAFE_API_KEY first.");

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 20 * 1024 * 1024);
builder.Services.AddSingleton(_ => new TypeSafeClient(apiKey));
builder.Services.AddSingleton<ICrayonClassifier, JevClassifier>();

var app = builder.Build();

app.MapPost("/crayon", async (IFormFile image, ICrayonClassifier classifier, ILogger<Program> log) =>
{
    try
    {
        using var buffer = new MemoryStream();
        await image.CopyToAsync(buffer);

        var result = await classifier.ClassifyAsync(ImagePreparer.Prepare(buffer.ToArray()));

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
```

### 5. Add the test project

Run from the repo root:

```bash
dotnet new xunit -o CrayonCli.Tests
dotnet add CrayonCli.Tests reference CrayonCli
dotnet add CrayonCli.Tests package Microsoft.AspNetCore.Mvc.Testing
dotnet add CrayonCli.Tests package Magick.NET-Q8-AnyCPU
```

Set the test project's target framework to `net10.0` if the template picks something else.

The test factory swaps in the fake classifier. It also sets a dummy API key, which takes precedence over any real key in `.env` because `.env` values don't overwrite existing variables:

```csharp
public class CrayonApiFactory : WebApplicationFactory<Program>
{
    public FakeClassifier Classifier { get; } = new();

    public CrayonApiFactory() =>
        Environment.SetEnvironmentVariable("TYPESAFE_API_KEY", "test-key");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(s => s.AddSingleton<ICrayonClassifier>(Classifier));
    }
}
```

`FakeClassifier` records the bytes it receives and returns a configured result or throws a configured exception.

### 6. Write the tests

**`ImagePreparerTests`**

| Case | Expected result |
|---|---|
| Image within limits | Same dimensions, output is PNG |
| Image over 1,568 px | Longest side scaled to 1,568 px |
| Image under 280 px | Shortest side scaled to 280 px |
| Non-square image that gets resized | Aspect ratio kept, within rounding |
| EXIF-rotated image | Width and height match the corrected orientation |
| Aspect ratio too extreme (for example, 2000x100) | `BadImageException` |
| Allowed formats (PNG, JPEG, WebP, GIF, BMP) | Accepted |
| Disallowed format (for example, TIFF) | `BadImageException` |
| Pixel count over `maxPixels` | `BadImageException` |
| Non-image bytes | `MagickException` |

**`CrayonEndpointTests`**

| Case | Expected result |
|---|---|
| Valid image | `200` with the JSON shape above |
| Unsorted probabilities from the classifier | Sorted highest first in the response |
| Valid image | Classifier receives a PNG |
| Non-image file | `400`, classifier not called |
| SVG file | `400`, classifier not called |
| Aspect ratio too extreme | `400` with the aspect-ratio message, classifier not called |
| No `image` field | `400`, classifier not called |
| Classifier throws `ClassifierException` | `502`, and the inner error's message isn't in the response |

### 7. Update `test-images.sh`

The script currently parses the `Closest crayon:` line from the CLI output. Change it to:

1. Start the server, or require one to be running.
2. Upload each image with `curl -F "image=@<file>"`.
3. Read the pick with `jq -r .closest`.
4. Keep comparing the pick to the filename.

Each image still costs one API call.

### 8. Update `CLAUDE.md`

Update the overview, commands, flow, exit codes and the output format note under "Changes that span files". Replace the statement that there's no unit test project, and add `dotnet test CrayonCli.Tests` to the commands, noting that it makes no API calls.

## Verification

Automated tests, with no API key or API calls needed:

```bash
dotnet test CrayonCli.Tests
```

Live checks, run from the project directory with a real API key:

```bash
dotnet run
curl -F "image=@test-files/red.jpg" http://localhost:5000/crayon
./test-images.sh
```

Use the port shown in the `dotnet run` output, as `launchSettings.json` may change it.

These cases need a live or manual check because the automated tests can't cover them:

| Case | Why it's manual | Expected result |
|---|---|---|
| All test images | Needs the real model | Script reports every pick as correct |
| Invalid API key | Needs the real SDK | `502`, with the detail only in the log |
| Upload over 20 MiB | `WebApplicationFactory` uses its test server, not Kestrel, so the limit doesn't apply | Rejected, and no TypeSafe call made; confirm the status code |
| No `TYPESAFE_API_KEY` | The check runs at app startup | App fails at startup |

## To check during implementation

- **Client sharing:** the plan assumes `TypeSafeClient` is safe to share across requests. If it isn't, create one per request.
- **Response values:** the `VERIFY` lines still apply, and now they also determine the JSON. `CrayonResult` assumes `answer.Choice` is a string, `answer.Confidence` is a `double` and `answer.Probabilities` is a dictionary of strings to `double`. Unit tests use the fake classifier, so only a live run can confirm these.
- **Cancellation:** if `SystemOneAsync` accepts a `CancellationToken`, add one to `ClassifyAsync` and pass in the request's token so that a client disconnect cancels the Jev call.
- **Error statuses:** TypeSafe can return `400` or `413` for a bad image, and this plan reports both as `502`. If `TypeSafeApiException` exposes the status code, consider returning those as caller errors.

## Required before public deployment

- Add authentication, because every request spends TypeSafe credits.
- Add rate limiting with `AddRateLimiter`.

## Out of scope

- Hosting and containerization
- Batch uploads
