# Fix Vanguard

Utilidades para gestionar **Riot Vanguard** (arrancar sus servicios y
desinstalarlo por completo) desde una UI oscura en español, con auto-update
remoto controlado por ti a través de GitHub Releases.

El proyecto tiene tres piezas:

| Carpeta                | Qué es                                                                                             |
|------------------------|----------------------------------------------------------------------------------------------------|
| `FixVanguard/`         | Loader / UI que se distribuye a los usuarios. Arranca vgk/vgc, desinstala, narra VALORANT.        |
| `FixVanguardUpdater/`  | Herramienta interna. La usas **tú** para publicar nuevas versiones a GitHub Releases.              |
| `Emulator/`            | Simulador C++ educativo (backend, service, kernel_sim, game) del pipeline de un anti-cheat.        |

Los `.exe` distribuibles están siempre en:

```
dist/
├── FixVanguard.exe         ← Este es el que le pasas a los usuarios (Discord, web).
└── FixVanguardUpdater.exe  ← Este te lo quedas tú. NO lo pases a nadie.
```

Ambos son **single-file self-contained**: no necesitan .NET instalado, no
necesitan carpetas al lado, doble click y funcionan.

---

## Qué hace `FixVanguard.exe`

UI oscura con:

- **Estado en vivo** de Vanguard: instalación, servicio kernel `vgk`, servicio
  cliente `vgc`, bandeja `vgtray`. Punto verde/rojo por cada uno.
- **Iniciar Vanguard**: cierra Riot, configura los servicios, arranca `vgk`
  y `vgc` esperando de verdad al estado *Running*, lanza la bandeja, y si todo
  está en línea vuelve a abrir Riot Client automáticamente.
- **Desinstalar Vanguard**: cierra Riot y todos sus procesos, detiene y
  elimina los servicios, ejecuta el desinstalador oficial de Riot Vanguard
  y limpia lo que queda en `C:\Program Files\Riot Vanguard`. Después reabre
  Riot Client.
- **Consola narrada** con los eventos en español y scrollbar morada.
- **Narrador de VALORANT**: mientras el juego está abierto, la consola dice
  `VALORANT detectado`, `En lobby detectado`, `Seleccionando agente`,
  `Agente seleccionado: <nombre>`, `Iniciando partida en <mapa> con <agente>`.
- **Check de actualizaciones** al arrancar: hace un GET al `manifest.json`.
  Si hay una versión más nueva que la instalada:
  - La consola escribe `Loader version X — new version Y — go to Discord for new download.`
  - Se abre un popup con el mismo mensaje y un botón que lleva a Discord.
  - Los botones Iniciar/Desinstalar quedan **deshabilitados**.
  - Es decir, el loader viejo deja de funcionar.

Requiere privilegios de administrador (declarado en su `app.manifest`).

---

## Qué hace `FixVanguardUpdater.exe`

Herramienta que **solo tú** ejecutas. Con un click sube el nuevo `.exe` a
GitHub Releases y publica el `manifest.json` en el repo, de forma que todos
los loaders antiguos vean el aviso al abrir.

La UI pide, en el mismo orden:

- **Ruta del `FixVanguard.exe` nuevo** (botón *Buscar*).
- **Versión** (`1.1.0`).
- **Versión mínima soportada** (normalmente = la nueva; los loaders con
  versión inferior quedan bloqueados).
- **URL de Discord** (opcional; el loader la abre desde el popup).
- **Mensaje corto** para el aviso (opcional).
- **Notas del release** (multi-línea, van en el release de GitHub y en el
  manifest).
- **Kill-switch** (checkbox): bloquea todos los loaders al arrancar,
  incluso el que acabas de publicar. Útil para pánico.
- **Owner / Repo / Branch / Ruta del manifest / Nombre del asset**
  (por defecto `main`, `manifest.json`, `FixVanguard.exe`).
- **GitHub Token** (Personal Access Token con scope `repo`).

Todos los campos se guardan en `%APPDATA%\FixVanguardUpdater\settings.json`
así que solo cambias la versión y la ruta del `.exe` en cada release.

