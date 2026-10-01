# One Dockerfile for every .NET container. Pick the project with --build-arg SERVICE=<ProjectName>.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG SERVICE
WORKDIR /src
COPY HappyHeadlines.slnx ./
COPY src/ src/
RUN dotnet publish src/${SERVICE}/${SERVICE}.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG SERVICE
ENV SERVICE_DLL=${SERVICE}.dll
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["sh", "-c", "exec dotnet $SERVICE_DLL"]
