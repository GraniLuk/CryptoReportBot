FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy everything and restore as distinct layers
COPY . .
RUN dotnet restore

# Build and publish the specific project
RUN dotnet publish src/CryptoReportBot/CryptoReportBot.csproj -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/out .

# Set environment variables - each on a separate line for clarity
ENV ASPNETCORE_URLS=http://+:80
ENV USE_ENVIRONMENT_VARIABLES=true
ENV ASPNETCORE_ENVIRONMENT=Production
ENV APPLICATIONINSIGHTS_ROLE_NAME="CryptoReportBot"

EXPOSE 80
ENTRYPOINT ["dotnet", "CryptoReportBot.dll"]