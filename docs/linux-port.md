# Linux port

Markit `v0.1.0` es una aplicacion WPF para Windows. WPF depende de Windows Desktop y no puede publicarse como ejecutable nativo para Linux cambiando solamente el runtime de `dotnet publish`.

Para que Markit sea exportable e instalable en Linux, el proyecto necesita una evolucion progresiva hacia una base multiplataforma.

## Objetivo

Construir una variante instalable de Markit para Linux sin romper la version Windows existente.

La version Linux debe conservar el proposito del producto:

- abrir archivos Markdown locales;
- leer con formato visual;
- ajustar zoom;
- buscar dentro del documento;
- resaltar contenido para estudiar;
- usar modo claro y oscuro;
- funcionar sin depender de herramientas online ni IDEs.

## Estado actual

| Componente | Estado | Plataforma |
|---|---|---|
| `ReadmeReader` | App WPF funcional | Windows |
| `Markit.Core` | Base compartida inicial | Multiplataforma |
| `Markit.slnx` | Solucion del repo | Multiplataforma |
| Instalador Windows | Funcional | Windows |
| Instalador Linux | Pendiente | Linux |

## Primer avance realizado

Se creo `Markit.Core`, una libreria `net8.0` sin dependencia de WPF. Esta libreria empieza a concentrar logica reutilizable por futuras interfaces.

Actualmente contiene:

- lectura de archivos;
- escritura de archivos;
- validacion de extensiones soportadas;
- filtros de archivos;
- descripcion de errores de archivo.

La app WPF `ReadmeReader` ahora referencia `Markit.Core`.

## Camino recomendado

### Etapa 1: separar nucleo compartido

Mover gradualmente a `Markit.Core` toda logica que no dependa de WPF:

- operaciones de archivos;
- extensiones soportadas;
- persistencia de configuracion independiente de plataforma;
- transformaciones de Markdown;
- logica de resaltado sobre texto Markdown;
- busqueda sobre contenido Markdown.

### Etapa 2: definir UI multiplataforma

Evaluar una tecnologia de escritorio compatible con Linux.

Opcion principal recomendada:

- Avalonia UI: mantiene C#/.NET, soporta Windows/Linux/macOS y encaja bien con una evolucion desde WPF.

Alternativas posibles:

- una app web local empaquetada;
- Electron/Tauri con frontend web;
- una interfaz GTK/Qt separada.

La decision deberia priorizar:

- instalacion simple;
- buena experiencia de lectura;
- soporte de tema claro/oscuro;
- rendimiento suficiente con documentos largos;
- mantenimiento razonable para un proyecto open source chico.

### Etapa 3: crear app Linux

Crear una nueva aplicacion, por ejemplo:

```text
Markit.Desktop/
```

Esa app deberia consumir `Markit.Core` y replicar primero el flujo minimo:

1. abrir archivo Markdown;
2. renderizar contenido;
3. zoom;
4. modo claro/oscuro;
5. busqueda;
6. resaltado;
7. guardar cambios.

### Etapa 4: empaquetado Linux

Cuando exista una UI compatible con Linux, generar artefactos instalables:

- AppImage para distribucion simple;
- `.deb` para Debian/Ubuntu;
- `.rpm` para Fedora/openSUSE;
- tarball portable como fallback.

## No objetivos inmediatos

- No eliminar la app WPF actual.
- No romper el instalador Windows.
- No migrar toda la interfaz en un solo cambio.
- No prometer instalador Linux hasta tener una UI compatible.

## Criterio de exito

El port Linux se considera inicial cuando exista una app que pueda abrirse en Linux y permita leer un Markdown local con formato, zoom y modo claro/oscuro.

El port Linux se considera instalable cuando ademas exista al menos un artefacto distribuible, como AppImage o `.deb`.
