# Multi-stage build for Render (Docker runtime — .NET is not a native Render language).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/ParliamentAnalysis.Core/ParliamentAnalysis.Core.csproj src/ParliamentAnalysis.Core/
COPY src/ParliamentAnalysis.Web/ParliamentAnalysis.Web.csproj src/ParliamentAnalysis.Web/

RUN dotnet restore src/ParliamentAnalysis.Web/ParliamentAnalysis.Web.csproj

COPY src/ParliamentAnalysis.Core/ src/ParliamentAnalysis.Core/
COPY src/ParliamentAnalysis.Web/ src/ParliamentAnalysis.Web/

RUN dotnet publish src/ParliamentAnalysis.Web/ParliamentAnalysis.Web.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 10000

# Render injects PORT; Program.cs also binds 0.0.0.0:$PORT when set.
ENTRYPOINT ["/bin/sh", "-c", "export ASPNETCORE_URLS=http://0.0.0.0:${PORT:-10000} && exec dotnet ParliamentAnalysis.Web.dll"]
