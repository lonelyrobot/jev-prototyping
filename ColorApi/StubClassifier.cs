// A fixed result for the Playwright tests, so they spend no TypeSafe credits.
// Program.cs registers this only in the E2E environment. The list is deliberately unsorted.
public class StubClassifier : IColorClassifier
{
    public Task<ColorResult> ClassifyAsync(byte[] png, CancellationToken cancellationToken) =>
        Task.FromResult(new ColorResult("green", 0.75,
        [
            new("red", 0.01),
            new("orange", 0.01),
            new("yellow", 0.1),
            new("green", 0.75),
            new("blue", 0.1),
            new("violet", 0.01),
            new("brown", 0.01),
            new("black", 0.01),
        ]));
}
