FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/Backend.Contracts/Backend.Contracts.csproj", "src/Backend.Contracts/"]
COPY ["src/Backend.RoutingEngine/Backend.RoutingEngine.csproj", "src/Backend.RoutingEngine/"]
COPY ["src/Backend.Api/Backend.Api.csproj", "src/Backend.Api/"]

RUN dotnet restore "src/Backend.Api/Backend.Api.csproj"

COPY . .

RUN dotnet publish "src/Backend.Api/Backend.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Backend.Api.dll"]
