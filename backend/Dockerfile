# syntax=docker/dockerfile:1
# Build context: ./backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props EEMP.slnx ./
COPY src/ src/
RUN dotnet restore src/EEMP.Api/EEMP.Api.csproj
RUN dotnet publish src/EEMP.Api/EEMP.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# IANA time zones (Asia/Tehran) are required at runtime; ICU ships with the aspnet image.
# Report-card PDFs (DL-057) use SkiaSharp.NativeAssets.Linux.NoDependencies + HarfBuzzSharp.NativeAssets.Linux: the native
# libraries need only glibc/libstdc++ (present in this image) and the Vazirmatn font is embedded in EEMP.Infrastructure, so no
# fontconfig or system fonts are installed. Alpine/musl images would also work (linux-musl-x64 natives are published).
RUN apt-get update \
 && apt-get install -y --no-install-recommends tzdata \
 && rm -rf /var/lib/apt/lists/*
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ASPNETCORE_URLS=http://+:8080
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "EEMP.Api.dll"]
