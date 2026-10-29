FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY KarakusAluminyum.Web.csproj ./
RUN dotnet restore ./KarakusAluminyum.Web.csproj
COPY . ./
RUN dotnet publish ./KarakusAluminyum.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "KarakusAluminyum.Web.dll"]
