/**
 * The few things every spec does, written once.
 *
 * Every one of these goes through `cypress-real-events`. That is not a style
 * preference: `cy.type()` and `cy.click()` dispatch synthetic events, which the
 * browser handles differently from a keystroke. A synthetic Tab does not move
 * focus at all, and a synthetic click does not focus what it clicks — so a suite
 * built on them would report that keyboard navigation works without ever having
 * exercised it.
 *
 * Since keyboard operation *is* the requirement here, that distinction is the
 * difference between a suite that proves something and one that does not.
 */

declare global {
  // eslint-disable-next-line @typescript-eslint/no-namespace
  namespace Cypress {
    interface Chainable {
      /** Focus the search box the way a user does, with the `/` shortcut. */
      focusSearch(): Chainable<void>;

      /** Type into the focused element one real keystroke at a time. */
      typeReal(text: string): Chainable<void>;

      /** Search for a term and select the first result, by keyboard alone. */
      findPartByKeyboard(term: string): Chainable<void>;

      /** Fail the test if the page has any serious accessibility violation. */
      checkAccessibility(context?: string): Chainable<void>;
    }
  }
}

Cypress.Commands.add("focusSearch", () => {
  // Clicking the body first, so the shortcut is pressed from the page rather
  // than from whatever the last test left focused.
  cy.get("body").realClick({ position: "topLeft" });
  cy.realPress("/");
  cy.get(".search--part .search__input").should("be.focused");
});

Cypress.Commands.add("typeReal", (text: string) => {
  cy.focused().realType(text);
});

Cypress.Commands.add("findPartByKeyboard", (term: string) => {
  cy.focusSearch();
  cy.typeReal(term);

  // The list has to be there before Enter means anything. Waiting on the
  // element rather than on a delay keeps this honest when the API is slow.
  cy.get(".search--part .search__result").should("have.length.greaterThan", 0);
  cy.realPress("Enter");
  cy.get(".part-heading__number").should("be.visible");
});

Cypress.Commands.add("checkAccessibility", (context?: string) => {
  cy.injectAxe();

  cy.checkA11y(
    context,
    {
      // WCAG 2.1 at A and AA, which is the standard this project claims. Named
      // explicitly: without it axe runs its whole ruleset, including
      // best-practice rules that are not WCAG at all, and "no WCAG 2.1 AA
      // violations" would be a claim about something else.
      runOnly: {
        type: "tag",
        values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"],
      },

      // Serious and critical only. Reporting every minor advisory would bury the
      // violations that actually stop someone using this, and a suite whose
      // failures are routinely ignored is worse than no suite. Anything moderate
      // or minor that survives is a known limit rather than a hidden one.
      includedImpacts: ["serious", "critical"],
    },
    (violations) => {
      for (const violation of violations) {
        cy.task("log", `${violation.impact}: ${violation.id} — ${violation.help}`, {
          log: false,
        });

        // The offending element, named. A count of violations tells whoever
        // reads the run that something is wrong; the selector and the measured
        // reason tell them what to change.
        for (const node of violation.nodes) {
          const summary = (node.failureSummary ?? "").split("\n").join(" | ");

          cy.task("log", `    ${node.target.join(" ")} :: ${summary}`, { log: false });
        }
      }
    },
    // Fail the test. An accessibility check that only logs is a check nobody
    // acts on.
    false,
  );
});

export {};
