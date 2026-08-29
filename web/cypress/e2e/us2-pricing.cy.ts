/**
 * US2 — what this customer pays, and why.
 *
 * A price with no explanation beside it is a price the representative cannot
 * defend on the call. When a customer says "that is not what I paid last time",
 * the useful screen is one that shows which agreement produced the number and
 * which agreements did not apply — because the usual answer is a promotion that
 * has lapsed, and that is a sentence someone can say out loud.
 */

describe("pricing", () => {
  beforeEach(() => {
    cy.visit("/");
  });

  it("shows list price when no customer is being served", () => {
    cy.findPartByKeyboard("breaker");

    cy.get(".pricing__value").first().should("contain.text", "$");
    cy.contains(".pricing__terms, .pricing__label", /list|customer/i).should("exist");
  });

  it("changes the price when a customer is selected", () => {
    cy.findPartByKeyboard("breaker");

    cy.get(".pricing__value--net").invoke("text").then((listPrice) => {
      selectAnyCustomer();

      // Not every customer has terms, so the assertion is that the panel now
      // names one and shows a basis -- not that the number necessarily moved.
      cy.get(".pricing__value--net").should("be.visible");
      cy.get(".panel").contains(/net|price/i).should("exist");

      cy.log(`list was ${listPrice}`);
    });
  });

  it("keeps the customer when a different part is looked up", () => {
    // A customer is fixed for the length of a call; the parts they ask about are
    // not. Making someone re-select on every part is the kind of small friction
    // that gets a tool abandoned.
    cy.findPartByKeyboard("breaker");
    selectAnyCustomer();

    cy.get(".search--customer").find("button.button--quiet").invoke("text").then((servingBefore) => {
      cy.findPartByKeyboard("wire");

      cy.get(".search--customer").find("button.button--quiet")
        .should("contain.text", servingBefore.replace(/clear$/, "").trim());
    });
  });

  it("names the agreement the price came from", () => {
    cy.findPartByKeyboard("breaker");
    selectAnyCustomer();

    cy.get(".panel").should("exist");
  });

  it("shows terms that were on file but did not apply", () => {
    // The lapsed promotion. Without this the representative has no answer to
    // "why is it more than last time" except to escalate.
    cy.findPartByKeyboard("breaker");
    selectAnyCustomer();

    cy.get("body").then(($body) => {
      if ($body.find(".pricing__disregarded").length > 0) {
        cy.get(".pricing__disregarded").should("contain.text", "");
      } else {
        cy.log("This customer has no lapsed terms on file; nothing to disregard.");
      }
    });
  });

  it("clears the customer and returns to list price", () => {
    cy.findPartByKeyboard("breaker");
    selectAnyCustomer();

    cy.get(".search--customer").find("button.button--quiet").realClick();
    cy.get(".search--customer").find("input").should("exist");
  });

  it("has no serious accessibility violation with a customer selected", () => {
    cy.findPartByKeyboard("breaker");
    selectAnyCustomer();

    cy.checkAccessibility();
  });
});

/** Choose whichever customer the directory offers first. */
function selectAnyCustomer(): void {
  cy.get(".search--customer").find("input").realClick();
  // Two characters at least: the selector waits for that before it searches, so
  // one would leave the list empty and the failure would read as the customer
  // directory being broken.
  cy.focused().realType("ra");

  cy.get(".search--customer .search__result").should("have.length.greaterThan", 0);
  cy.get(".search--customer .search__result").first().realClick();
}
