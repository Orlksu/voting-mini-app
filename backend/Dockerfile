FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

COPY ["src/Backend.Contracts/Backend.Contracts.csproj", "src/Backend.Contracts/"]
COPY ["src/Backend.RoutingEngine/Backend.RoutingEngine.csproj", "src/Backend.RoutingEngine/"]
COPY ["src/Backend.Api/Backend.Api.csproj", "src/Backend.Api/"]

RUN case "$TARGETARCH" in \
      amd64) rid=linux-x64 ;; \
      arm64) rid=linux-arm64 ;; \
      *) echo "Unsupported Docker architecture: $TARGETARCH" >&2; exit 1 ;; \
    esac && \
    dotnet restore "src/Backend.Api/Backend.Api.csproj" --runtime "$rid"

COPY . .

RUN case "$TARGETARCH" in \
      amd64) rid=linux-x64 ;; \
      arm64) rid=linux-arm64 ;; \
      *) echo "Unsupported Docker architecture: $TARGETARCH" >&2; exit 1 ;; \
    esac && \
    dotnet publish "src/Backend.Api/Backend.Api.csproj" \
      -c Release \
      -r "$rid" \
      --self-contained false \
      -o /app/publish \
      /p:UseAppHost=false \
      --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Backend.Api.dll"]
