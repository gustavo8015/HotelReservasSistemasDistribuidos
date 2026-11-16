# Imagen del cliente Blazor WebAssembly. El resultado de la compilación son archivos estáticos,
# de modo que la imagen final es un nginx que los sirve; no hay motor de .NET en ejecución.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS compilacion
WORKDIR /origen
COPY HotelReservas.Web/HotelReservas.Web.csproj HotelReservas.Web/
RUN dotnet restore HotelReservas.Web/HotelReservas.Web.csproj
COPY HotelReservas.Web/ HotelReservas.Web/
RUN dotnet publish HotelReservas.Web/HotelReservas.Web.csproj -c Release -o /publicado

FROM nginx:1.27-alpine AS final
COPY --from=compilacion /publicado/wwwroot /usr/share/nginx/html
COPY despliegue/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80

HEALTHCHECK --interval=15s --timeout=3s --retries=3 \
  CMD ["/bin/sh","-c","wget -q -O /dev/null http://localhost/ || exit 1"]
