FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY backend/ backend/
RUN dotnet publish backend/Chat.Api/Chat.Api.csproj -c Release -o /out/api
COPY frontend/ frontend/
RUN dotnet publish frontend/Chat.Client.csproj -c Release -o /out/client
RUN printf '{"ApiBaseUrl":"/"}\n' > /out/client/wwwroot/appsettings.json

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out/api/ ./
COPY --from=build /out/client/wwwroot/ ./wwwroot/
RUN mkdir /app/files && chown $APP_UID:$APP_UID /app/files
ENV ASPNETCORE_HTTP_PORTS=10000 \
    ASPNETCORE_ENVIRONMENT=Production \
    FileStorage__RootPath=/app/files \
    ServeClient=true \
    MigrateOnStart=true
USER $APP_UID
EXPOSE 10000
ENTRYPOINT ["dotnet", "Chat.Api.dll"]
