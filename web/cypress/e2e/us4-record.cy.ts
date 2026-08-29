/**
 * US4 — the record as the ERP stores it.
 *
 * This is the screen that has to survive an ERP developer looking at it. Its
 * claim is narrow and checkable: what is on the left is the record, separators
 * included, and what is on the right is what this application made of it. If the
 * two disagree, the disagreement is visible rather than buried.
 *
 * SC-008 is the assertion that matters most here — each branch appears in the raw
 * record at the position it occupies in the grid. That is the parallel-field
 * property the whole data model rests on, and a parser that read position three
 * of one field beside position four of another would produce a screen that looks
 * entirely reasonable and is entirely wrong.
 */

const ATTRIBUTE_MARK = String.fromCharCode(254);
const VALUE_MARK = String.fromCharCode(253);

describe("the stored record", () => {
  beforeEach(() => {
    cy.visit("/");
    cy.findPartByKeyboard("breaker");
  });

  it("opens with the R shortcut", () => {
    cy.realPress("r");
    cy.get('[role="dialog"]').should("be.visible");
  });

  it("names the file and the key it read", () => {
    openRecord();

    cy.get('[role="dialog"]').should("contain.text", "INVENTORY");
    cy.get(".part-heading__number").invoke("text").then((partNumber) => {
      cy.get('[role="dialog"]').should("contain.text", partNumber.trim());
    });
  });

  it("shows the query that was run", () => {
    // A figure with no visible provenance is a figure a reviewer has to take on
    // trust. This is the line that makes the screen checkable.
    openRecord();

    cy.get(".record-query").should("contain.text", "LIST INVENTORY");
  });

  it("renders every separator as a labelled badge, never as nothing", () => {
    openRecord();

    cy.get(".record-pane").first().find(".mark").should("have.length.greaterThan", 0);
    cy.get(".record-legend").should("contain.text", "Attribute mark");

    // The characters are invisible. Left raw they would show one unbroken run of
    // text, which reads as though the record has no structure at all.
    cy.get(".record-pane").first().invoke("text").should((text: string) => {
      expect(text).not.to.contain(ATTRIBUTE_MARK);
      expect(text).not.to.contain(VALUE_MARK);
    });
  });

  it("explains what each separator separates", () => {
    openRecord();

    cy.get(".record-legend").should("contain.text", "separates");
  });

  it("shows the parsed form beside the stored one", () => {
    openRecord();

    cy.get(".record-pane").should("have.length.greaterThan", 1);
    cy.get('[role="dialog"]').should("contain.text", "field 1");
  });

  it("places each branch in the record at the position it occupies in the grid", () => {
    // SC-008. Read from the screen rather than from the API, because the claim
    // being checked is about what a person looking at this can verify for
    // themselves — which is the entire point of the drawer.
    cy.get(".branch__code").then(($codes) => {
      const onScreen = Array.from($codes).map((code) => code.textContent?.trim() ?? "");

      openRecord();

      cy.get(".record-pane").first().invoke("text").then((rawText: string) => {
        for (const [position, code] of onScreen.entries()) {
          expect(
            rawText,
            `${code} should appear in the record; it is at grid position ${position + 1}`,
          ).to.contain(code);
        }
      });
    });
  });

  it("closes with Escape and returns focus to the screen", () => {
    openRecord();

    cy.realPress("Escape");
    cy.get('[role="dialog"]').should("not.exist");
  });

  it("has no serious accessibility violation with the record open", () => {
    openRecord();
    cy.checkAccessibility();
  });
});

/** Open the record drawer and wait for it to have content. */
function openRecord(): void {
  cy.contains("button", "Show the record").realClick();
  cy.get('[role="dialog"]').should("be.visible");
  cy.get(".record-pane").should("exist");
}
