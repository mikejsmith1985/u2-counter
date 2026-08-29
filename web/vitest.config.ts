/**
 * The unit layer for the front end.
 *
 * Kept separate from vite.config.ts because that file describes a dev server
 * with a proxy to the API, and none of that belongs in a test run. A test that
 * could reach the API would eventually reach it by accident, and the failure
 * would look like a component bug rather than a missing mock.
 *
 * The Cypress suite covers the parts of this interface that are about behaviour
 * in a browser -- focus, keyboard, real events. These tests cover the parts that
 * are about logic: what the copy button puts on the clipboard, how a record's
 * marks are rendered. The split is deliberate; running either kind in the other
 * place makes it slower and less convincing.
 */

import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  test: {
    environment: "jsdom",
    include: ["tests/**/*.test.{ts,tsx}"],
    globals: true,
    setupFiles: ["tests/setup.ts"],
    coverage: {
      provider: "v8",
      include: ["src/**/*.{ts,tsx}"],
      // The entry point mounts React and does nothing else worth asserting.
      exclude: ["src/main.tsx", "src/**/*.d.ts"],
    },
  },
});
