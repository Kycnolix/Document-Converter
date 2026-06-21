FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/DocumentConverter.Worker/DocumentConverter.Worker.csproj src/DocumentConverter.Worker/
COPY src/DocumentConverter.Shared/DocumentConverter.Shared.csproj src/DocumentConverter.Shared/
RUN dotnet restore src/DocumentConverter.Worker/DocumentConverter.Worker.csproj

COPY . .
RUN dotnet publish src/DocumentConverter.Worker/DocumentConverter.Worker.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
ENV DEBIAN_FRONTEND=noninteractive
ENV HOME=/tmp
ENV TZ=Europe/Istanbul

RUN apt-get update && apt-get install -y --no-install-recommends \
    libreoffice \
    libreoffice-writer \
    libreoffice-calc \
    libreoffice-impress \
    fonts-dejavu \
    fonts-liberation \
    fontconfig \
    tzdata \
    locales \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "DocumentConverter.Worker.dll"]
