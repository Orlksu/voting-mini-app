FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Копируем файлы проектов для эффективного кеширования слоев
COPY ["src/Backend.Contracts/Backend.Contracts.csproj", "src/Backend.Contracts/"]
COPY ["src/Backend.RoutingEngine/Backend.RoutingEngine.csproj", "src/Backend.RoutingEngine/"]
COPY ["src/Backend.Api/Backend.Api.csproj", "src/Backend.Api/"]

RUN dotnet restore "src/Backend.Api/Backend.Api.csproj"

COPY . .

# Параметры БД из .env (через build args в docker-compose).
# Значения подставляются в appsettings.json ВНУТРИ образа; исходник в репо не меняется.
ARG DB_HOST=db
ARG DB_PORT=5432
ARG DB_NAME=lctdb
ARG DB_USER=user
ARG DB_PASSWORD=password

# Литеральная подстановка без sed-спецсимволов: кавычки/&/backslash в пароле ничего не сломают.
RUN awk -v host="$DB_HOST" -v port="$DB_PORT" -v name="$DB_NAME" -v user="$DB_USER" -v pass="$DB_PASSWORD" 'function rep(s, old, new) { n = index(s, old); if (n == 0) return s; return substr(s, 1, n - 1) new substr(s, n + length(old)); } { line = rep($0, "Host=db", "Host=" host); line = rep(line, "Port=5432", "Port=" port); line = rep(line, "Database=lctdb", "Database=" name); line = rep(line, "Username=user", "Username=" user); line = rep(line, "Password=password", "Password=" pass); print line }' src/Backend.Api/appsettings.json > src/Backend.Api/appsettings.json.tmp && mv src/Backend.Api/appsettings.json.tmp src/Backend.Api/appsettings.json

WORKDIR "/src/src/Backend.Api"
RUN dotnet publish "Backend.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Backend.Api.dll"]
