const esbuild = require("esbuild");

const watch = process.argv.includes("--watch");
const config = {
  entryPoints: ["src/extension.ts"],
  bundle: true,
  outfile: "dist/extension.js",
  external: ["vscode"],
  format: "cjs",
  platform: "node",
  target: "node20",
  sourcemap: true,
  logLevel: "info"
};

async function run() {
  if (watch) {
    const context = await esbuild.context(config);
    await context.watch();
    return;
  }

  await esbuild.build(config);
}

run().catch(() => process.exit(1));
