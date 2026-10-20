using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using ParliamentAnalysis.Core.Config;
using ParliamentAnalysis.Core.Dip;
using ParliamentAnalysis.Core.Llm;
using ParliamentAnalysis.Core.Services;

namespace ParliamentAnalysis.Core.Tools;

public sealed class ParliamentTools
{
    private readonly AppSettings _settings;
    private readonly Func<DipClient> _clientFactory;
    private readonly DistributionSummarizer _summarizer;
    private static readonly ConcurrentDictionary<int, Lazy<Task<IReadOnlyList<Person>>>> PersonCache = new();

    public ParliamentTools(
        IOptions<AppSettings> options,
        Func<DipClient> clientFactory,
        DistributionSummarizer summarizer)
    {
        _settings = options.Value;
        _clientFactory = clientFactory;
        _summarizer = summarizer;
    }

    public async Task<JsonObject> GetPersonInfoAsync(
        string query,
        int? wahlperiode = null,
        CancellationToken cancellationToken = default)
    {
        var period = wahlperiode ?? _settings.DefaultWahlperiode;
        query = (query ?? "").Trim();
        if (string.IsNullOrEmpty(query))
        {
            return new JsonObject
            {
                ["found"] = false,
                ["notes"] = new JsonArray("Empty query."),
                ["matches"] = new JsonArray(),
            };
        }

        using var client = _clientFactory();
        if (query.All(char.IsDigit))
        {
            try
            {
                var person = await client.GetPersonAsync(query, cancellationToken).ConfigureAwait(false);
                return PersonPayload(person, period);
            }
            catch (Exception ex)
            {
                return new JsonObject
                {
                    ["found"] = false,
                    ["notes"] = new JsonArray($"Lookup by id failed: {ex.Message}"),
                    ["matches"] = new JsonArray(),
                };
            }
        }

        var matches = await client.SearchPersonsByNameAsync(query, period, 8, cancellationToken)
            .ConfigureAwait(false);

        if (matches.Count == 0)
        {
            var cached = await PersonsForPeriodAsync(period, cancellationToken).ConfigureAwait(false);
            matches = cached
                .Where(person =>
                    person.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || person.Titel.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(8)
                .ToList();
        }

        if (matches.Count == 0)
        {
            return new JsonObject
            {
                ["found"] = false,
                ["notes"] = new JsonArray($"No person matched query '{query}' for Wahlperiode {period}."),
                ["matches"] = new JsonArray(),
            };
        }

        if (matches.Count > 1)
        {
            var exact = matches
                .Where(person => string.Equals(person.DisplayName, query, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (exact.Count == 1)
            {
                var payload = PersonPayload(exact[0], period);
                var notes = payload["notes"] as JsonArray ?? [];
                notes.Add("Multiple candidates existed; exact display-name match selected.");
                payload["notes"] = notes;
                return payload;
            }

            var matchArray = new JsonArray();
            foreach (var person in matches)
            {
                var (fraktion, _, _) = AffiliationService.ResolveFraktion(person, period);
                matchArray.Add(new JsonObject
                {
                    ["person_id"] = person.Id,
                    ["name"] = person.DisplayName,
                    ["fraktion"] = fraktion,
                });
            }

            return new JsonObject
            {
                ["found"] = false,
                ["notes"] = new JsonArray(
                    $"Ambiguous query '{query}'. Pass a more specific name or person id."),
                ["matches"] = matchArray,
            };
        }

        return PersonPayload(matches[0], period);
    }

    public async Task<JsonObject> GetFraktionDistributionAsync(
        int? wahlperiode = null,
        bool includeUnknown = true,
        CancellationToken cancellationToken = default)
    {
        var period = wahlperiode ?? _settings.DefaultWahlperiode;
        var persons = await PersonsForPeriodAsync(period, cancellationToken).ConfigureAwait(false);
        return DistributionService.ComputeDistribution(persons, period, includeUnknown);
    }

    public async Task<JsonObject> SummarizeFraktionDistributionAsync(
        int? wahlperiode = null,
        string language = "en",
        CancellationToken cancellationToken = default)
    {
        var period = wahlperiode ?? _settings.DefaultWahlperiode;
        var distribution = await GetFraktionDistributionAsync(period, true, cancellationToken)
            .ConfigureAwait(false);
        var summary = await _summarizer.SummarizeAsync(distribution, language, cancellationToken)
            .ConfigureAwait(false);
        return new JsonObject
        {
            ["wahlperiode"] = period,
            ["summary"] = summary,
            ["distribution"] = distribution.DeepClone(),
        };
    }

    public async Task<JsonObject> InvokeAsync(
        string name,
        JsonObject? arguments,
        CancellationToken cancellationToken = default)
    {
        arguments ??= new JsonObject();
        return name switch
        {
            "get_person_info" => await GetPersonInfoAsync(
                arguments["query"]?.GetValue<string>() ?? "",
                arguments["wahlperiode"]?.GetValue<int?>(),
                cancellationToken).ConfigureAwait(false),
            "get_fraktion_distribution" => await GetFraktionDistributionAsync(
                arguments["wahlperiode"]?.GetValue<int?>(),
                arguments["include_unknown"]?.GetValue<bool?>() ?? true,
                cancellationToken).ConfigureAwait(false),
            "summarize_fraktion_distribution" => await SummarizeFraktionDistributionAsync(
                arguments["wahlperiode"]?.GetValue<int?>(),
                arguments["language"]?.GetValue<string>() ?? "en",
                cancellationToken).ConfigureAwait(false),
            _ => throw new KeyNotFoundException($"Unknown tool: {name}"),
        };
    }

    private async Task<IReadOnlyList<Person>> PersonsForPeriodAsync(
        int wahlperiode,
        CancellationToken cancellationToken)
    {
        var lazy = PersonCache.GetOrAdd(
            wahlperiode,
            period => new Lazy<Task<IReadOnlyList<Person>>>(() => LoadPersonsAsync(period)));

        try
        {
            return await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            PersonCache.TryRemove(wahlperiode, out _);
            throw;
        }
    }

    private async Task<IReadOnlyList<Person>> LoadPersonsAsync(int wahlperiode)
    {
        using var client = _clientFactory();
        var persons = await client.ListPersonsAsync(wahlperiode).ConfigureAwait(false);
        return persons;
    }

    private static JsonObject PersonPayload(Person person, int wahlperiode)
    {
        var (fraktion, funktion, notes) = AffiliationService.ResolveFraktion(person, wahlperiode);
        var wahlperioden = person.Wahlperioden
            .Concat(person.PersonRoles.SelectMany(r => r.WahlperiodeNummer))
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        return new JsonObject
        {
            ["found"] = true,
            ["person_id"] = person.Id,
            ["name"] = person.DisplayName,
            ["titel"] = person.Titel,
            ["fraktion"] = fraktion,
            ["funktion"] = funktion,
            ["wahlperioden"] = new JsonArray(wahlperioden.Select(p => (JsonNode?)p).ToArray()),
            ["matches"] = new JsonArray(),
            ["notes"] = new JsonArray(notes.Select(n => (JsonNode?)n).ToArray()),
        };
    }
}
