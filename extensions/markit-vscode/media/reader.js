(function () {
  const vscode = acquireVsCodeApi();
  const previousState = vscode.getState() || {};
  const state = {
    zoom: clamp(previousState.zoom || 100, 70, 220),
    theme: ["system", "light", "dark"].includes(previousState.theme) ? previousState.theme : "system"
  };

  const content = document.getElementById("content");
  const zoomReset = document.getElementById("zoomReset");
  const themeSelect = document.getElementById("themeSelect");
  const autoOpenToggle = document.getElementById("autoOpenToggle");

  function clamp(value, minimum, maximum) {
    return Math.min(maximum, Math.max(minimum, value));
  }

  function applyState() {
    document.documentElement.style.setProperty("--reader-scale", String(state.zoom / 100));
    document.body.dataset.theme = state.theme;
    zoomReset.textContent = `${state.zoom}%`;
    themeSelect.value = state.theme;
    vscode.setState(state);
  }

  function setZoom(nextZoom) {
    state.zoom = clamp(Math.round(nextZoom / 10) * 10, 70, 220);
    applyState();
  }

  document.getElementById("sourceButton").addEventListener("click", () => {
    vscode.postMessage({ type: "revealSource" });
  });
  document.getElementById("zoomOut").addEventListener("click", () => setZoom(state.zoom - 10));
  document.getElementById("zoomIn").addEventListener("click", () => setZoom(state.zoom + 10));
  zoomReset.addEventListener("click", () => setZoom(100));
  themeSelect.addEventListener("change", (event) => {
    state.theme = event.target.value;
    applyState();
  });
  autoOpenToggle.addEventListener("change", () => {
    vscode.postMessage({ type: "setAutoOpen", enabled: autoOpenToggle.checked });
  });

  window.addEventListener("message", (event) => {
    if (event.data?.type === "render") {
      content.innerHTML = event.data.html;
    } else if (event.data?.type === "autoOpenState") {
      autoOpenToggle.checked = event.data.enabled === true;
    }
  });

  window.addEventListener("keydown", (event) => {
    if (!(event.ctrlKey || event.metaKey)) {
      return;
    }

    if (event.key === "+" || event.key === "=") {
      event.preventDefault();
      setZoom(state.zoom + 10);
    } else if (event.key === "-") {
      event.preventDefault();
      setZoom(state.zoom - 10);
    } else if (event.key === "0") {
      event.preventDefault();
      setZoom(100);
    }
  });

  applyState();
})();
