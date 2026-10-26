import { readdirSync } from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

export const root = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  ".."
);

export function run(command, args = [], options = {}) {
  const result = spawnSync(command, args, {
    cwd: root,
    stdio: "inherit",
    ...options,
  });
  if (result.error) throw result.error;
  if (result.status !== 0)
    throw new Error(
      `${command} failed${
        result.signal ? ` (${result.signal})` : ` (exit ${result.status})`
      }`
    );
}

export function runTests({ skipBuild = false, skipTransport = false } = {}) {
  const windows = process.platform === "win32";
  const platform = windows ? "Windows" : "Posix";
  const resultsPrefix = windows
    ? "Windows"
    : process.platform === "darwin"
    ? "macOS"
    : process.platform === "linux"
    ? "Linux"
    : "Posix";
  const excludedPlatform = windows ? "LINUX" : "WINDOWS";
  const skip = new Set([
    "Nodearr.Automation.Test.dll",
    windows ? "Nodearr.Mono.Test.dll" : "Nodearr.Windows.Test.dll",
  ]);
  const filter = `TestCategory!=ManualTest&TestCategory!=${excludedPlatform}&TestCategory!=IntegrationTest&TestCategory!=AutomationTest`;

  if (!skipBuild) {
    run("dotnet", [
      "build",
      "src/Nodearr.sln",
      "--configuration",
      "Debug",
      `-p:Platform=${platform}`,
      "--nologo",
      "--verbosity",
      "minimal",
    ]);
  }

  const testDir = path.join(root, "_tests", "net10.0");
  const assemblies = readdirSync(testDir)
    .filter((name) => /^Nodearr\..*\.Test\.dll$/.test(name) && !skip.has(name))
    .sort();
  if (!assemblies.length)
    throw new Error("No test assemblies found. Build the solution first.");

  const failed = [];
  for (const assembly of assemblies) {
    console.log(`\nTesting ${assembly}`);
    try {
      run("dotnet", [
        "test",
        path.join(testDir, assembly),
        "--filter",
        filter,
        "--logger",
        `trx;LogFilePrefix=${resultsPrefix}`,
        "--results-directory",
        "TestResults",
      ]);
    } catch (error) {
      console.error(error.message);
      failed.push(assembly);
    }
  }

  if (!skipTransport) {
    console.log("\nTesting transport");
    try {
      run("cargo", [
        "test",
        "--locked",
        "--manifest-path",
        "transport/Cargo.toml",
      ]);
    } catch (error) {
      console.error(error.message);
      failed.push("transport");
    }
  }
  if (failed.length) throw new Error(`Tests failed: ${failed.join(", ")}`);
}

if (
  process.argv[1] &&
  path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)
) {
  try {
    const args = process.argv.slice(2);
    if (
      args.some((arg) => !["--skip-build", "--skip-transport"].includes(arg))
    ) {
      throw new Error(
        "Usage: node build/test-ci.mjs [--skip-build] [--skip-transport]"
      );
    }
    runTests({
      skipBuild: args.includes("--skip-build"),
      skipTransport: args.includes("--skip-transport"),
    });
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
