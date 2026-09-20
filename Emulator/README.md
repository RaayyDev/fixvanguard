# acsim — anti-cheat de laboratorio

Simulador educativo de la arquitectura de un anti-cheat tipo Vanguard, todo
en user-mode, sin tocar Vanguard ni Valorant y sin tecnicas de evasion.

## Componentes

| Binario              | Rol                                                  |
|----------------------|------------------------------------------------------|
| `acsim_game.exe`     | Proceso "protegido" de mentira.                     |
| `acsim_kernel_sim`   | Imita el driver: enumera procesos, hashea modulos.  |
| `acsim_service`      | Recoge trust, hace handshake y reenvia eventos.     |
| `acsim_backend`      | Servidor TCP con nonce/HMAC y politica de sesion.   |

## Compilar (Visual Studio 2022)

```powershell
cmake -S . -B build -G "Visual Studio 17 2022" -A x64
cmake --build build --config Release
```

Los binarios quedan en `build/Release/`.

## Ejecutar (4 terminales)

```powershell
build\Release\acsim_backend.exe
build\Release\acsim_kernel_sim.exe
build\Release\acsim_service.exe
build\Release\acsim_game.exe
```

Los logs quedan en `logs/*.log` en formato JSON-linea.

## Que pasa

1. `acsim_service` recoge Secure Boot, TPM, debugger, test-signing, HVCI, DSE.
2. Se conecta al backend, recibe un `nonce`, devuelve `HMAC(key, nonce || trust)`.
3. El backend valida la politica y emite un token de sesion.
4. `acsim_service` se conecta a `acsim_kernel_sim` por named pipe.
5. `acsim_kernel_sim` publica eventos de procesos y modulos, con alertas si
   ve algo de la denylist de laboratorio.
6. `acsim_service` hashea modulos del juego cada 5 s y envia integridad al backend.
