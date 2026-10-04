using robeem.DataModel;

namespace robeem.Tests.DataModel;

public sealed class EntityIndexerTests
{
    [Fact]
    public void Indexer_ReadsAndWritesScalarProperties_WithConversion()
    {
        var line = new TimeScheduleLine();

        line["CycleDay"] = "3";

        Assert.Equal(3, line.CycleDay);
        Assert.Equal(3, line["CycleDay"]);
    }

    [Fact]
    public void Indexer_OnLoadedNavigation_WritesTheForeignKey()
    {
        var line = new TimeScheduleLine { Team = new TimeScheduleTeam { Id = 1 }, TeamId = 1 };

        line["Team"] = 42;

        Assert.Equal(42, line.TeamId);
    }

    [Fact]
    public void Indexer_UnknownProperty_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Profile()["DoesNotExist"]);
    }

    [Fact]
    public void Indexer_ReadOnlyProperty_Throws()
    {
        var profile = new Profile();

        Assert.Throws<InvalidOperationException>(() => profile["FullName"] = "x");
    }
}
