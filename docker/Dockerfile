FROM microsoft/dotnet:2.0.0-sdk-jessie AS build-env
WORKDIR /app

# copy sln/csproj and restore as distinct layers
COPY *.sln ./
COPY ../PlatformLevelTechempower/*.csproj PlatformLevelTechempower/
RUN dotnet restore

# copy everything else and build
COPY .. ./
RUN dotnet publish -c Release -o out /p:PublishWithAspNetCoreTargetManifest=false

# build runtime image
FROM microsoft/dotnet:2.0.0-runtime-jessie
WORKDIR /app
COPY --from=build-env /app/PlatformLevelTechempower/out ./
ENTRYPOINT ["dotnet", "PlatformLevelTechempower.dll"]
