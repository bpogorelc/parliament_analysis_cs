using System.Text.Json;

namespace ParliamentAnalysis.Core.Dip;

public sealed class PersonRole
{
    public string? Funktion { get; init; }
    public string Nachname { get; init; } = "";
    public string Vorname { get; init; } = "";
    public string? Fraktion { get; init; }
    public string? Namenszusatz { get; init; }
    public string? Funktionszusatz { get; init; }
    public List<int> WahlperiodeNummer { get; init; } = [];
}

public sealed class Person
{
    public string Id { get; init; } = "";
    public string Nachname { get; init; } = "";
    public string Vorname { get; init; } = "";
    public string Titel { get; init; } = "";
    public string Typ { get; init; } = "Person";
    public string? Namenszusatz { get; init; }
    public List<int> Wahlperioden { get; init; } = [];
    public List<string> Fraktionen { get; init; } = [];
    public List<string> Funktionen { get; init; } = [];
    public List<PersonRole> PersonRoles { get; init; } = [];

    public string DisplayName
    {
        get
        {
            var parts = new List<string> { Vorname };
            if (!string.IsNullOrWhiteSpace(Namenszusatz))
            {
                parts.Add(Namenszusatz);
            }

            parts.Add(Nachname);
            return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }
    }

    public static Person FromApi(JsonElement payload)
    {
        var roles = new List<PersonRole>();
        if (payload.TryGetProperty("person_roles", out var rolesEl) && rolesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var role in rolesEl.EnumerateArray())
            {
                if (role.ValueKind == JsonValueKind.Object)
                {
                    roles.Add(PersonRoleFromApi(role));
                }
            }
        }

        return new Person
        {
            Id = GetString(payload, "id") ?? "",
            Nachname = GetString(payload, "nachname") ?? "",
            Vorname = GetString(payload, "vorname") ?? "",
            Titel = GetString(payload, "titel") ?? "",
            Typ = GetString(payload, "typ") ?? "Person",
            Namenszusatz = GetString(payload, "namenszusatz"),
            Wahlperioden = AsIntList(payload, "wahlperiode"),
            Fraktionen = AsStrList(payload, "fraktion"),
            Funktionen = AsStrList(payload, "funktion"),
            PersonRoles = roles,
        };
    }

    private static PersonRole PersonRoleFromApi(JsonElement payload) => new()
    {
        Funktion = GetString(payload, "funktion"),
        Nachname = GetString(payload, "nachname") ?? "",
        Vorname = GetString(payload, "vorname") ?? "",
        Fraktion = GetString(payload, "fraktion"),
        Namenszusatz = GetString(payload, "namenszusatz"),
        Funktionszusatz = GetString(payload, "funktionszusatz"),
        WahlperiodeNummer = AsIntList(payload, "wahlperiode_nummer"),
    };

    private static string? GetString(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var el) || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
    }

    private static List<string> AsStrList(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var el) || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            return string.IsNullOrWhiteSpace(s) ? [] : [s];
        }

        if (el.ValueKind == JsonValueKind.Array)
        {
            return el.EnumerateArray()
                .Where(item => item.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()! : item.ToString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }

        return [el.ToString()];
    }

    private static List<int> AsIntList(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var el) || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var single))
        {
            return [single];
        }

        if (el.ValueKind == JsonValueKind.Array)
        {
            var result = new List<int>();
            foreach (var item in el.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var n))
                {
                    result.Add(n);
                }
                else if (item.ValueKind == JsonValueKind.String && int.TryParse(item.GetString(), out var parsed))
                {
                    result.Add(parsed);
                }
            }

            return result;
        }

        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var fromString))
        {
            return [fromString];
        }

        return [];
    }
}
