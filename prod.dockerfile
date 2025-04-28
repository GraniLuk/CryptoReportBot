FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy everything and restore as distinct layers
COPY . .
RUN dotnet restore

# Build and publish the specific project rather than the solution
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

EXPOSE 80
ENTRYPOINT ["dotnet", "CryptoReportBot.dll"]