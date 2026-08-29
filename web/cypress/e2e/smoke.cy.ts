/**
 * The shell renders, and says what it is.
 *
 * Deliberately the smallest possible spec. When every other test in this suite
 * fails at once, the useful question is whether the application is running at
 * all, and a spec that asserts one thing answers it in a second.
 */

describe("the counter screen", () => {
  beforeEach(() => {
    cy.visit("/");
  });

  it("renders the search box and the governance strip", () => {
    cy.get(".search--part .search__input").should("be.visible");
    cy.get(".governance").should("be.visible");
  });

  it("says whose figures these are before anyone asks", () => {
    // The badge is the whole reason the strip is always on screen. Someone
    // reading a number here must not be able to mistake it for live stock.
    cy.contains(".governance__badge", "DEMONSTRATION DATA").should("be.visible");
    cy.contains(".governance__badge", "READ ONLY").should("be.visible");
  });

  it("names the person and the database account separately", () => {
    // Two different things, and the difference is the point: several people
    // share the database login, so the database cannot tell them apart and this
    // application has to.
    cy.contains(".governance", "Signed in as").should("be.visible");
    cy.contains(".governance", "Database login").should("be.visible");
    cy.contains(".governance__badge", "SHARED LOGIN").should("be.visible");
  });

  it("offers an empty state rather than a blank screen", () => {
    cy.get(".state, .empty-state").should("exist");
  });

  it("has no serious accessibility violation before anything is searched", () => {
    cy.checkAccessibility();
  });
});
