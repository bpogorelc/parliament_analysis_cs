using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ParliamentAnalysis.Core.Config;

namespace ParliamentAnalysis.Core.Dip;

public sealed class DipClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public DipClient(HttpClient http, IOptions<AppSettings> options)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.DipApiKey))
        {
            throw new InvalidOperationException("DIP_API_KEY is required");
        }

        _http = http;
        _ownsClient = false;
        ConfigureClient(_http, settings);
    }

    public DipClient(AppSettings settings, HttpMessageHandler? handler = null)
    {
        if (string.IsNullOrWhiteSpace(settings.DipApiKey))
        {
            throw new InvalidOperationException("DIP_API_KEY is required");
        }

        if (handler is null)
        {
            var sockets = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = settings.DipSslVerify
                    ? null
                    : HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            };
            _http = new HttpClient(sockets);
        }
        else
        {
            _http = new HttpClient(handler);
        }

        _ownsClient = true;
        ConfigureClient(_http, settings);
    }

    private static void ConfigureClient(HttpClient http, AppSettings settings)
    {
        http.BaseAddress = new Uri(settings.DipApiBaseUrl.TrimEnd('/') + "/");
        http.Timeout = TimeSpan.FromSeconds(60);
        http.DefaultRequestHeaders.Remove("Authorization");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"ApiKey {settings.DipApiKey}");
        http.DefaultRequestHeaders.Accept.Clear();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.Remove("Origin");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://dip.bundestag.de");
        http.DefaultRequestHeaders.Referrer = new Uri("https://dip.bundestag.de/");
        http.DefaultRequestHeaders.UserAgent.Clear();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("parliament-agent/0.2 (+interview coding challenge)");
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }

    public async Task<Person> GetPersonAsync(string personId, CancellationToken cancellationToken = default)
    {
        using var doc = await GetJsonAsync($"person/{personId}?format=json", cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;
        if (root.TryGetProperty("id", out _) && root.TryGetProperty("nachname", out _))
        {
            return Person.FromApi(root);
        }

        if (root.TryGetProperty("documents", out var documents) && documents.ValueKind == JsonValueKind.Array
            && documents.GetArrayLength() > 0)
        {
            return Person.FromApi(documents[0]);
        }

        throw new KeyNotFoundException($"Person {personId} not found");
    }

    public async Task<List<Person>> ListPersonsAsync(int? wahlperiode = null, CancellationToken cancellationToken = default)
    {
        var persons = new List<Person>();
        await foreach (var person in IterPersonsAsync(wahlperiode, cancellationToken).ConfigureAwait(false))
        {
            persons.Add(person);
        }

        return persons;
    }

    public async IAsyncEnumerable<Person> IterPersonsAsync(
        int? wahlperiode = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var doc in IterPersonDocsAsync(wahlperiode, cancellationToken).ConfigureAwait(false))
        {
            yield return Person.FromApi(doc);
        }
    }

    public async IAsyncEnumerable<JsonElement> IterPersonDocsAsync(
        int? wahlperiode = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;
        var seen = new HashSet<string>();

        while (true)
        {
            var query = "person?format=json";
            if (wahlperiode is not null)
            {
                query += $"&f.wahlperiode={wahlperiode}";
            }

            if (!string.IsNullOrEmpty(cursor))
            {
                query += $"&cursor={Uri.EscapeDataString(cursor)}";
            }

            using var doc = await GetJsonAsync(query, cancellationToken).ConfigureAwait(false);
            var root = doc.RootElement;
            if (!root.TryGetProperty("documents", out var documents) || documents.ValueKind != JsonValueKind.Array
                || documents.GetArrayLength() == 0)
            {
                yield break;
            }

            foreach (var item in documents.EnumerateArray())
            {
                yield return item.Clone();
            }

            string? nextCursor = null;
            if (root.TryGetProperty("cursor", out var cursorEl) && cursorEl.ValueKind == JsonValueKind.String)
            {
                nextCursor = cursorEl.GetString();
            }

            if (string.IsNullOrEmpty(nextCursor) || !seen.Add(nextCursor))
            {
                yield break;
            }

            cursor = nextCursor;
        }
    }

    public async Task<List<Person>> SearchPersonsByNameAsync(
        string query,
        int? wahlperiode = null,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var needle = NormalizeName(query);
        if (string.IsNullOrEmpty(needle))
        {
            return [];
        }

        var scored = new List<(int Score, Person Person)>();
        await foreach (var person in IterPersonsAsync(wahlperiode, cancellationToken).ConfigureAwait(false))
        {
            var hay = NormalizeName(string.Join(" ", new[]
            {
                person.Vorname,
                person.Namenszusatz ?? "",
                person.Nachname,
                person.Titel,
            }.Where(p => !string.IsNullOrWhiteSpace(p))));

            var score = NameScore(needle, hay, person);
            if (score > 0)
            {
                scored.Add((score, person));
            }
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Person.Nachname)
            .ThenBy(x => x.Person.Vorname)
            .Take(limit)
            .Select(x => x.Person)
            .ToList();
    }

    private async Task<JsonDocument> GetJsonAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(relativeUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    internal static string NormalizeName(string value) =>
        string.Join(" ", value.ToLowerInvariant().Replace(",", " ").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    internal static int NameScore(string needle, string hay, Person person)
    {
        if (needle == NormalizeName(person.DisplayName))
        {
            return 100;
        }

        if (needle == NormalizeName($"{person.Nachname} {person.Vorname}"))
        {
            return 95;
        }

        if (hay.Contains(needle, StringComparison.Ordinal))
        {
            return hay.StartsWith(needle, StringComparison.Ordinal)
                   || needle.StartsWith(person.Nachname.ToLowerInvariant(), StringComparison.Ordinal)
                ? 80
                : 60;
        }

        var parts = needle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && parts.All(part => hay.Contains(part, StringComparison.Ordinal)))
        {
            return 70;
        }

        if (parts.Contains(person.Nachname.ToLowerInvariant())
            && hay.Contains(person.Vorname.ToLowerInvariant(), StringComparison.Ordinal))
        {
            return 65;
        }

        return 0;
    }
}
