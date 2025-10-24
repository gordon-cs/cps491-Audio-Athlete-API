FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["AudioAthleteApi.csproj", "./"]
RUN dotnet restore "./AudioAthleteApi.csproj"
COPY . .
RUN dotnet build "AudioAthleteApi.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "AudioAthleteApi.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AudioAthleteApi.dll"]
