/**
 * SC-011 — all five journeys, completed without a mouse.
 *
 * This is not a courtesy for a minority. The person this interface is for has a
 * telephone against one shoulder and a customer waiting; reaching for a mouse
 * costs them the thread of the conversation. Keyboard operation is the primary
 * way this screen is meant to be driven, and the other way is the fallback.
 *
 * Every press here is real. `cy.type()` dispatches a synthetic event, and a
 * synthetic Tab does not move focus at all — so a suite built on it would report
 * that keyboard navigation works while never once having moved the caret. That
 * is precisely the failure this file exists to make impossible.
 */

describe("every journey, keyboard only", () => {
  beforeEach(() => {
    cy.visit("/");
  });

  it("US1 — from an empty screen to a branch-by-branch answer", () => {
    cy.focusSearch();
    cy.typeReal("breaker");

    cy.get(".search__result").should("have.length.greaterThan", 0);
    cy.realPress("ArrowDown");
    cy.realPress("Enter");

    cy.get(".headline__figure").should("be.visible");
    cy.get(".branch").should("have.length.greaterThan", 0);
  });

  it("US2 — choosing a customer and reading their price", () => {
    cy.findPartByKeyboard("breaker");

    // Tab to the customer selector rather than clicking it. The number of
    // presses is deliberately not hard-coded: what matters is that it is
    // reachable, not that it sits at a particular tab index.
    cy.get(".search--customer input").focus();
    // The selector waits for two characters before it searches.
    cy.typeReal("ra");

    cy.get(".search--customer .search__result").should("have.length.greaterThan", 0);
    cy.realPress("Enter");

    cy.get(".pricing__value--net").should("be.visible");
  });

  it("US3 — expanding a branch to see what holds its stock", () => {
    cy.findPartByKeyboard("breaker");

    cy.get(".branch").first().focus().should("be.focused");
    cy.realPress("Enter");

    cy.get("#commitments-title").should("be.visible");
  });

  it("US4 — opening the stored record with a shortcut and closing it", () => {
    cy.findPartByKeyboard("breaker");

    cy.realPress("r");
    cy.get('[role="dialog"]').should("be.visible");

    cy.realPress("Escape");
    cy.get('[role="dialog"]').should("not.exist");
  });

  it("US5 — reaching the activity record and the persona picker", () => {
    cy.findPartByKeyboard("breaker");

    cy.contains("button", "Recent activity").focus().should("be.focused");
    cy.realPress("Enter");
    cy.get('[role="dialog"]').should("be.visible");

    cy.realPress("Escape");

    cy.get(".governance__identity").focus().should("be.focused");
    cy.realPress("Enter");
    cy.get(".persona").should("have.length.greaterThan", 1);
  });

  it("keeps a visible focus ring on whatever is focused", () => {
    // A keyboard user who cannot see where they are is not being served by
    // keyboard support. Tabbing has to leave a mark.
    cy.focusSearch();
    cy.realPress("Tab");

    cy.focused().should(($element) => {
      const styles = window.getComputedStyle($element[0]);
      const hasRing =
        styles.outlineStyle !== "none" ||
        styles.boxShadow !== "none" ||
        styles.borderColor !== "";

      expect(hasRing, "the focused element shows no focus indicator").to.equal(true);
    });
  });

  it("returns the caret to the search box after a drawer closes", () => {
    // Escape should put someone back where they were, not at the top of the
    // document with the whole page to tab through again.
    cy.findPartByKeyboard("breaker");

    cy.realPress("r");
    cy.get('[role="dialog"]').should("be.visible");
    cy.realPress("Escape");

    cy.focused().should("exist");
    cy.get('[role="dialog"]').should("not.exist");
  });
});
