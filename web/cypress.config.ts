/**
 * The browser layer.
 *
 * These tests drive a real browser against the running application, which is the
 * only place several of this interface's requirements can be observed at all.
 * Focus moving to the right element, a roving index through a grid, Escape
 * returning the caret to the search box: none of those exist in jsdom, and a
 * synthetic `click()` skips exactly the behaviour being asserted.
 *
 * So `cypress-real-events` is used throughout — `realPress`, `realClick`,
 * `realType` — because they go through the browser's own input handling. That is
 * the whole reason this layer is worth its runtime.
 *
 * The services are started by scripts/run-dev-clean.ps1, never by building a
 * binary. Cypress connects to what is running; it does not own it.
 */

import { defineConfig } from "cypress";

export default defineConfig({
  e2e: {
    setupNodeEvents(on) {
      // The accessibility check reports each violation through here, because
      // Cypress commands cannot write to the terminal from the browser and a
      // violation nobody can read in the run output is one nobody will fix.
      on("task", {
        log(message: string) {
          // eslint-disable-next-line no-console
          console.log(message);
          return null;
        },
      });
    },
    baseUrl: "http://127.0.0.1:5173",
    supportFile: "cypress/support/e2e.ts",
    specPattern: "cypress/e2e/**/*.cy.ts",
    fixturesFolder: false,
    video: false,
    screenshotOnRunFailure: true,
    // Generous, because the first search of a run waits for the API's catalogue
    // to finish building. A tight timeout here fails as "search is broken".
    defaultCommandTimeout: 15000,
    requestTimeout: 15000,
    viewportWidth: 1440,
    viewportHeight: 900,
    retries: {
      // One retry in CI absorbs a genuinely flaky browser start. None
      // interactively, where a flake is something to look at rather than hide.
      runMode: 1,
      openMode: 0,
    },
  },
});
