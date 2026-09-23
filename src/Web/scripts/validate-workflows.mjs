import { readFile, readdir } from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";
import { parseDocument } from "yaml";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../..");
const workflowDirectory = path.join(root, ".github", "workflows");
const files = (await readdir(workflowDirectory))
  .filter((name) => name.endsWith(".yml") || name.endsWith(".yaml"))
  .sort();
const failures = [];
const shaAction = /^[^@\s]+@[0-9a-f]{40}(?:\s+#.*)?$/;

for (const file of files) {
  const filePath = path.join(workflowDirectory, file);
  const source = await readFile(filePath, "utf8");
  const document = parseDocument(source, { prettyErrors: true, strict: true });
  for (const error of document.errors) failures.push(`${file}: ${error.message}`);
  const workflow = document.toJS();

  if (!workflow?.permissions) failures.push(`${file}: top-level permissions are required`);
  if (!workflow?.concurrency) failures.push(`${file}: top-level concurrency is required`);
  if (/(AZURE_CLIENT_SECRET|client-secret|password\s*:)/i.test(source)) {
    failures.push(`${file}: long-lived credential reference is forbidden`);
  }

  for (const [lineNumber, line] of source.split(/\r?\n/u).entries()) {
    const match = line.match(/^\s*uses:\s*(.+)\s*$/u);
    if (match && !shaAction.test(match[1])) {
      failures.push(`${file}:${lineNumber + 1}: actions must use an immutable 40-character commit SHA`);
    }
  }

  const deploymentWorkflow = ["candidate-deploy.yml", "promote.yml", "rollback.yml"].includes(file);
  if (deploymentWorkflow) {
    if (!source.includes("id-token: write")) failures.push(`${file}: OIDC id-token permission is required`);
    if (!source.includes("environment:")) failures.push(`${file}: protected environment binding is required`);
    if (!source.includes("cancel-in-progress: false")) {
      failures.push(`${file}: deployments must queue instead of cancelling or racing`);
    }
  }
}

if (files.length !== 4) failures.push(`Expected 4 workflows, found ${files.length}`);
if (failures.length > 0) {
  console.error(failures.join("\n"));
  process.exitCode = 1;
} else {
  console.log(`Validated ${files.length} workflow files: syntax, immutable actions, OIDC, permissions, and concurrency.`);
}
