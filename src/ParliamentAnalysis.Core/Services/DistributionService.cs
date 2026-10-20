using System.Text.Json.Nodes;
using ParliamentAnalysis.Core.Dip;

namespace ParliamentAnalysis.Core.Services;

public static class DistributionService
{
    public const string Unknown = "Unknown";

    public static JsonObject ComputeDistribution(
        IReadOnlyList<Person> persons,
        int wahlperiode,
        bool includeUnknown = true)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var notes = new List<string>();
        var classified = 0;

        var inScope = persons
            .Where(person => person.Wahlperioden.Count == 0 || person.Wahlperioden.Contains(wahlperiode))
            .ToList();

        if (inScope.Count < persons.Count)
        {
            notes.Add($"Excluded {persons.Count - inScope.Count} persons without Wahlperiode {wahlperiode}.");
        }

        foreach (var person in inScope)
        {
            var (fraktion, _, personNotes) = AffiliationService.ResolveFraktion(person, wahlperiode);
            if (!string.IsNullOrWhiteSpace(fraktion))
            {
                counts[fraktion!] = counts.GetValueOrDefault(fraktion!) + 1;
                classified++;
            }
            else if (includeUnknown)
            {
                counts[Unknown] = counts.GetValueOrDefault(Unknown) + 1;
            }
            else
            {
                continue;
            }

            if (personNotes.Count > 0 && notes.Count < 12
                && personNotes.Any(n => n.Contains("No Fraktion", StringComparison.Ordinal)
                                        || n.Contains("Multiple", StringComparison.Ordinal)))
            {
                notes.Add($"{person.DisplayName}: {personNotes[0]}");
            }
        }

        var total = counts.Values.Sum();
        var groups = new JsonArray();
        foreach (var (name, count) in counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key))
        {
            groups.Add(new JsonObject
            {
                ["fraktion"] = name,
                ["count"] = count,
                ["percentage"] = total == 0 ? 0.0 : Math.Round(count * 100.0 / total, 2),
            });
        }

        return new JsonObject
        {
            ["wahlperiode"] = wahlperiode,
            ["total_persons"] = total,
            ["classified_persons"] = classified,
            ["groups"] = groups,
            ["methodology"] =
                "Affiliation is derived from DIP /person payloads: prefer person_roles "
                + $"with Fraktion for Wahlperiode {wahlperiode}, else top-level fraktion fields. "
                + "Percentages use count(fraktion) / total_persons_in_scope * 100.",
            ["notes"] = new JsonArray(notes.Select(n => (JsonNode?)n).ToArray()),
        };
    }
}
