# ÇAP / YDP Öğrenci Takip Otomasyonu — .NET 8 üretim imajı (Render, Railway vb.)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY CapYdp.csproj ./
RUN dotnet restore CapYdp.csproj
COPY . ./
RUN dotnet publish CapYdp.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_ENVIRONMENT=Production \
    TRUST_PROXY=1 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
# Render/Railway PORT değişkenini verir; yoksa 8080
CMD ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} dotnet CapYdp.dll"]
