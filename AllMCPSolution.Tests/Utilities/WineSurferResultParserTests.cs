using System.Linq;
using AllMCPSolution.Models;
using AllMCPSolution.Utilities;
using FluentAssertions;
using Xunit;

namespace AllMCPSolution.Tests.Utilities;

public class WineSurferResultParserTests
{
    [Fact]
    public void TryParse_ReturnsMatches_ForValidObjectPayload()
    {
        const string payload = "{\"wines\":[{\"name\":\"Chateau Example\",\"region\":\"Bordeaux\",\"appellation\":\"Pauillac\",\"subAppellation\":\"Lafite\"}]}";

        var success = WineSurferResultParser.TryParse(payload, out var matches);

        success.Should().BeTrue();
        matches.Should().HaveCount(1);
        matches[0].Should().BeEquivalentTo(new WineSurferLookupResult(
            "Chateau Example",
            "Bordeaux",
            "Pauillac",
            "Lafite"));
    }

    [Fact]
    public void TryParse_HandlesCodeFencesAndTrimming()
    {
        const string payload = "```json\n{\n  \"wines\": [\n    { \"name\": \"Wine A\", \"region\": \"Region\" },\n    { \"name\": \"Wine B\" }\n  ]\n}\n```";

        var success = WineSurferResultParser.TryParse(payload, out var matches);

        success.Should().BeTrue();
        matches.Should().HaveCount(2);
        matches[0].Name.Should().Be("Wine A");
        matches[0].Region.Should().Be("Region");
        matches[1].Name.Should().Be("Wine B");
        matches[1].Region.Should().BeNull();
    }

    [Fact]
    public void TryParse_LimitsToSixResultsAndDeduplicates()
    {
        var unique = string.Join(',', Enumerable.Range(1, 7).Select(index =>
            $"{{\"name\":\"Wine {index}\",\"region\":\"Region {index}\"}}"));
        var payload = "[" + unique + ",{" + "\"name\":\"Wine 3\",\"region\":\"Another\"}" + "]";

        var success = WineSurferResultParser.TryParse(payload, out var matches);

        success.Should().BeTrue();
        matches.Should().HaveCount(6);
        matches.Select(m => m.Name).Should().ContainInOrder("Wine 1", "Wine 2", "Wine 3", "Wine 4", "Wine 5", "Wine 6");
        matches.Any(m => m.Name == "Wine 7").Should().BeFalse();
    }

    [Fact]
    public void TryParse_ReturnsFalseForInvalidJson()
    {
        var success = WineSurferResultParser.TryParse("not json", out var matches);

        success.Should().BeFalse();
        matches.Should().BeEmpty();
    }
}
