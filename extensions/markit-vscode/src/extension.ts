import * as path from "node:path";
import MarkdownIt from "markdown-it";
import taskLists from "markdown-it-task-lists";
import sanitizeHtml from "sanitize-html";
import * as vscode from "vscode";

const AUTO_OPEN_SETTING = "openInReaderByDefault";
let suppressedAutoOpenUri: string | undefined;

export function activate(context: vscode.ExtensionContext): void {
  context.subscriptions.push(
    vscode.commands.registerCommand("markit.openReader", () => openReader(context.extensionUri)),
    vscode.commands.registerCommand("markit.toggleReader", () => {
      if (ReaderPanel.currentPanel) {
        ReaderPanel.currentPanel.dispose();
        return;
      }

      void openReader(context.extensionUri);
    }),
    vscode.commands.registerCommand("markit.toggleAutoOpen", () => toggleAutoOpen(context.extensionUri)),
    vscode.window.onDidChangeActiveTextEditor((editor) => openAutomatically(context.extensionUri, editor)),
    vscode.workspace.onDidChangeConfiguration((event) => {
      if (event.affectsConfiguration(`markit.${AUTO_OPEN_SETTING}`)) {
        ReaderPanel.currentPanel?.updateAutoOpenState(isAutoOpenEnabled());
      }
    })
  );

  openAutomatically(context.extensionUri, vscode.window.activeTextEditor);
}

export function deactivate(): void {
  ReaderPanel.currentPanel?.dispose();
}

async function openReader(extensionUri: vscode.Uri): Promise<void> {
  const editor = vscode.window.activeTextEditor;

  if (!editor || editor.document.languageId !== "markdown") {
    void vscode.window.showInformationMessage("Abri un archivo Markdown para usar el modo lectura de Markit.");
    return;
  }

  ReaderPanel.createOrShow(
    extensionUri,
    editor.document,
    vscode.ViewColumn.Beside,
    editor.viewColumn ?? vscode.ViewColumn.One
  );
}

async function toggleAutoOpen(
  extensionUri: vscode.Uri,
  enabled = !isAutoOpenEnabled(),
  showConfirmation = true
): Promise<void> {
  await vscode.workspace
    .getConfiguration("markit")
    .update(AUTO_OPEN_SETTING, enabled, vscode.ConfigurationTarget.Global);

  if (showConfirmation) {
    void vscode.window.showInformationMessage(
      enabled
        ? "Markit abrira automaticamente los archivos Markdown en modo lectura."
        : "Apertura automatica de Markit desactivada."
    );
  }

  if (enabled) {
    openAutomatically(extensionUri, vscode.window.activeTextEditor);
  }
}

function openAutomatically(extensionUri: vscode.Uri, editor: vscode.TextEditor | undefined): void {
  if (!isAutoOpenEnabled() || !editor || editor.document.languageId !== "markdown") {
    return;
  }

  if (ReaderPanel.currentPanel?.isShowingDocument(editor.document)) {
    return;
  }

  const documentUri = editor.document.uri.toString();
  if (suppressedAutoOpenUri === documentUri) {
    suppressedAutoOpenUri = undefined;
    return;
  }

  suppressedAutoOpenUri = undefined;
  const sourceViewColumn = editor.viewColumn ?? vscode.ViewColumn.Active;
  ReaderPanel.createOrShow(extensionUri, editor.document, sourceViewColumn, sourceViewColumn);
}

function isAutoOpenEnabled(): boolean {
  return vscode.workspace.getConfiguration("markit").get<boolean>(AUTO_OPEN_SETTING, false);
}

class ReaderPanel {
  public static currentPanel: ReaderPanel | undefined;

  private readonly panel: vscode.WebviewPanel;
  private readonly extensionUri: vscode.Uri;
  private readonly disposables: vscode.Disposable[] = [];
  private document: vscode.TextDocument;
  private sourceViewColumn: vscode.ViewColumn;

