FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy everything and publish
COPY . .
RUN dotnet publish "TSIC-Core-Angular/src/backend/TSIC.API/TSIC.API.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Expose HTTP port (we use HTTP inside container for local dev)
ENV ASPNETCORE_URLS=http://+:5022
EXPOSE 5022

ENTRYPOINT ["dotnet", "TSIC.API.dll"]
