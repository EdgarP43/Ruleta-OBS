# Ruleta OBS

Ruleta local y gratuita para Twitch: escucha canjes de puntos del canal y bits, los guarda en una cola persistente y muestra alerta, ruleta y pendientes en OBS. Nada se descuenta hasta que el creador pulsa **Confirmar concluido**.

Todo corre desde un único `RuletaObs.exe` en la PC del creador: sin servidor en la nube, sin mensualidades y sin instalar .NET, Node ni bases de datos.

## Para creadores: instalación en 3 minutos

1. Descarga `RuletaObs-Setup.exe` desde Releases (o `RuletaObs.exe` portable y ábrelo directo).
   - Si Windows muestra “Windows protegió tu PC”, pulsa **Más información → Ejecutar de todas formas**. Pasa porque el instalador gratuito no tiene firma digital de pago.
2. Al abrirse la app, pulsa **Conectar mi canal**, inicia sesión en Twitch y autoriza.
3. Crea la recompensa de puntos desde la misma pantalla (o asigna una existente).
4. Copia el enlace **Ruleta y alertas para OBS** y pégalo en OBS como **Fuente de navegador** (1920x1080 recomendado).
5. Copia el **Panel del creador** como panel personalizado de OBS o ábrelo en otra pestaña.
6. Usa **Agregar tirada de prueba** para ver la alerta sin gastar puntos.

Durante el directo:

- **Girar siguiente**: gira la ruleta del espectador que sigue.
- **Confirmar concluido**: recién ahí se descuenta la tirada de la cola.
- **Confirmar y siguiente**: confirma y gira la próxima de una vez.
- **Cancelar**: descarta la tirada. Si fue por puntos y la recompensa es administrable por la app, devuelve los puntos en Twitch.

Si cierras OBS o la app, las tiradas pendientes quedan guardadas para el próximo directo.

## Datos y respaldo

La información queda en `%AppData%\RuletaObs`:

- `ruleta.db`: cola e historial.
- `config.json`: premios, reglas y recompensa asignada.
- `twitch-token.bin`: sesión de Twitch cifrada para tu usuario de Windows.
- `backups/`: respaldo diario de la cola (se conservan 7).

Si cambias de PC, puedes exportar la cola desde el panel e importarla desde la API local; copia además la carpeta completa si quieres conservar configuración.

## Recompensa existente vs. creada por la app

Twitch solo permite completar (`FULFILLED`) o reembolsar (`CANCELED`) recompensas creadas por la misma aplicación que las administra.

- Si creas la recompensa desde Ruleta OBS, la confirmación y el reembolso en Twitch son automáticos.
- Si asignas una recompensa creada a mano u por otra herramienta, la ruleta funciona igual, pero la confirmación será solo interna. La app te avisa y puedes clonarla como recompensa propia con el mismo nombre y costo.

Los puntos se descuentan al espectador en Twitch en el momento del canje; la recompensa queda pendiente en Twitch hasta que confirmas. Los bits no se pueden reembolsar desde Twitch.

## Para desarrolladores

### Requisitos

- Windows 10/11 x64.
- .NET SDK 10 para compilar (el usuario final no lo necesita).

### Ejecutar local

```powershell
dotnet run --project src/RuletaObs.App
```

Abre:

- Configuración: http://127.0.0.1:47832/
- Panel: http://127.0.0.1:47832/panel

Para desarrollo puedes cambiar datos y puerto:

```powershell
$env:RULETA_DATA_DIR = "$PWD/.data"
$env:RULETA_URL = "http://127.0.0.1:47832"
dotnet run --project src/RuletaObs.App
```

### Publicar el .exe único

```powershell
dotnet publish src/RuletaObs.App/RuletaObs.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:DebugType=None -o artifacts/publish
```

El resultado es `artifacts/publish/RuletaObs.exe`. Para compilar el instalador necesitas Inno Setup 6 y ejecutar `ISCC.exe installer/RuletaObs.iss` después de publicar.

### App de Twitch del proyecto

La aplicación usa una única app pública de Twitch para todos los creadores, con Device Code Flow: no hay Client Secret en el ejecutable ni lo debe haber.

Configuración en https://dev.twitch.tv/console/apps:

- Nombre sin la palabra Twitch, por ejemplo `Ruleta OBS Overlay`.
- Client Type: **Public**.
- OAuth Redirect URL: `http://localhost` (valor de respaldo; Device Code Flow no redirige).
- Categoría: Broadcaster Suite.

Scopes solicitados: `channel:manage:redemptions`, `channel:read:redemptions` y `bits:read`.

Para que el .exe oficial ya traiga el Client ID, define la variable de repositorio de GitHub Actions `TWITCH_CLIENT_ID`. Los workflows la inyectan al compilar. Sin esa variable, el asistente permite pegar el Client ID una vez (nunca pidas ni pegues contraseñas, códigos 2FA o Client Secrets de apps confidenciales).

### Pipeline

- `.github/workflows/ci.yml`: en push/PR a `main`, compila, corre tests, publica el .exe y compila el instalador como artefacto.
- `.github/workflows/release.yml`: al crear un tag `v*`, repite la verificación y adjunta `RuletaObs.exe` y `RuletaObs-Setup.exe` a la GitHub Release.

### Consumo objetivo

- Menos de 80 MB de RAM en reposo para `RuletaObs.exe`.
- Menos de 1% de CPU promedio sin girar.
- Sin Electron ni otro Chromium: la animación vive en la fuente de navegador que OBS ya tiene.

## Limitaciones conocidas

- Puntos del canal y bits requieren canal afiliado o partner.
- La app debe estar abierta durante el directo para recibir eventos.
- Si una llamada a Twitch falla al confirmar/cancelar, el estado local queda guardado y marcado para sincronización pendiente.
- TikTok no es parte de la v1.
