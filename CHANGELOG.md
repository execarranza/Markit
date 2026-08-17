# Changelog

Todos los cambios importantes de Markit se documentaran en este archivo.

El proyecto sigue versionado semantico de forma simple:

- `MAJOR`: cambios incompatibles o redisenos grandes.
- `MINOR`: nuevas funcionalidades compatibles.
- `PATCH`: correcciones y mejoras pequenas.

## [Unreleased]

### Documentacion de producto

- Se amplio el posicionamiento de Markit como lector de apuntes Markdown generados por IA.
- Se documento el valor de Markdown como formato liviano, legible y amigable para flujos con IA.
- Se reescribio la seccion "Por que nace" con foco en el flujo generar, estudiar, resaltar y reutilizar Markdown.
- Se agregaron evolutivos vinculados a resaltados, estudio y preparacion de contexto para IA.

### UI

- Se modernizo el modo claro con una paleta blanca/azul/cian alineada con la identidad visual de Markit.
- Se mantuvo el modo oscuro con la paleta oscura anterior para conservar comodidad de lectura.

### Exportacion

- Se agrego la opcion `Exportar PDF` para generar una copia de lectura del Markdown actual.
- La exportacion conserva estructura basica de lectura: titulos, parrafos, listas, citas, codigo, tablas simples y resaltados visuales.
- Se mejoro el formato PDF de tablas, vinetas, listas numeradas y resaltados pastel.

### Distribucion

- Se dejo `dist/MarkitInstaller.exe` versionado en el repositorio para que pueda instalarse al descargar el codigo.

### Preparacion multiplataforma

- Se agrego `Markit.Core` como primera libreria compartida `net8.0`.
- Se movio la logica de archivos y extensiones soportadas a `Markit.Core`.
- Se agrego `Markit.slnx` para ordenar los proyectos del repositorio.
- Se documento el plan tecnico de port a Linux en `docs/linux-port.md`.

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
