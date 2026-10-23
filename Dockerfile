# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source
COPY nuget.config Directory.Build.props ./
COPY src/ResortWise.LoanBalance.Api/ResortWise.LoanBalance.Api.csproj src/ResortWise.LoanBalance.Api/
RUN dotnet restore src/ResortWise.LoanBalance.Api/ResortWise.LoanBalance.Api.csproj
COPY src/ src/
RUN dotnet publish src/ResortWise.LoanBalance.Api/ResortWise.LoanBalance.Api.csproj \
    -c Release -o /app --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
# The base image listens on port 8080 (ASPNETCORE_HTTP_PORTS). Set ASPNETCORE_URLS in the
# environment to override it; all other configuration also comes from environment variables.
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "ResortWise.LoanBalance.Api.dll"]
