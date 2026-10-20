# Architecture (C#)

Interview-friendly MCP agent for DIP Fraktion analysis — ASP.NET Core port of the original Python system.

## Goal

1. Pull politician data from the DIP API.
2. Derive Fraktion affiliation and percentage shares.
3. Expose capabilities as MCP tools.
4. Use an LLM for natural-language summaries and chat tool calling — including an MCP client↔server path that works on Render.

## Layout

```text
src/
  ParliamentAnalysis.Core/
    Config/AppSettings.cs
    Dip/                  # HTTP client + Person models
    Services/             # Affiliation + Distribution (deterministic)
    Tools/ParliamentTools.cs
    Llm/DistributionSummarizer.cs
    Host/ChatHost.cs
    Mcp/                  # in-memory + stdio MCP session factory
  ParliamentAnalysis.Web/ # Minimal APIs + static UI
  ParliamentAnalysis.Cli/ # chat | distribution | person | summarize | mcp
schemas/tools.json        # MCP tool contracts (source of truth)
```

## Data flow

```text
DIP API (/person)
    → DipClient (paginate, normalize)
    → AffiliationService / DistributionService
    → ParliamentTools
    → ChatHost + DistributionSummarizer
         ├─ mode=direct → ParliamentTools.InvokeAsync
         └─ mode=mcp    → McpClient ↔ McpServer over System.IO.Pipelines
                            default: memory (Render-safe)
                            optional: MCP_TRANSPORT=stdio
    → Web API + wwwroot UI
```

## Dual-path chat

Groq (or the keyword router) always selects the tool. The `mode` only changes how the tool runs:

- **direct** — call `ParliamentTools` in-process.
- **mcp** — `McpClient` connected to `McpServer` over in-memory pipes (`StreamClientTransport` / `StreamServerTransport`).

Same three tools either way. Chat responses expose `path`, `engine`, and `tools_called`.

## DIP quirks

- Base URL: `https://search.dip.bundestag.de/api/v1`
- Auth: `Authorization: ApiKey <key>`
- Also send `Origin: https://dip.bundestag.de` and Referer (otherwise 401)
- Cursor pagination on `/person`
- No dedicated Fraktion endpoint — compute from person payloads

## Affiliation rules

For a chosen `wahlperiode` (default **20**):

1. Prefer `person_roles` with matching period and non-empty Fraktion.
2. Prefer MdB / parliamentary-looking roles.
3. Else top-level `fraktion` fields.
4. Else any role Fraktion with a note.
5. Else `Unknown` (optional bucket).

Percentages = `count(fraktion) / total_persons_in_scope * 100` (2 decimals).

## Render

.NET is not a native Render runtime → ship via **Dockerfile**. Bind `0.0.0.0:$PORT`. Keep `MCP_TRANSPORT=memory`.
