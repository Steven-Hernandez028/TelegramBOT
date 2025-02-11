# Etapa de compilación
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copia el archivo de proyecto y restaura las dependencias
COPY ["QuesoPrinter.csproj", "./"]
RUN dotnet restore "QuesoPrinter.csproj"

# Copia el resto de los archivos y compila la aplicación en modo Release
COPY . .
RUN dotnet publish "QuesoPrinter.csproj" -c Release -o /app/publish

# Etapa final: imagen runtime
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "QuesoPrinter.dll"]
