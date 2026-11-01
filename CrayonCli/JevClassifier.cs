using System.Text.Json.Nodes;
using TypeSafeAI;
using JsonContent = TypeSafeAI.JsonContent; // the Web SDK also imports System.Net.Http.Json.JsonContent

public record ColorProbability(string Color, double Probability);
public record CrayonResult(string Closest, double Confidence, IReadOnlyList<ColorProbability> Probabilities);

public class ClassifierException(Exception inner) : Exception("Classifier call failed.", inner);

public interface ICrayonClassifier
{
    Task<CrayonResult> ClassifyAsync(byte[] png, CancellationToken cancellationToken);
}

public class JevClassifier(TypeSafeClient client) : ICrayonClassifier
{
    private const string Model = "jev-1.14.0"; // pin a version; images also work on jev-1.13.0

    public async Task<CrayonResult> ClassifyAsync(byte[] png, CancellationToken cancellationToken)
    {
        var swatch = ImageAsset.FromBytes(png, "png");

        // The SDK adds the TypeSafe-Danger-Zone-Images header automatically.
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
            // 400 = bad image / missing opt-in / context overflow, 413 = size limit.
            var result = await client.SystemOneAsync(state, questions, Model, options, cancellationToken);
            var answer = result.Get(crayon);

            return new CrayonResult(
                answer.Choice,
                answer.Confidence,
                answer.Probabilities
                    .Select(kv => new ColorProbability(kv.Key, kv.Value))
                    .ToList());
        }
        catch (TypeSafeApiException ex)
        {
            throw new ClassifierException(ex);
        }
    }
}
