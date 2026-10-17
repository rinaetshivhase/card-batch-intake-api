FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["CardBatchIntakeAll/CardBatchIntakeAll.csproj", "CardBatchIntakeAll/"]
RUN dotnet restore "CardBatchIntakeAll/CardBatchIntakeAll.csproj"

COPY . .
WORKDIR /src/CardBatchIntakeAll
RUN dotnet publish "CardBatchIntakeAll.csproj" \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

RUN mkdir -p /data && chown 1654:1654 /data
ENV ASPNETCORE_URLS=http://+:8080
ENV ConnectionStrings__Default="Data Source=/data/cardbatches.db"
EXPOSE 8080
VOLUME ["/data"]
USER 1654

ENTRYPOINT ["dotnet", "CardBatchIntakeAll.dll"]
