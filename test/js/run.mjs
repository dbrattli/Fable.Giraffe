// Runner for the JS/Node behavioral suite. Imports the Fable-compiled `run` (which drives
// the shared test list, awaiting each Promise) and propagates its exit code.
import { run } from "../../build/test-js/Main.js";

const code = await run();
process.exit(code);
