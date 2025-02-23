FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["CrazyBikeShop.OrderProcessor/CrazyBikeShop.OrderProcessor.csproj", "CrazyBikeShop.OrderProcessor/"]
RUN dotnet restore "CrazyBikeShop.OrderProcessor/CrazyBikeShop.OrderProcessor.csproj"
COPY . .
WORKDIR "/src/CrazyBikeShop.OrderProcessor"
RUN dotnet build "CrazyBikeShop.OrderProcessor.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "CrazyBikeShop.OrderProcessor.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CrazyBikeShop.OrderProcessor.dll"]
