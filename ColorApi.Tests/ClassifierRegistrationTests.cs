using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

public class ClassifierRegistrationTests
{
    // .env values don't overwrite existing variables, so a real key in .env is never used.
    public ClassifierRegistrationTests() =>
        Environment.SetEnvironmentVariable("TYPESAFE_API_KEY", "test-key");

    private static IColorClassifier Resolve(string environment)
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseEnvironment(environment));
        return factory.Services.GetRequiredService<IColorClassifier>();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void Uses_the_real_classifier_outside_E2E(string environment) =>
        Assert.IsType<JevClassifier>(Resolve(environment));

    [Fact]
    public void Uses_the_stub_only_in_E2E() =>
        Assert.IsType<StubClassifier>(Resolve("E2E"));
}
