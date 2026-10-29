FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY tests/MockOidcServer/MockOidcServer.csproj tests/MockOidcServer/
RUN dotnet restore tests/MockOidcServer/MockOidcServer.csproj

COPY tests/MockOidcServer tests/MockOidcServer
RUN dotnet publish tests/MockOidcServer/MockOidcServer.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 19090
ENTRYPOINT ["dotnet", "MockOidcServer.dll"]
