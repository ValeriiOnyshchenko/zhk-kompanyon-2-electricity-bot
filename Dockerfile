# Cross-compiles on the CI runner's native arch, produces amd64 + arm64 images.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY src/ZhkElectricityBot/ZhkElectricityBot.csproj src/ZhkElectricityBot/
RUN dotnet restore src/ZhkElectricityBot/ZhkElectricityBot.csproj -a $TARGETARCH
COPY src/ src/
RUN dotnet publish src/ZhkElectricityBot/ZhkElectricityBot.csproj -c Release -a $TARGETARCH --no-restore -o /app \
 && mkdir -p /data

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
COPY --from=build --chown=$APP_UID:$APP_UID /data /data
USER $APP_UID
ENV DOTNET_ENVIRONMENT=Production \
    TZ=Europe/Kyiv \
    Bot__DatabasePath=/data/bot.db
VOLUME /data
ENTRYPOINT ["dotnet", "ZhkElectricityBot.dll"]
