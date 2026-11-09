# Stage 1: Build the application
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY NotesCrudApi.csproj ./
RUN dotnet restore NotesCrudApi.csproj

COPY . .
RUN dotnet build NotesCrudApi.csproj -c Release -o /app/build --no-restore

# Stage 2: Publish the application
FROM build AS publish
RUN dotnet publish NotesCrudApi.csproj -c Release -o /app/publish /p:UseAppHost=false --no-restore

# Stage 3: Run the application
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS production
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080

# Install curl for the Docker health check
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .

# Run as a non-root user
USER app

EXPOSE 8080

ENTRYPOINT ["dotnet", "NotesCrudApi.dll"]