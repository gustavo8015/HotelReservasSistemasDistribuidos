# Imagen de la API REST. Construcción en dos etapas: la primera compila con el SDK de .NET 8 y
# la segunda solo lleva el motor de ejecución, de modo que la imagen final no carga el
# compilador ni el código fuente.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS compilacion
WORKDIR /origen

# Primero el archivo de proyecto: si no cambia, Docker reutiliza la capa de dependencias y la
# construcción siguiente no vuelve a descargar los paquetes.
COPY HotelReservas.Api/HotelReservas.Api.csproj HotelReservas.Api/
RUN dotnet restore HotelReservas.Api/HotelReservas.Api.csproj

COPY HotelReservas.Api/ HotelReservas.Api/
RUN dotnet publish HotelReservas.Api/HotelReservas.Api.csproj -c Release -o /publicado /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=compilacion /publicado .

# Usuario sin privilegios: si alguien lograra ejecutar código dentro del contenedor, no lo haría
# como administrador de la imagen.
RUN useradd --uid 10001 --no-create-home --shell /usr/sbin/nologin servicio
USER 10001

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# El orquestador consulta este recurso para saber si el proceso sigue atendiendo. No toca la
# base de datos: para eso está /health/ready.
HEALTHCHECK --interval=15s --timeout=3s --start-period=20s --retries=3 \
  CMD ["/bin/sh","-c","wget -q -O /dev/null http://localhost:8080/health || exit 1"]

ENTRYPOINT ["dotnet", "HotelReservas.Api.dll"]
