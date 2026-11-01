// CrayonCli — ask Jev which of the 8 classic crayon colors an image is closest to.
// Usage:   dotnet run -- path/to/image.jpg
// Env:     TYPESAFE_API_KEY (from the environment or a .env file)
//
// Packages:
//   dotnet add package tryAGI.TypeSafeAI       (tryAGI community SDK)
//   dotnet add package Magick.NET-Q8-AnyCPU
//   dotnet add package DotNetEnv
//
// Lines marked VERIFY are educated guesses about the SDK surface; check them
// against https://tryagi.github.io/TypeSafeAI/ before relying on them.

using System.Text.Json.Nodes;
using DotNetEnv;
using ImageMagick;
using TypeSafeAI;

const string Model = "jev-1.14.0"; // pin a version; images also work on jev-1.13.0

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: crayon <image.png|image.jpg>");
    return 1;
}

Env.TraversePath().NoClobber().Load(); // real environment variables win over .env

var apiKey = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Set TYPESAFE_API_KEY first (in the environment or a .env file).");
    return 1;
}

try
{
    // 1. Normalize the image: fix EXIF rotation, fit within Jev's size limits, re-encode as PNG.
    var pngBytes = PrepareImage(args[0]);
    var swatch = ImageAsset.FromBytes(pngBytes, "png");

    // 2. Attach it. The SDK adds the TypeSafe-Danger-Zone-Images header automatically.
    var options = new RequestOptions();
    options.Images.Add(swatch.Reference, swatch);

    var state = JsonContent.FromNode(new JsonObject
    {
        ["task"] = "Pick the crayon color closest to the dominant color of `swatch`.",
        ["swatch"] = swatch.Reference,
    });

    // 3. One Choice question with the 8 crayon colors as options.
    var questions = new QuestionSet();
    var crayon = questions.AddChoice("closest_crayon", new ChoiceQuestion( // VERIFY: AddChoice name
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

    // 4. Call Jev and print the pick plus the full probability spread.
    using var client = new TypeSafeClient(apiKey);
    var result = await client.SystemOneAsync(state, questions, Model, options);
    var answer = result.Get(crayon);

    Console.WriteLine($"Closest crayon: {answer.Choice}  (confidence {answer.Confidence:P0})"); // VERIFY: .Choice
    foreach (var (option, p) in answer.Probabilities.OrderByDescending(kv => kv.Value))       // VERIFY: .Probabilities
        Console.WriteLine($"  {option,-7} {p:P1}");

    return 0;
}
catch (TypeSafeApiException ex)
{
    // 400 = bad image / missing opt-in / context overflow, 413 = size limit.
    Console.Error.WriteLine($"TypeSafe API error: {ex.Message}");
    return 2;
}
catch (Exception ex) when (ex is FileNotFoundException or MagickException or InvalidOperationException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static byte[] PrepareImage(string path)
{
    const int MinSide = 280;   // docs disagree (256 vs 280); 280 is the safe floor
    const int MaxSide = 1568;

    using var image = new MagickImage(path); // accepts PNG, JPEG, WebP, GIF, BMP...
    image.AutoOrient();                      // Jev ignores EXIF orientation

    uint w = image.Width, h = image.Height;
    double scale = 1.0;
    if (Math.Max(w, h) > MaxSide) scale = (double)MaxSide / Math.Max(w, h);
    else if (Math.Min(w, h) < MinSide) scale = (double)MinSide / Math.Min(w, h);

    if (scale != 1.0)
    {
        w = (uint)Math.Max(1, Math.Round(w * scale));
        h = (uint)Math.Max(1, Math.Round(h * scale));
        image.Resize(w, h);
    }

    if (Math.Min(w, h) < MinSide || Math.Max(w, h) > MaxSide)
        throw new InvalidOperationException(
            $"Aspect ratio too extreme to fit {MinSide}–{MaxSide}px on both sides ({w}x{h}). Crop it first.");

    // PNG avoids adding JPEG artifacts on re-encode; the 16 MiB cap is far away at these sizes.
    image.Format = MagickFormat.Png;
    return image.ToByteArray();
}
