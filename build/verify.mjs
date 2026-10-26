import { run, runTests } from "./test-ci.mjs";

try {
  for (const task of [
    "lint",
    "stylelint",
    "typecheck",
    "build --env production",
  ]) {
    console.log(`\nRunning yarn ${task}`);
    // These are fixed commands; a shell lets Windows run the Yarn command shim.
    run(`yarn ${task}`, [], { shell: true });
  }
  runTests();
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