Al pulsar **Publicar release + manifest**:

1. Verifica el acceso al repo con el token.
2. Calcula el SHA-256 del `.exe`.
3. Crea el release `v<versión>` (o reusa el que ya exista con ese tag).
4. Borra el asset antiguo con el mismo nombre si estaba.
5. Sube el `.exe` como asset del release.
6. Construye el `manifest.json` con la URL del asset, el hash, tu Discord,
   el kill-switch, y lo commitea en el repo con la Contents API.
7. Copia al portapapeles la URL raw del `manifest.json`.

---

## Setup inicial (solo una vez)

### 1) Repo en GitHub

Crea el repo (por ejemplo `danma/FixVanguard`). Puede ser **privado**
mientras el `manifest.json` sea accesible desde una URL raw que el loader
pueda leer sin auth. Lo simple es hacerlo **público**.

### 2) Personal Access Token (PAT)

- GitHub → *Settings* → *Developer settings* → *Personal access tokens* →
  **Tokens (classic)** → *Generate new token (classic)*.
- Scope: **`repo`** (marca la casilla padre para incluir contents + releases).
- Copia el token (empieza por `ghp_…`). Es lo que meterás en el Updater.

### 3) URL del `manifest.json`

Será siempre esta forma:

```
https://raw.githubusercontent.com/<owner>/<repo>/<branch>/manifest.json
```

Ejemplo: `https://raw.githubusercontent.com/danma/FixVanguard/main/manifest.json`

### 4) Pon la URL en el loader

Abre `FixVanguard\UpdateChecker.cs` y cambia la constante:

```csharp
public const string ManifestUrl = "https://raw.githubusercontent.com/<owner>/<repo>/main/manifest.json";
```

Luego republica el loader (ver *Compilar* abajo).

### 5) Primera publicación

Abre `dist\FixVanguardUpdater.exe`, rellena todos los campos (versión
`1.0.0`, ruta del `dist\FixVanguard.exe` inicial, tu token, tu Discord,
etc.), y pulsa **Publicar release + manifest**. Con eso el repo queda
inicializado: primer release con el `.exe` y primer `manifest.json`.

A partir de ahí ya solo mantienes el ciclo de arriba.

---

## Ciclo cuando saques una versión nueva

1. Edita `FixVanguard\FixVanguard.csproj` y sube `<Version>`:

   ```xml
   <Version>1.1.0</Version>
   <AssemblyVersion>1.1.0.0</AssemblyVersion>
   <FileVersion>1.1.0.0</FileVersion>
   ```

2. Compila en Release single-file:

   ```powershell
   cd c:\Users\danma\source\repos\FixVanguard
   dotnet publish FixVanguard\FixVanguard.csproj -c Release -r win-x64 `
     --self-contained true -p:PublishSingleFile=true `
     -p:IncludeNativeLibrariesForSelfExtract=true `
     -p:EnableCompressionInSingleFile=true -p:DebugType=embedded `
     -o dist
   ```

   Sale un `dist\FixVanguard.exe` fresco de ~50 MB.

3. Abre `dist\FixVanguardUpdater.exe`:
   - **Ruta**: `dist\FixVanguard.exe`.
   - **Versión**: `1.1.0`.
   - **Versión mínima soportada**: `1.1.0` (así todo lo anterior queda
     bloqueado).
   - El resto se conserva de la sesión pasada.
   - **Publicar release + manifest**.

4. Sube el link del release a Discord (o linkéalo desde tu web).

5. Cualquier loader viejo, al abrirse:
   - Ve `latest_version = 1.1.0` en el manifest.
   - Su propia versión es `1.0.0` < `min_supported_version = 1.1.0`.
   - Muestra el popup **Loader desactualizado — Loader version 1.0.0 —
     New version 1.1.0 — Go to Discord for the new download**.
   - Botones Iniciar/Desinstalar bloqueados.

---

## Kill-switch de emergencia

