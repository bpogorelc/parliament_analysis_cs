using System.Text.Json.Nodes;
using ParliamentAnalysis.Core.Dip;
using ParliamentAnalysis.Core.Services;

namespace ParliamentAnalysis.Tests;

public class DistributionServiceTests
{
    [Fact]
    public void ComputeDistribution_CountsFraktionenAndPercentages()
    {
        var persons = new List<Person>
        {
            MakePerson("1", "Alice", "A", 20, "SPD"),
            MakePerson("2", "Bob", "B", 20, "SPD"),
            MakePerson("3", "Carla", "C", 20, "CDU/CSU"),
            MakePerson("4", "Dana", "D", 20, fraktion: null),
        };

        var result = DistributionService.ComputeDistribution(persons, 20, includeUnknown: true);

        Assert.Equal(20, result["wahlperiode"]!.GetValue<int>());
        Assert.Equal(4, result["total_persons"]!.GetValue<int>());
        Assert.Equal(3, result["classified_persons"]!.GetValue<int>());

        var groups = result["groups"]!.AsArray();
        Assert.Equal(3, groups.Count);

        var spd = FindGroup(groups, "SPD");
        Assert.Equal(2, spd["count"]!.GetValue<int>());
        Assert.Equal(50.0, spd["percentage"]!.GetValue<double>());

        var cdu = FindGroup(groups, "CDU/CSU");
        Assert.Equal(1, cdu["count"]!.GetValue<int>());
        Assert.Equal(25.0, cdu["percentage"]!.GetValue<double>());

        var unknown = FindGroup(groups, DistributionService.Unknown);
        Assert.Equal(1, unknown["count"]!.GetValue<int>());
        Assert.Equal(25.0, unknown["percentage"]!.GetValue<double>());
    }

    [Fact]
    public void ComputeDistribution_ExcludesUnknownWhenDisabled()
    {
        var persons = new List<Person>
        {
            MakePerson("1", "Alice", "A", 20, "SPD"),
            MakePerson("2", "Bob", "B", 20, fraktion: null),
        };

        var result = DistributionService.ComputeDistribution(persons, 20, includeUnknown: false);

        Assert.Equal(1, result["total_persons"]!.GetValue<int>());
        Assert.Equal(1, result["classified_persons"]!.GetValue<int>());

        var groups = result["groups"]!.AsArray();
        Assert.Single(groups);
        Assert.Equal("SPD", groups[0]!["fraktion"]!.GetValue<string>());
        Assert.Equal(100.0, groups[0]!["percentage"]!.GetValue<double>());
    }

    [Fact]
    public void ComputeDistribution_ExcludesPersonsOutsideWahlperiode()
    {
        var persons = new List<Person>
        {
            MakePerson("1", "Alice", "A", 20, "SPD"),
            MakePerson("2", "Bob", "B", 19, "CDU/CSU"),
        };

        var result = DistributionService.ComputeDistribution(persons, 20);

        Assert.Equal(1, result["total_persons"]!.GetValue<int>());
        var notes = result["notes"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        Assert.Contains(notes, n => n.Contains("Excluded 1 persons", StringComparison.Ordinal));
    }

    private static Person MakePerson(
        string id,
        string vorname,
        string nachname,
        int wahlperiode,
        string? fraktion)
    {
        var roles = new List<PersonRole>();
        if (fraktion is not null)
        {
            roles.Add(new PersonRole
            {
                Fraktion = fraktion,
                Funktion = "MdB",
                WahlperiodeNummer = [wahlperiode],
            });
        }

        return new Person
        {
            Id = id,
            Vorname = vorname,
            Nachname = nachname,
            Wahlperioden = [wahlperiode],
            PersonRoles = roles,
        };
    }

    private static JsonObject FindGroup(JsonArray groups, string name) =>
        groups.OfType<JsonObject>().Single(g => g["fraktion"]!.GetValue<string>() == name);
}
