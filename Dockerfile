# One Dockerfile for every .NET container. Pick the project with --build-arg SERVICE=<ProjectName>.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG SERVICE
WORKDIR /src
COPY HappyHeadlines.slnx Directory.Build.props ./
COPY src/ src/
# The chiseled runtime has no shell to expand $SERVICE at startup, so every service's executable gets the same name.
RUN dotnet publish src/${SERVICE}/${SERVICE}.csproj -c Release -o /app \
    && mv /app/${SERVICE} /app/service

# Chiseled: only the .NET runtime and its native dependencies. No shell or package manager, runs as non-root.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["./service"]
