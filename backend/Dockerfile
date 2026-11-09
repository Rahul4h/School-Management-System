FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY backend/src/Admission.Api/Admission.Api.csproj ./
RUN dotnet restore
COPY backend/src/Admission.Api/ .
RUN dotnet publish -c Release -o /out /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet","Admission.Api.dll"]
