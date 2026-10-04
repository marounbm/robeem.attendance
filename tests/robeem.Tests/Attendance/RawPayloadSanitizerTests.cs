using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using robeem.Application.Attendance;

namespace robeem.Tests.Attendance;

public sealed class RawPayloadSanitizerTests
{
    [Fact]
    public void Sanitize_StripsTemplateLikeFields_AndLeavesTheRestUntouched()
    {
        const string payload = """
            {"emp":"E-1","time":"2026-10-04T09:00:00+03:00","FingerprintTemplate":"AAEC","device":{"id":"D-1","face_print":"x"},"events":[{"type":"in","BiometricData":"y"}],"amount":1.50,"name":"جورج"}
            """;

        var result = JsonNode.Parse(CreateSanitizer().Sanitize(payload))!;

        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""{"emp":"E-1","time":"2026-10-04T09:00:00+03:00","device":{"id":"D-1"},"events":[{"type":"in"}],"amount":1.50,"name":"جورج"}"""),
            result));
    }

    [Fact]
    public void Sanitize_ReturnsThePayloadVerbatim_WhenNothingIsDenied()
    {
        const string payload = "{ \"emp\" : \"E-1\", \"amount\": 1.50 }";

        Assert.Same(payload, CreateSanitizer().Sanitize(payload));
    }

    [Fact]
    public void Sanitize_AppliesConfiguredTermsOnTopOfTheDefaults()
    {
        var result = JsonNode.Parse(CreateSanitizer("iris").Sanitize("""{"IrisScan":"a","Template":"b","emp":"E-1"}"""))!;

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"emp":"E-1"}"""), result));
    }

    [Fact]
    public void Sanitize_RejectsInvalidJson()
    {
        Assert.Throws<FormatException>(() => CreateSanitizer().Sanitize("emp=E-1;template=AAEC"));
    }

    private static RawPayloadSanitizer CreateSanitizer(params string[] additionalTerms) =>
        new(Options.Create(new RawPayloadSanitizerOptions { AdditionalDenyTerms = [.. additionalTerms] }));
}
