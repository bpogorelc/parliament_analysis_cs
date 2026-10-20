using ParliamentAnalysis.Core.Dip;

namespace ParliamentAnalysis.Core.Services;

public static class AffiliationService
{
    private static readonly string[] ParliamentaryHints =
    [
        "mdb",
        "mdbr",
        "mitglied des bundestages",
        "mitglied des bundesrates",
    ];

    public static (string? Fraktion, string? Funktion, List<string> Notes) ResolveFraktion(
        Person person,
        int wahlperiode)
    {
        var notes = new List<string>();

        if (person.Wahlperioden.Count > 0 && !person.Wahlperioden.Contains(wahlperiode))
        {
            notes.Add($"Person wahlperiode list [{string.Join(", ", person.Wahlperioden)}] does not include {wahlperiode}.");
        }

        var roleMatches = person.PersonRoles
            .Where(role =>
                !string.IsNullOrWhiteSpace(role.Fraktion)
                && (role.WahlperiodeNummer.Count == 0 || role.WahlperiodeNummer.Contains(wahlperiode)))
            .ToList();

        if (roleMatches.Count > 0)
        {
            var role = PickRole(roleMatches);
            if (roleMatches.Select(r => r.Fraktion).Where(f => f is not null).Distinct().Count() > 1)
            {
                notes.Add("Multiple role Fraktionen for this Wahlperiode; preferred parliamentary-looking role.");
            }

            return (role.Fraktion, role.Funktion ?? FirstOrNull(person.Funktionen), notes);
        }

        if (person.Fraktionen.Count == 1)
        {
            return (person.Fraktionen[0], FirstOrNull(person.Funktionen), notes);
        }

        if (person.Fraktionen.Count > 1)
        {
            notes.Add($"Multiple top-level Fraktionen [{string.Join(", ", person.Fraktionen)}]; using first value.");
            return (person.Fraktionen[0], FirstOrNull(person.Funktionen), notes);
        }

        var anyRole = person.PersonRoles.FirstOrDefault(role => !string.IsNullOrWhiteSpace(role.Fraktion));
        if (anyRole is not null)
        {
            notes.Add("No period-specific Fraktion; fell back to another role Fraktion.");
            return (anyRole.Fraktion, anyRole.Funktion, notes);
        }

        notes.Add("No Fraktion found in DIP person payload.");
        return (null, FirstOrNull(person.Funktionen), notes);
    }

    private static PersonRole PickRole(List<PersonRole> roles) =>
        roles.OrderByDescending(Score).First();

    private static (int Parliamentary, int HasPeriod) Score(PersonRole role)
    {
        var text = string.Join(" ", new[] { role.Funktion ?? "", role.Funktionszusatz ?? "" }
            .Where(p => !string.IsNullOrWhiteSpace(p))).ToLowerInvariant();
        var parliamentary = ParliamentaryHints.Any(hint => text.Contains(hint, StringComparison.Ordinal)) ? 1 : 0;
        var hasPeriod = role.WahlperiodeNummer.Count > 0 ? 1 : 0;
        return (parliamentary, hasPeriod);
    }

    private static string? FirstOrNull(List<string> values) => values.Count > 0 ? values[0] : null;
}
