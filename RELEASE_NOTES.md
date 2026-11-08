# Notas de versión

## 1.0.0 — 2026-11-08

Primera versión pública de ApagadoProgramado.

### Incluye

- Programación del apagado de Windows por hora y días de la semana.
- Servicio de Windows que ejecuta el apagado aunque no haya una sesión iniciada.
- Aviso con cuenta atrás en el área de notificación y opción para cancelar el apagado desde cualquier sesión.
- Panel de configuración para administradores, con elevación de permisos mediante UAC.
- Instalación, reparación y desinstalación del servicio, incluida la opción de conservar los archivos de la aplicación.
- Configuración y registro persistentes en `C:\ProgramData\ApagadoProgramado\`.
- Comandos `--install` y `--uninstall` para administrar el servicio desde la línea de comandos.

### Importante

El apagado cierra a la fuerza todas las sesiones. Guarda los documentos y el trabajo pendiente antes de activar la programación.
