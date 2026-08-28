// Lint rules for the Counter front end.
//
// Article IV is enforced here as errors rather than warnings: a rule that only
// warns is a rule the build ignores.

import js from "@eslint/js";
import globals from "globals";
import reactHooks from "eslint-plugin-react-hooks";
import reactRefresh from "eslint-plugin-react-refresh";
import tseslint from "typescript-eslint";

export default tseslint.config(
  {
    ignores: ["dist", "coverage", "cypress/videos", "cypress/screenshots"],
  },
  {
    extends: [js.configs.recommended, ...tseslint.configs.recommendedTypeChecked],
    files: ["**/*.{ts,tsx}"],
    languageOptions: {
      ecmaVersion: 2023,
      globals: globals.browser,
      parserOptions: {
        project: ["./tsconfig.app.json", "./tsconfig.node.json"],
        tsconfigRootDir: import.meta.dirname,
      },
    },
    plugins: {
      "react-hooks": reactHooks,
      "react-refresh": reactRefresh,
    },
    rules: {
      ...reactHooks.configs.recommended.rules,
      "react-refresh/only-export-components": ["warn", { allowConstantExport: true }],

      // Article IV: names carry meaning. A single letter does not, outside a
      // loop index.
      "id-length": ["error", { min: 2, exceptions: ["i", "j", "k", "_"] }],

      // Booleans read as questions, so their names should too.
      "@typescript-eslint/naming-convention": [
        "error",
        { selector: "variable", types: ["boolean"], format: ["PascalCase"],
          prefix: ["is", "has", "can", "should", "was"] },
        { selector: "typeLike", format: ["PascalCase"] },
        { selector: "function", format: ["camelCase", "PascalCase"] },
      ],

      // An unhandled promise in a data-fetching client is a silently missing
      // answer on screen.
      "@typescript-eslint/no-floating-promises": "error",
      "@typescript-eslint/no-misused-promises": "error",

      // `any` erases the contract the API worked to define.
      "@typescript-eslint/no-explicit-any": "error",

      // Guard clauses over deep nesting.
      "max-depth": ["error", 3],
      complexity: ["error", 10],
      "max-lines-per-function": ["error", { max: 60, skipBlankLines: true, skipComments: true }],

      // No magic numbers, per Article IV.
      "no-magic-numbers": ["error", { ignore: [0, 1, -1], ignoreArrayIndexes: true,
        enforceConst: true, detectObjects: false }],

      eqeqeq: ["error", "always"],
      "no-console": ["error", { allow: ["warn", "error"] }],
    },
  },
);
