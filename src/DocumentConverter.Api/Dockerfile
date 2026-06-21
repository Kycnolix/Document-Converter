FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/DocumentConverter.Api/DocumentConverter.Api.csproj src/DocumentConverter.Api/
COPY src/DocumentConverter.Shared/DocumentConverter.Shared.csproj src/DocumentConverter.Shared/
RUN dotnet restore src/DocumentConverter.Api/DocumentConverter.Api.csproj

COPY . .
RUN dotnet publish src/DocumentConverter.Api/DocumentConverter.Api.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ENV ASPNETCORE_HTTP_PORTS=8080
ENV TZ=Europe/Istanbul

RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "DocumentConverter.Api.dll"]
