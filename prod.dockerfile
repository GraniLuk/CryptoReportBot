FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy everything and restore as distinct layers
COPY . .
RUN dotnet restore

# Build and publish the specific project rather than the solution
# Include reference to the Aspire ServiceDefaults project that the main project now depends on
RUN dotnet publish src/CryptoReportBot/CryptoReportBot.csproj -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/out .

# Set environment variables - each on a separate line for clarity
ENV ASPNETCORE_URLS=http://+:80
ENV alerts_bot_token=${alerts_bot_token}
ENV azure_function_url=${azure_function_url}
ENV azure_function_key=${azure_function_key}
# Set this to tell the app to use environment variables directly
ENV USE_ENVIRONMENT_VARIABLES=true
# Add Aspire-specific environment variables
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
ENV ASPIRE_DASHBOARD_ENABLED=false
ENV ASPNETCORE_ENVIRONMENT=Production
# Application Insights connection string - this will be set during deployment
ENV APPLICATIONINSIGHTS_CONNECTION_STRING=${APPLICATIONINSIGHTS_CONNECTION_STRING}
# Set service name for better identification in Application Insights
ENV APPLICATIONINSIGHTS_ROLE_NAME="CryptoReportBot"

EXPOSE 80
ENTRYPOINT ["dotnet", "CryptoReportBot.dll"]