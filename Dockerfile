FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build-env
WORKDIR TrickFireDiscordBot

# Copy everything else and build
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR TrickFireDiscordBot
COPY secrets.txt .
COPY --from=build-env /TrickFireDiscordBot/out .

# Set workdir to our permanent volume
WORKDIR /data

# Run the app on container startup
EXPOSE 8080/tcp
ENTRYPOINT [ "dotnet", "/TrickFireDiscordBot/TrickFireDiscordBot.dll", "/TrickFireDiscordBot/secrets.txt" ]