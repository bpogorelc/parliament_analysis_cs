using ParliamentAnalysis.Core.Dip;
using ParliamentAnalysis.Core.Services;

namespace ParliamentAnalysis.Tests;

public class AffiliationServiceTests
{
    [Fact]
    public void ResolveFraktion_PrefersPeriodSpecificRole()
    {
        var person = new Person
        {
            Id = "1",
            Vorname = "Friedrich",
            Nachname = "Merz",
            Wahlperioden = [20],
            Fraktionen = ["FDP"],
            PersonRoles =
            [
                new PersonRole
                {
                    Fraktion = "CDU/CSU",
                    Funktion = "MdB",
                    WahlperiodeNummer = [20],
                },
                new PersonRole
                {
                    Fraktion = "CDU/CSU",
                    Funktion = "MdB",
                    WahlperiodeNummer = [19],
                },
            ],
        };

        var (fraktion, funktion, notes) = AffiliationService.ResolveFraktion(person, 20);

        Assert.Equal("CDU/CSU", fraktion);
        Assert.Equal("MdB", funktion);
        Assert.Empty(notes);
    }

    [Fact]
    public void ResolveFraktion_FallsBackToSingleTopLevelFraktion()
    {
        var person = new Person
        {
            Id = "2",
            Vorname = "Olaf",
            Nachname = "Scholz",
            Wahlperioden = [20],
            Fraktionen = ["SPD"],
            Funktionen = ["Bundeskanzler"],
        };

        var (fraktion, funktion, notes) = AffiliationService.ResolveFraktion(person, 20);

        Assert.Equal("SPD", fraktion);
        Assert.Equal("Bundeskanzler", funktion);
        Assert.Empty(notes);
    }

    [Fact]
    public void ResolveFraktion_NotesWhenPeriodMissingAndNoFraktion()
    {
        var person = new Person
        {
            Id = "3",
            Vorname = "Max",
            Nachname = "Mustermann",
            Wahlperioden = [19],
        };

        var (fraktion, funktion, notes) = AffiliationService.ResolveFraktion(person, 20);

        Assert.Null(fraktion);
        Assert.Null(funktion);
        Assert.Contains(notes, n => n.Contains("does not include 20", StringComparison.Ordinal));
        Assert.Contains(notes, n => n.Contains("No Fraktion found", StringComparison.Ordinal));
    }

    [Fact]
    public void ResolveFraktion_PrefersParliamentaryLookingRole()
    {
        var person = new Person
        {
            Id = "4",
            Vorname = "Anna",
            Nachname = "Beispiel",
            Wahlperioden = [20],
            PersonRoles =
            [
                new PersonRole
                {
                    Fraktion = "BÜNDNIS 90/DIE GRÜNEN",
                    Funktion = "Sachverständige",
                    WahlperiodeNummer = [20],
                },
                new PersonRole
                {
                    Fraktion = "SPD",
                    Funktion = "Mitglied des Bundestages",
                    WahlperiodeNummer = [20],
                },
            ],
        };

        var (fraktion, funktion, notes) = AffiliationService.ResolveFraktion(person, 20);

        Assert.Equal("SPD", fraktion);
        Assert.Equal("Mitglied des Bundestages", funktion);
        Assert.Contains(notes, n => n.Contains("Multiple role Fraktionen", StringComparison.Ordinal));
    }
}
