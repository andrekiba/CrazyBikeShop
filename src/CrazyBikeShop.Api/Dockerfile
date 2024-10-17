FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["CrazyBikeShop.Api/CrazyBikeShop.Api.csproj", "CrazyBikeShop.Api/"]
RUN dotnet restore "CrazyBikeShop.Api/CrazyBikeShop.Api.csproj"
COPY . .
WORKDIR "/src/CrazyBikeShop.Api"
RUN dotnet build "CrazyBikeShop.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "CrazyBikeShop.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CrazyBikeShop.Api.dll"]
