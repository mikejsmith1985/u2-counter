/**
 * Changing one value, and the proof that nothing beside it moved.
 *
 * This had no browser coverage at all. The local stack ran read-only, so the
 * suite could not reach the editor, and the only write in the application was
 * exercised for the first time by a person clicking — who immediately found that
 * a successful write left the table showing the value it had replaced.
 *
 * Runs only where the deployment permits writing. On a read-only stack these
 * skip rather than fail: a read-only deployment having no editor is correct, and
 * a suite that goes red for it would train somebody to ignore it.
 */

/** A file whose records are small enough to reason about in a failure message. */
const FILE = "BRANCH";

/**
 * Type a new value and take it through the two-step confirmation.
 *
 * The editor shows the current value in a read-only box and the replacement in a
 * second one, then asks for a review before writing anything. Both steps are
 * deliberate -- a write that happens on one click is a write somebody makes by
 * accident -- so a test that skips the review is not exercising the real path.
 *
 * @param value What to write.
 */
function writeValue(value: string): void {
  cy.get(".update__edit input:not([readonly])").clear().type(value);
  cy.contains(".update button", /review the change/i).click();
  cy.contains(".update button", /write it/i).click();
}

describe("changing one value", () => {
  beforeEach(() => {
    cy.visit("/");

    cy.request("/api/v1/records/status").then((status) => {
      if (status.body.canWrite !== true) {
        cy.log("this deployment is read-only; nothing to exercise");
        // Marks the whole spec pending rather than passing vacuously.
        (Cypress as unknown as { mocha: { getRunner: () => { suite: Mocha.Suite } } }).mocha
          .getRunner()
          .suite.ctx.skip();
      }
    });

    cy.contains("button", /Explore the database/i).click();
    cy.get(".drawer__panel").should("be.visible");
    cy.get(".explore select").first().select(FILE);
  });

  it("says the editor is there, rather than hiding it in a hover", () => {
    // The affordance was a dotted underline in transparent. Every value was a
    // button and looked exactly like text, so nobody found the feature at all.
    cy.contains(/click any value to change it/i).should("be.visible");
    cy.get(".explore__cell").should("have.length.greaterThan", 0);
  });

  it("names the record and the position before changing anything", () => {
    cy.get(".explore__cell").first().click();

    cy.get(".update").should("be.visible");
    cy.get(".update").invoke("text").should("match", /field \d/i);
  });

  it("shows the new value in the table, not the one it replaced", () => {
    // The defect this pins. The refresh rebuilt the filter state into a new
    // object, which changes its identity and none of its values -- so the query
    // key was identical and the cache answered. A write could succeed and the
    // table would go on showing what it had replaced, which is the worst
    // possible outcome for the one screen whose subject is whether a write
    // happened.
    const changed = `T${Date.now().toString().slice(-6)}`;

    cy.get(".explore__cell")
      .first()
      .invoke("text")
      .then((before) => {
        cy.get(".explore__cell").first().click();

        writeValue(changed);

        cy.contains(".update", /written,/i, { timeout: 15000 }).should("be.visible");
        cy.contains(".update button", /done/i).click();

        cy.get(".explore__cell").first().should("contain.text", changed);
        cy.get(".explore__cell").first().should("not.contain.text", before.trim());
      });
  });

  it("proves no parallel field moved", () => {
    // The subtlest claim in the project, and the reason the write path exists.
    // A padded field is still a well-formed record: no error, every later read
    // agrees with it, and a branch is quietly described by the wrong position.
    cy.get(".explore__cell").first().click();

    writeValue(`P${Date.now().toString().slice(-5)}`);

    cy.contains(".update", /written,/i, { timeout: 15000 }).should("be.visible");

    // The before and after value counts, and the verdict that reads them.
    cy.contains(".update", /every field still holds the same number of values/i).should("be.visible");
  });

});
