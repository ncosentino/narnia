---
applyTo: "tests/**/*.cs"
---

# Child-process test isolation

- Start draining redirected standard output and standard error concurrently before
  waiting for a child to exit. Await both drains; never wait on process exit while
  either pipe can fill.
- Keep subprocesses headless and non-interactive. Bound their lifetime and clean up
  only the process tree created by the test.
- Exercise output larger than the pipe buffer when testing redirected launchers.
  Do not hide pipe deadlocks by rerunning or increasing exit timeouts.
- Isolate native launcher contracts from concurrent application-host integration
  tests with a non-parallel xUnit collection, not assembly-wide serialization.