Si sacaste una versión y por lo que sea quieres **matar todos los loaders
ahora mismo, incluso el más nuevo**, sin recompilar nada:

1. Abre `FixVanguardUpdater.exe`.
2. Marca **Kill-switch**.
3. **Publicar release + manifest**.

El `manifest.json` queda con `"kill_switch": true` y cualquier loader
que arranque, sea la versión que sea, se bloquea. Cuando quieras
levantarlo desmarcas el checkbox y vuelves a publicar.

---

## Compilar desde código

Requisitos: **.NET 10 SDK** en Windows.

### Loader (`FixVanguard.exe`)

```powershell
cd c:\Users\danma\source\repos\FixVanguard
dotnet publish FixVanguard\FixVanguard.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=embedded `
  -o dist
```

### Updater (`FixVanguardUpdater.exe`)

```powershell
cd c:\Users\danma\source\repos\FixVanguard
dotnet publish FixVanguardUpdater\FixVanguardUpdater.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=embedded `
  -o dist
```

### Los dos a la vez

```powershell
cd c:\Users\danma\source\repos\FixVanguard
dotnet publish FixVanguard\FixVanguard.csproj                -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=embedded -o dist
dotnet publish FixVanguardUpdater\FixVanguardUpdater.csproj  -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=embedded -o dist
```

---

## Estructura del `manifest.json`

Es un JSON plano en la raíz del repo (o donde configures). Ejemplo:

```json
{
  "latest_version": "1.1.0",
  "min_supported_version": "1.1.0",
  "download_url": "https://github.com/danma/FixVanguard/releases/download/v1.1.0/FixVanguard.exe",
  "sha256": "57e9e9cb16ebe1d76c8526f5c81ed93bb99f9ac84f0ddee263be0a9922245a8e",
  "notes": "Se corrige X, se añade Y.",
  "kill_switch": false,
  "message": "Descarga la nueva versión desde Discord.",
  "discord_url": "https://discord.gg/tu-invite"
}
```

Cómo lo interpreta el loader:

| Condición                                         | Estado    | Efecto                                                       |
|--------------------------------------------------|-----------|--------------------------------------------------------------|
| `current >= latest_version`                       | UpToDate  | Sigue normal.                                                |
| `current < min_supported_version`                 | Required  | Bloquea la UI, popup con link a Discord.                     |
| `current < latest_version` (≥ min)                | Optional  | Igual que Required en este build: bloquea y avisa.           |
| `kill_switch: true`                               | Blocked   | Bloquea la UI, popup con link a Discord.                     |
| Sin red / manifest roto / dominio caído          | Failed    | Warning en consola; el loader sigue funcionando.             |

---

## Nota sobre el `Emulator/`

`Emulator/` es un simulador C++ 100% independiente que **no toca VALORANT ni
Vanguard**. Está pensado para estudiar la arquitectura de un anti-cheat
moderno en laboratorio: componente user-mode, kernel simulado, backend con
attestación por HMAC, IPC por named pipes, integridad de módulos, etc.

Se compila con CMake + MSVC:

```powershell
cd c:\Users\danma\source\repos\FixVanguard\Emulator
cmake -S . -B build
cmake --build build --config Release
```

Los binarios acaban en `Emulator\build\Release\` y los logs de cada
componente en `Emulator\logs\` en formato JSON-line.

---

## Solución de problemas

- **"vgc no arrancó"**: el loader espera hasta 25 s a que llegue a
  RUNNING. Si sigue en pending suele bastar reiniciar el PC.
- **"No pude aplicar la actualización"**: en este flujo el loader **no**
  auto-descarga; solo avisa y bloquea. La descarga va por Discord.
- **La app no arranca**: comprueba que se lanza como administrador (el
  UAC salta al abrir el `.exe`).
- **`Publicar release + manifest` da 401**: el token está mal o caducó.
  Genera uno nuevo con scope `repo` clásico.
- **`Publicar release + manifest` da 404**: comprueba owner/repo/branch;
  el repo debe existir y el token debe tener acceso.
