FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["DevLaunch.Api/DevLaunch.Api.csproj", "DevLaunch.Api/"]
RUN dotnet restore "DevLaunch.Api/DevLaunch.Api.csproj"
COPY . .
WORKDIR "/src/DevLaunch.Api"
RUN dotnet build "DevLaunch.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "DevLaunch.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DevLaunch.Api.dll"]
