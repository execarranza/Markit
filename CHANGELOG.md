# Changelog

Todos los cambios importantes de Markit se documentaran en este archivo.

El proyecto sigue versionado semantico de forma simple:

- `MAJOR`: cambios incompatibles o redisenos grandes.
- `MINOR`: nuevas funcionalidades compatibles.
- `PATCH`: correcciones y mejoras pequenas.

## [0.1.0] - 2026-07-21

### Linea base inicial

- Primera version funcional de Markit para Windows.
- Lector de archivos Markdown `.md`, `.markdown`, `.mdown` y `.txt`.
- Renderizado visual de titulos, parrafos, listas, tablas, citas, codigo, enlaces, imagenes locales y resaltados HTML `<mark>`.
- Zoom de lectura, pantalla completa, scroll suavizado y ancho de lectura adaptativo.
- Modo claro y oscuro.
- Busqueda con resaltado de coincidencias y navegacion.
- Resaltadores pastel y goma para quitar resaltados.
- Guardado, guardar como e impresion.
- Archivos recientes y persistencia local de preferencias.
- Instalador local para Windows con asociacion de archivos Markdown por usuario.
- Licencia MIT y documentacion inicial open source.

### Evolucion propuesta

- Preparar una version instalable en Linux.
- Evaluar una capa de interfaz multiplataforma sin perder el foco de lector simple.
- Separar progresivamente el motor de lectura/renderizado Markdown de la UI WPF.