  public static createOrShow(
    extensionUri: vscode.Uri,
    document: vscode.TextDocument,
    viewColumn: vscode.ViewColumn,
    sourceViewColumn: vscode.ViewColumn
  ): void {
    if (ReaderPanel.currentPanel) {
      ReaderPanel.currentPanel.setDocument(document, sourceViewColumn);
      ReaderPanel.currentPanel.panel.reveal(viewColumn);
      return;
    }

    const panel = vscode.window.createWebviewPanel(
      "markit.reader",
      `Markit: ${path.basename(document.fileName)}`,
      viewColumn,
      {
        enableScripts: true,
        retainContextWhenHidden: true,
        localResourceRoots: resourceRoots(extensionUri, document)
      }
    );

    ReaderPanel.currentPanel = new ReaderPanel(panel, extensionUri, document, sourceViewColumn);
  }

  private constructor(
    panel: vscode.WebviewPanel,
    extensionUri: vscode.Uri,
    document: vscode.TextDocument,
    sourceViewColumn: vscode.ViewColumn
  ) {
    this.panel = panel;
    this.extensionUri = extensionUri;
    this.document = document;
    this.sourceViewColumn = sourceViewColumn;
    this.panel.webview.html = this.getHtml();

    this.panel.onDidDispose(() => this.dispose(), null, this.disposables);
    this.panel.webview.onDidReceiveMessage(
      (message: { type?: string; enabled?: boolean }) => {
        if (message.type === "revealSource") {
          void this.revealSourceEditor();
        } else if (message.type === "setAutoOpen") {
          void toggleAutoOpen(this.extensionUri, message.enabled === true);
        }
      },
      null,
      this.disposables
    );

    vscode.workspace.onDidChangeTextDocument(
      (event) => {
        if (event.document.uri.toString() === this.document.uri.toString()) {
          void this.panel.webview.postMessage({ type: "render", html: this.renderMarkdown() });
        }
      },
      null,
      this.disposables
    );
  }

  public dispose(): void {
    if (ReaderPanel.currentPanel !== this) {
      return;
    }

    suppressedAutoOpenUri = this.document.uri.toString();
    ReaderPanel.currentPanel = undefined;
    this.panel.dispose();

    while (this.disposables.length > 0) {
      this.disposables.pop()?.dispose();
    }
  }

  public updateAutoOpenState(enabled: boolean): void {
    void this.panel.webview.postMessage({ type: "autoOpenState", enabled });
  }

  public isShowingDocument(document: vscode.TextDocument): boolean {
    return this.document.uri.toString() === document.uri.toString();
  }

  private async revealSourceEditor(): Promise<void> {
    if (isAutoOpenEnabled()) {
      await toggleAutoOpen(this.extensionUri, false, false);
    }

    await vscode.window.showTextDocument(this.document, {
      viewColumn: this.sourceViewColumn,
      preserveFocus: false
    });
  }

  private setDocument(document: vscode.TextDocument, sourceViewColumn: vscode.ViewColumn): void {
    this.document = document;
    this.sourceViewColumn = sourceViewColumn;
    this.panel.title = `Markit: ${path.basename(document.fileName)}`;
    this.panel.webview.options = {
      enableScripts: true,
      localResourceRoots: resourceRoots(this.extensionUri, document)
    };
    this.panel.webview.html = this.getHtml();
  }

  private renderMarkdown(): string {
    const markdown = new MarkdownIt({ html: true, linkify: true, typographer: true });
    markdown.use(taskLists, { enabled: false });
    const fallbackImageRule = markdown.renderer.rules.image;

    markdown.renderer.rules.image = (tokens, index, options, env, renderer) => {
      const token = tokens[index];
      const source = token.attrGet("src");

      if (source && this.document.uri.scheme === "file" && isRelativeResource(source)) {
        const absoluteImage = vscode.Uri.file(path.resolve(path.dirname(this.document.uri.fsPath), source));
        token.attrSet("src", this.panel.webview.asWebviewUri(absoluteImage).toString());
      }

      return fallbackImageRule
        ? fallbackImageRule(tokens, index, options, env, renderer)
        : renderer.renderToken(tokens, index, options);
    };

    const source = this.document.getText();
    return source.trim()
      ? sanitizeRenderedMarkdown(markdown.render(source))
      : '<section class="empty-state"><h1>Documento vacio</h1><p>Escribi contenido Markdown en el editor y aparecera aca.</p></section>';
  }

