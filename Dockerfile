# Stage 1: Build your app
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy only the project file first (caching purposes)
COPY *.csproj ./
RUN dotnet restore

# Copy everything else and build
COPY . ./
RUN dotnet publish -c Release -o out

# Stage 2: Run your app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/out .

# Tell Docker which port your app uses
EXPOSE 8080

# Command to run the app
ENTRYPOINT ["dotnet", "AivenApi.dll"]