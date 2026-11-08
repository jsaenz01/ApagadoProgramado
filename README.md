# ApagadoProgramado

Aplicación libre para Windows que programa el apagado del equipo a una hora y en unos días determinados, incluso cuando no hay ninguna sesión iniciada. Se distribuye bajo la licencia **GNU GPL versión 3 o posterior**; consulta [LICENSE](./LICENSE).

> **Aviso:** el apagado fuerza el cierre de todas las sesiones y puede hacer que se pierdan datos que no se hayan guardado. Antes de activar la programación, guarda tu trabajo.

## Funciones

- Servicio de Windows que se ejecuta automáticamente con la cuenta `SYSTEM` y realiza el apagado programado.
- Icono en el área de notificación con cuenta atrás y opción para cancelar el apagado. Cualquier usuario puede cancelarlo; la cancelación afecta a todo el equipo.
- Panel de configuración para administradores, con solicitud de permisos mediante UAC.
- Instalación, reparación y desinstalación del servicio desde la aplicación.
- Configuración y registro guardados en `C:\ProgramData\ApagadoProgramado\`.

## Uso

1. Abre `ApagadoProgramado.exe` y configura la hora, los días y los minutos de aviso.
2. Pulsa **Instalar servicio** para guardar la configuración e instalar la aplicación en `C:\Program Files\ApagadoProgramado`. La instalación activa el apagado programado.
3. Usa el botón según el estado del servicio:
   - **Guardar cambios** guarda la configuración modificada.
   - **Desinstalar servicio** quita el servicio y el inicio automático. La casilla **Borrar programa de Program Files** determina si también se elimina la aplicación instalada.
   - **Reparar servicio** vuelve a habilitar e iniciar un servicio detenido o deshabilitado.

El panel de configuración requiere permisos de administrador. Los usuarios normales pueden cancelar un apagado desde el icono de notificación, pero no configurar el servicio.

También se puede administrar desde la línea de comandos:

```text
ApagadoProgramado.exe --install
ApagadoProgramado.exe --uninstall
ApagadoProgramado.exe --uninstall --keep-files
```

`--keep-files` conserva la aplicación en `C:\Program Files\ApagadoProgramado` al desinstalar.

## Descargar

Descarga el código fuente clonando el repositorio:

```sh
git clone https://github.com/jsaenz01/ApagadoProgramado.git
cd ApagadoProgramado
```

Si no usas Git, también puedes bajar el repositorio como ZIP desde GitHub (**Code → Download ZIP**) y descomprimirlo.

## Compilar

Se requiere el SDK de .NET 8. Para publicar una versión autocontenida para Windows x64:

```sh
dotnet publish -c Release -r win-x64 -o dist
```

El ejecutable resultante es `dist/ApagadoProgramado.exe`.

Si el SDK está instalado en `~/.dotnet` y no está en el `PATH` (por ejemplo, en Linux), añádelo antes de compilar:

```sh
export PATH=$PATH:/home/$USER/.dotnet
cd /home/$USER/Proyectos/ApagadoProgramado
dotnet publish -c Release -r win-x64 -o dist
```

## Contribuir

Las propuestas de mejora y los informes de errores son bienvenidos mediante issues o pull requests en el repositorio. Al contribuir, asegúrate de que tus cambios también puedan distribuirse bajo la licencia GPL-3.0-or-later.

## Licencia

Copyright (C) 2026 Jesús Sáenz.

Este programa está disponible bajo la GNU General Public License, versión 3 o, a tu elección, cualquier versión posterior. Consulta el archivo [LICENSE](./LICENSE) y el [texto oficial de la licencia](https://www.gnu.org/licenses/gpl-3.0.html).
