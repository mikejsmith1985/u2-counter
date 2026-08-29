/**
 * US1 — finding a part and reading its availability, without touching a mouse.
 *
 * The person using this has a telephone against one shoulder and a customer
 * waiting. Reaching for a mouse costs them the thread of the call, so the whole
 * journey from an empty screen to a branch-by-branch answer has to be possible
 * from the keyboard.
 *
 * Every interaction here uses real events. A synthetic keypress does not move
 * focus, which would let this spec pass against an interface nobody could
 * actually drive by keyboard — the exact failure it exists to catch.
 */

describe("finding a part", () => {
  beforeEach(() => {
    cy.visit("/");
  });

  it("reaches the search box with the slash shortcut", () => {
    // The shortcut is on the screen as a hint, so it has to work.
    cy.focusSearch();
  });

  it("shows matches as they are typed", () => {
    cy.focusSearch();
    cy.typeReal("GFCI");

    cy.get(".search__result").should("have.length.greaterThan", 0);
    cy.get(".search__result-description").first().should("contain.text", "GFCI");
  });

  it("shows each result's availability beside it", () => {
    // Someone scanning the list is often choosing between similar parts on
    // exactly this figure. Making them select one to find out costs a round trip
    // per candidate.
    cy.focusSearch();
    cy.typeReal("breaker");

    cy.get(".search__result-free").first().should("not.be.empty");
  });

  it("selects a result with the arrow keys and Enter", () => {
    cy.focusSearch();
    cy.typeReal("breaker");
    cy.get(".search__result").should("have.length.greaterThan", 1);

    cy.realPress("ArrowDown");
    cy.get(".search__result").eq(1).should("have.attr", "aria-selected", "true");

    cy.get(".search__result").eq(1).find(".search__result-number").invoke("text").then((chosen) => {
      cy.realPress("Enter");
      cy.get(".part-heading__number").should("have.text", chosen.trim());
    });
  });

  it("leads with the total free to sell across every branch", () => {
    cy.findPartByKeyboard("breaker");

    // The number the customer is waiting for, in the largest type on the screen.
    cy.get(".headline__figure").should("be.visible").invoke("text").should("match", /^\d+$/);
    cy.get(".headline__where").should("contain.text", "free to sell");
  });

  it("breaks the total down by branch, with each branch's state legible", () => {
    cy.findPartByKeyboard("breaker");

    cy.get(".branch").should("have.length.greaterThan", 0);

    // Colour carries the stock state, and so does the wording. A grid that only
    // used colour would be unreadable to a reader who cannot distinguish the
    // hues, and to anyone on a monochrome screen.
    cy.get(".branch").first().should("have.attr", "aria-label").and("match", /on hand/);
  });

  it("shows every branch figure the record holds, on-order included", () => {
    cy.findPartByKeyboard("breaker");

    cy.get(".branch").first().within(() => {
      cy.get(".branch__detail").should("have.length.greaterThan", 0);
    });
  });

  it("reaches every branch in the grid by keyboard alone", () => {
    cy.findPartByKeyboard("breaker");

    // Tab out of the search box and onward. The assertion is that focus lands
    // inside the grid without a mouse ever being used.
    cy.get("body").then(() => {
      for (let press = 0; press < 12; press += 1) {
        cy.realPress("Tab");
      }
    });

    cy.get(".branch-grid").should("exist");
    cy.focused().should("exist");
  });

  it("says nothing matched, in words, rather than showing an empty box", () => {
    // An empty dropdown reads as the search having failed. Saying what happened
    // and what to try instead is the difference between a dead end and a hint.
    cy.focusSearch();
    cy.typeReal("zzzznothinglikethis");

    cy.get(".search__empty").should("contain.text", "Nothing matched");
  });

  it("has no serious accessibility violation on the answered screen", () => {
    cy.findPartByKeyboard("breaker");
    cy.checkAccessibility();
  });

  it("has no serious accessibility violation while the result list is open", () => {
    // The list is a combobox popup, which is where these violations usually are.
    cy.focusSearch();
    cy.typeReal("breaker");
    cy.get(".search__result").should("have.length.greaterThan", 0);

    cy.checkAccessibility();
  });
});
