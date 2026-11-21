FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
COPY global.json ./
COPY backend/ backend/
RUN dotnet publish backend/Chat.Api/Chat.Api.csproj -c Release -r linux-x64 --self-contained false -o /out/api
COPY deploy/Chat.Migrator/ deploy/Chat.Migrator/
RUN dotnet publish deploy/Chat.Migrator/Chat.Migrator.csproj -c Release -r linux-x64 --self-contained false -o /out/migrate

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
COPY --from=api-build /out/api/ ./
RUN mkdir /app/keys && chown $APP_UID:$APP_UID /app/keys
ENV ASPNETCORE_HTTP_PORTS=8080 ASPNETCORE_ENVIRONMENT=Production
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Chat.Api.dll"]

FROM api AS migrate
COPY --from=api-build /out/migrate/ ./
ENTRYPOINT ["dotnet", "Chat.Migrator.dll"]

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS web-build
WORKDIR /src
COPY global.json ./
COPY frontend/Chat.Client.csproj frontend/
RUN dotnet restore frontend/Chat.Client.csproj
COPY frontend/ frontend/
RUN dotnet publish frontend/Chat.Client.csproj -c Release --no-restore -o /out/web
RUN printf '{"ApiBaseUrl":"/"}\n' > /out/web/wwwroot/appsettings.json

FROM caddy:2 AS web
COPY --from=web-build /out/web/wwwroot/ /srv/
COPY deploy/Caddyfile /etc/caddy/Caddyfile