  private getHtml(): string {
    const webview = this.panel.webview;
    const styleUri = webview.asWebviewUri(vscode.Uri.joinPath(this.extensionUri, "media", "reader.css"));
    const scriptUri = webview.asWebviewUri(vscode.Uri.joinPath(this.extensionUri, "media", "reader.js"));
    const nonce = createNonce();
    const documentName = escapeHtml(path.basename(this.document.fileName));
    const autoOpenChecked = isAutoOpenEnabled() ? " checked" : "";

    return `<!doctype html>
<html lang="es">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src ${webview.cspSource} https: data:; style-src ${webview.cspSource} 'unsafe-inline'; script-src 'nonce-${nonce}';">
  <link rel="stylesheet" href="${styleUri}">
  <title>Markit: ${documentName}</title>
</head>
<body data-theme="system">
  <header class="reader-toolbar" aria-label="Controles del modo lectura">
    <div class="document-identity">
      <span class="markit-symbol" aria-hidden="true">m</span>
      <span class="document-name" title="${documentName}">${documentName}</span>
    </div>
    <div class="reader-actions">
      <button type="button" id="sourceButton" title="Volver al editor">Editor</button>
      <label class="auto-control" title="Abrir Markdown automaticamente con Markit">
        <input type="checkbox" id="autoOpenToggle"${autoOpenChecked}>
        <span class="auto-switch" aria-hidden="true"></span>
        <span>Auto</span>
      </label>
      <div class="zoom-control" role="group" aria-label="Zoom de lectura">
        <button type="button" id="zoomOut" aria-label="Reducir zoom" title="Reducir zoom (Ctrl+-)">-</button>
        <button type="button" id="zoomReset" aria-label="Restablecer zoom" title="Restablecer zoom (Ctrl+0)">100%</button>
        <button type="button" id="zoomIn" aria-label="Aumentar zoom" title="Aumentar zoom (Ctrl++)">+</button>
      </div>
      <label class="theme-control">
        <span class="sr-only">Tema</span>
        <select id="themeSelect" aria-label="Tema de lectura" title="Tema de lectura">
          <option value="system">Sistema</option>
          <option value="light">Claro</option>
          <option value="dark">Oscuro</option>
        </select>
      </label>
    </div>
  </header>
  <main id="content" class="markdown-body" tabindex="-1">${this.renderMarkdown()}</main>
  <script nonce="${nonce}" src="${scriptUri}"></script>
</body>
</html>`;
  }
}

function resourceRoots(extensionUri: vscode.Uri, document: vscode.TextDocument): vscode.Uri[] {
  const roots = [extensionUri];
  if (document.uri.scheme === "file") {
    roots.push(vscode.Uri.file(path.dirname(document.uri.fsPath)));
  }
  return roots;
}

function isRelativeResource(source: string): boolean {
  return !/^(?:[a-z]+:|\/|\\|#)/i.test(source);
}

function createNonce(): string {
  const characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
  return Array.from({ length: 32 }, () => characters.charAt(Math.floor(Math.random() * characters.length))).join("");
}

function escapeHtml(value: string): string {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function sanitizeRenderedMarkdown(html: string): string {
  return sanitizeHtml(html, {
    allowedTags: sanitizeHtml.defaults.allowedTags.concat(["img", "input", "mark"]),
    allowedAttributes: {
      a: ["href", "name", "target"],
      img: ["src", "alt", "title"],
      code: ["class"],
      input: ["checked", "class", "disabled", "type"],
      li: ["class"],
      mark: ["style"],
      ul: ["class"]
    },
    allowedStyles: {
      mark: {
        "background-color": [/^#[0-9a-f]{3,8}$/i, /^rgba?\([\d\s,.%]+\)$/i]
      }
    },
    allowedSchemes: ["http", "https", "mailto"],
    allowedSchemesByTag: {
      img: ["http", "https", "data"]
    },
    allowProtocolRelative: false
  });
}
