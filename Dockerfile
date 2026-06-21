FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Copy Directory.Build.props (shared props + analyzers) and every csproj before restore so the
# host's `dotnet restore` resolves the four context libraries it references transitively.
COPY Directory.Build.props ./
COPY src/LupiraMtgApi.Pricing/LupiraMtgApi.Pricing.csproj src/LupiraMtgApi.Pricing/
COPY src/LupiraMtgApi.Catalog/LupiraMtgApi.Catalog.csproj src/LupiraMtgApi.Catalog/
COPY src/LupiraMtgApi.Recognition/LupiraMtgApi.Recognition.csproj src/LupiraMtgApi.Recognition/
COPY src/LupiraMtgApi.Collections/LupiraMtgApi.Collections.csproj src/LupiraMtgApi.Collections/
COPY src/LupiraMtgApi/LupiraMtgApi.csproj src/LupiraMtgApi/
RUN dotnet restore src/LupiraMtgApi/LupiraMtgApi.csproj
COPY src/ src/
RUN dotnet publish src/LupiraMtgApi/LupiraMtgApi.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# libfontconfig1 is required by SkiaSharp.NativeAssets.Linux (pulled in transitively via the
# Recognition context) — without it the SkiaSharp native init (SKImageInfo cctor) throws
# DllNotFoundException on the first icon rasterize, breaking the set-symbol pipeline.
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl libfontconfig1 \
 && rm -rf /var/lib/apt/lists/*
RUN mkdir -p /app/bin /app/cache
COPY --from=build /out /app/bin
WORKDIR /app/cache
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    ASPNETCORE_CONTENTROOT=/app/bin \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
ENTRYPOINT ["dotnet", "/app/bin/LupiraMtgApi.dll"]
