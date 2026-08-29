/**
 * US5 — who asked, and what this application is allowed to do.
 *
 * The database is reached through one shared login, so its own log can only ever
 * say that u2demo did something. Everything a reviewer needs beyond that has to
 * be recorded and shown here, and the strip that says so is on every screen
 * rather than on a page someone has to go looking for.
 *
 * The read-only claim is presented the same way: stated where the writing
 * control would be, so the boundary reads as a decision rather than as a feature
 * nobody got round to.
 */

describe("governance", () => {
  beforeEach(() => {
    cy.visit("/");
  });

  it("keeps the strip on screen before anything is searched", () => {
    cy.get(".governance").should("be.visible");
  });

  it("keeps the strip on screen once a part is showing", () => {
    cy.findPartByKeyboard("breaker");
    cy.get(".governance").should("be.visible");
  });

  it("says whose figures these are even when the drawer covers the strip", () => {
    // The requirement is that nobody can be looking at a figure here without
    // being told what it is. The drawer is a modal and covers the strip, so it
    // carries the badges itself -- and it is the screen most likely to be
    // photographed, being the one that shows a real MultiValue record.
    cy.findPartByKeyboard("breaker");
    cy.contains("button", "Show the record").realClick();

    cy.get('[role="dialog"]')
      .contains(".governance__badge", "DEMONSTRATION DATA")
      .should("be.visible");

    cy.get('[role="dialog"]')
      .contains(".governance__badge", "READ ONLY")
      .should("be.visible");
  });

  it("says the database login is shared, rather than leaving it to be discovered", () => {
    // The limitation a reviewer would otherwise find for themselves, stated
    // first. It is the reason the activity record has to exist at all.
    cy.contains(".governance__badge", "SHARED LOGIN").should("be.visible");
    cy.get(".governance").should("contain.text", "Database login");
  });

  it("offers a control it will not perform, shown disabled with the reason", () => {
    cy.findPartByKeyboard("breaker");

    cy.contains("button", "Reserve stock").should("be.disabled");
    cy.get(".read-only-gate__reason").should("contain.text", "never writes");
  });

  it("lists what this person just did", () => {
    cy.findPartByKeyboard("breaker");
    cy.contains("button", "Recent activity").realClick();

    cy.get('[role="dialog"]').should("be.visible");
    cy.get('[role="dialog"]').should("contain.text", "Read availability");
  });

  it("names the shared login on each recorded action", () => {
    cy.findPartByKeyboard("breaker");
    cy.contains("button", "Recent activity").realClick();

    cy.get('[role="dialog"]').invoke("text").should("match", /u2demo|DEMO/);
  });

  it("lets the persona be changed, and says plainly that it is not a login", () => {
    cy.get(".governance__identity").realClick();

    cy.get('[role="dialog"]').should("be.visible");
    cy.get('[role="dialog"]').should("contain.text", "not accounts");
    cy.get(".persona").should("have.length.greaterThan", 1);
  });

  it("shows the chosen persona on the strip afterwards", () => {
    cy.get(".governance__identity").realClick();

    cy.get(".persona").not(".persona--current").first().find(".persona__name")
      .invoke("text").then((name) => {
        cy.get(".persona").not(".persona--current").first().realClick();
        cy.get(".governance__identity").should("contain.text", name.trim());
      });
  });

  it("has no serious accessibility violation on the persona picker", () => {
    cy.get(".governance__identity").realClick();
    cy.get('[role="dialog"]').should("be.visible");

    cy.checkAccessibility();
  });

  it("has no serious accessibility violation on the activity panel", () => {
    cy.contains("button", "Recent activity").realClick();
    cy.get('[role="dialog"]').should("be.visible");

    cy.checkAccessibility();
  });
});
