# Apagado programado

App para Windows que apaga el equipo a una hora configurable, aunque no haya ningún usuario con la sesión iniciada.

## Cómo funciona
- **Servicio de Windows** `ApagadoProgramado` (cuenta SYSTEM, arranque automático): a la hora configurada ejecuta `shutdown /s /f /t 0`, que apaga el equipo y cierra todas las sesiones.
- **Icono en la bandeja** (se inicia minimizado al iniciar sesión cualquier usuario, sin pedir permisos): muestra un aviso con cuenta atrás antes del apagado, con botón **Cancelar apagado**. Si cancela un usuario, el apagado se cancela para todos. Sin servicio instalado, el icono no se muestra.
- **Panel de configuración**: solo administradores. Al abrir el .exe (o "Configurar…" en el icono) salta el UAC automáticamente; no hace falta "ejecutar como administrador".
- Configuración y registro: `C:\ProgramData\ApagadoProgramado\` (`config.json`, `log.txt`).

## Uso
1. Abre `ApagadoProgramado.exe`, elige hora, días y minutos de aviso.
2. Pulsa **Instalar servicio**: guarda la configuración, copia la app a `C:\Program Files\ApagadoProgramado`, crea el servicio y el inicio automático. Instalar = activar el apagado.
3. El mismo botón cambia según el estado:
   - **Guardar cambios**: servicio instalado y hay cambios sin guardar.
   - **Desinstalar servicio**: servicio instalado y sin cambios. Elimina el servicio y el inicio automático. Junto al botón aparece el tick **Borrar programa de Program Files** (marcado por defecto): si lo desmarcas, el programa se conserva en `C:\Program Files\ApagadoProgramado`.
   - **Reparar servicio**: el servicio existe pero está detenido o deshabilitado.

## Permisos
- Solo los **administradores** ven "Configurar" y pueden abrir el panel. Además, `C:\ProgramData\ApagadoProgramado` es de solo lectura para usuarios normales.
- **Cancelar un apagado** puede hacerlo cualquier usuario; solo escribe en `ApagadoProgramado\cancel\`.

Por línea de comandos: `ApagadoProgramado.exe --install` / `--uninstall` (añade `--keep-files` para conservar el programa en Program Files).

## Compilar (requiere .NET 8 SDK)
```
dotnet publish -c Release -r win-x64 -o dist
```
