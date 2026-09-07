# Markit para Visual Studio Code

Complemento opcional de Markit que abre el archivo Markdown activo en una vista de lectura limpia y responsive dentro de VS Code.

## Funciones del MVP 0.2.2

- Abrir el Markdown activo en modo lectura desde el titulo del editor, el menu contextual o la paleta de comandos.
- Activar o desactivar la vista con `Ctrl+Alt+M` (`Cmd+Alt+M` en macOS).
- Actualizar la lectura mientras se modifica el archivo.
- Zoom entre 70% y 220% con controles o `Ctrl++`, `Ctrl+-` y `Ctrl+0`.
- Elegir tema del sistema, claro u oscuro.
- Renderizar tablas, listas, codigo, citas, enlaces e imagenes locales.
- Renderizar listas de tareas `[x]` y `[ ]` como casillas de lectura.
- Mostrar de forma segura los resaltados guardados por Markit Desktop.
- Mantener el contenido centrado y adaptado al ancho disponible.
- Abrir automaticamente el lector al entrar en un Markdown cuando la preferencia `Auto` esta activa.

## Instalar el VSIX

1. Abrir VS Code.
2. Abrir **Extensiones** con `Ctrl+Shift+X`.
3. Abrir el menu `...` del panel Extensiones.
4. Elegir **Instalar desde VSIX...**.
5. Seleccionar `dist/Markit-VSCode-0.2.2.vsix` desde la raiz del repositorio.
6. Reiniciar VS Code si lo solicita.

Tambien se puede instalar desde una terminal:

```powershell
code --install-extension .\dist\Markit-VSCode-0.2.2.vsix
```

## Probar durante el desarrollo

1. Abrir en VS Code la carpeta `extensions/markit-vscode`.
2. Ejecutar `npm install` una vez.
3. Presionar `F5`.
4. VS Code abrira una segunda ventana llamada **Extension Development Host**.
5. En esa ventana, abrir un archivo `.md`.
6. Ejecutar **Markit: Abrir modo lectura** desde `Ctrl+Shift+P`.

La segunda ventana es un entorno aislado para probar la extension sin instalarla en el VS Code principal.

## Apertura automatica

Se puede activar o desactivar de tres maneras:

- usar el interruptor **Auto** dentro de la vista de lectura;
- ejecutar **Markit: Activar/desactivar apertura automatica** desde `Ctrl+Shift+P`;
- abrir Configuracion con `Ctrl+,`, buscar `Markit` y cambiar **Open In Reader By Default**.

La preferencia se guarda en VS Code y se conserva al reiniciar. Cuando esta activa, el Markdown y su lectura quedan como pestañas navegables. Al pulsar **Editor**, Markit desactiva `Auto` y vuelve al archivo fuente para evitar ciclos. Cuando esta desactivada, los Markdown se abren en el editor normal y el lector sigue disponible manualmente.

## Como esta organizada

```text
markit-vscode/
|-- package.json          Metadatos, comandos, menus y atajos
|-- src/extension.ts      Entrada y logica de integracion con VS Code
|-- media/reader.css      Apariencia responsive del lector
|-- media/reader.js       Zoom, tema y mensajes del Webview
|-- .vscode/launch.json   Configuracion para ejecutar con F5
|-- esbuild.js            Empaquetado de TypeScript y markdown-it
`-- dist/extension.js     Codigo compilado de la extension
```

## Conceptos para aprender

- **Extension Host:** proceso separado donde VS Code ejecuta la logica del complemento.
- **Comando:** accion registrada que aparece en la paleta, menus o atajos.
- **Webview:** pagina HTML aislada dentro de una pestaña de VS Code.
- **Content Security Policy:** limita scripts y recursos para evitar ejecutar contenido peligroso de un Markdown.
- **VSIX:** paquete instalable de una extension, equivalente al artefacto de distribucion.

## Compilar y empaquetar

```powershell
npm run check
npm run compile
npm run package
```

El ultimo comando genera `dist/Markit-VSCode-0.2.2.vsix` en la raiz del repositorio.

Markit Desktop sigue siendo el producto independiente para leer y estudiar sin depender de un IDE. Esta extension es un complemento para quienes ya trabajan dentro de Visual Studio Code.
