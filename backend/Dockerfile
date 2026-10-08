# API image for Render (Docker web service). Build context: backend/
#   docker build -t kiosk-api backend

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from project files only, so dependency layers are cached between code changes.
COPY global.json ./
COPY src/Kiosk.Domain/Kiosk.Domain.csproj src/Kiosk.Domain/
COPY src/Kiosk.Application/Kiosk.Application.csproj src/Kiosk.Application/
COPY src/Kiosk.Infrastructure/Kiosk.Infrastructure.csproj src/Kiosk.Infrastructure/
COPY src/Kiosk.Api/Kiosk.Api.csproj src/Kiosk.Api/
RUN dotnet restore src/Kiosk.Api/Kiosk.Api.csproj

COPY src/ src/
RUN dotnet publish src/Kiosk.Api/Kiosk.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_ENVIRONMENT=Production
# The base image's non-root user.
USER $APP_UID
EXPOSE 8080

# Render tells the service which port to listen on through $PORT; fall back to 8080 elsewhere.
ENTRYPOINT ["sh", "-c", "ASPNETCORE_HTTP_PORTS=${PORT:-8080} exec dotnet Kiosk.Api.dll"]
