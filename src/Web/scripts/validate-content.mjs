import { readFile, readdir, writeFile } from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../..");
const args = new Map();
for (let index = 2; index < process.argv.length; index += 2) {
  args.set(process.argv[index], process.argv[index + 1]);
}

const candidate = args.get("--candidate") ?? "v2";
const output = args.get("--output");
const failures = [];
const promptDirectory = path.join(root, "Prompts");
const fixtureDirectory = path.join(root, "EvaluationData");

const promptEntries = await readdir(promptDirectory, { withFileTypes: true });
const prompts = [];
for (const entry of promptEntries.filter((item) => item.isDirectory()).sort((a, b) => a.name.localeCompare(b.name))) {
  const metadataPath = path.join(promptDirectory, entry.name, "metadata.json");
  const metadata = JSON.parse(await readFile(metadataPath, "utf8"));
  const promptPath = path.join(promptDirectory, entry.name, "prompt.md");
  const prompt = await readFile(promptPath, "utf8");

  if (metadata.version !== entry.name) failures.push(`${metadataPath}: version must match its directory`);
  if (!metadata.description?.trim()) failures.push(`${metadataPath}: description is required`);
  if (!prompt.trim()) failures.push(`${promptPath}: prompt must not be empty`);
  for (const metric of ["taskAdherence", "groundedness", "toolAccuracy"]) {
    const value = metadata.expectedMetrics?.[metric];
    if (!Number.isFinite(value) || value < 0 || value > 100) {
      failures.push(`${metadataPath}: expectedMetrics.${metric} must be between 0 and 100`);
    }
  }
  prompts.push(metadata);
}

const promptVersions = new Set(prompts.map((prompt) => prompt.version));
const fixtureFiles = (await readdir(fixtureDirectory)).filter((name) => name.endsWith(".json")).sort();
for (const fixtureFile of fixtureFiles) {
  const fixturePath = path.join(fixtureDirectory, fixtureFile);
  const fixture = JSON.parse(await readFile(fixturePath, "utf8"));
  if (!fixture.id?.trim() || !fixture.category?.trim() || !fixture.userMessage?.trim()) {
    failures.push(`${fixturePath}: id, category, and userMessage are required`);
  }
  if (!Array.isArray(fixture.promptVersions) || fixture.promptVersions.length === 0) {
    failures.push(`${fixturePath}: promptVersions must not be empty`);
  } else {
    for (const version of fixture.promptVersions) {
      if (!promptVersions.has(version)) failures.push(`${fixturePath}: unknown prompt version ${version}`);
    }
  }
  if (!Array.isArray(fixture.expected?.responseContains) || fixture.expected.responseContains.length === 0) {
    failures.push(`${fixturePath}: expected.responseContains must not be empty`);
  }
  if (!fixture.expected?.requiredTool?.trim()) failures.push(`${fixturePath}: expected.requiredTool is required`);
  if (typeof fixture.expected?.shouldAskClarifyingQuestion !== "boolean") {
    failures.push(`${fixturePath}: expected.shouldAskClarifyingQuestion must be boolean`);
  }
}

const selected = prompts.find((prompt) => prompt.version === candidate);
if (!selected) {
  failures.push(`Candidate ${candidate} does not exist`);
} else if (selected.intentionallyPoor) {
  failures.push(`Candidate ${candidate} is intentionally poor and cannot pass quality gates`);
} else {
  for (const [metric, minimum] of Object.entries({ taskAdherence: 90, groundedness: 90, toolAccuracy: 90 })) {
    if (selected.expectedMetrics[metric] < minimum) {
      failures.push(`Candidate ${candidate} failed ${metric}: ${selected.expectedMetrics[metric]} < ${minimum}`);
    }
  }
}

const report = {
  candidate,
  deterministic: true,
  promptCount: prompts.length,
  fixtureCount: fixtureFiles.length,
  qualityGates: { minimumTaskAdherence: 90, minimumGroundedness: 90, minimumToolAccuracy: 90 },
  observed: selected?.expectedMetrics ?? null,
  passed: failures.length === 0,
  failures,
};

if (output) {
  await writeFile(path.resolve(root, output), `${JSON.stringify(report, null, 2)}\n`);
}
console.log(JSON.stringify(report, null, 2));
if (failures.length > 0) process.exitCode = 1;
