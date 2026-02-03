# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy only SubPubC project
COPY SubPubC/SubPubC.csproj SubPubC/
RUN dotnet restore SubPubC/SubPubC.csproj

# Copy the remaining source and publish
COPY SubPubC/ SubPubC/
RUN dotnet publish SubPubC/SubPubC.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "SubPubC.dll"]

# docker build -t registry.gitlab.com/gcoder.ninhph/sub-pub-c .
# docker push registry.gitlab.com/gcoder.ninhph/sub-pub-c
