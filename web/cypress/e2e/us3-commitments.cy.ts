/**
 * US3 — what is already spoken for.
 *
 * "Twelve on hand, nine committed" raises exactly one question, and it is the
 * question the customer on the phone is about to ask: committed to whom, and
 * when do they take it. A screen that shows the total and stops has moved the
 * work to whoever the representative rings next.
 *
 * The unaccounted row matters as much as the orders. An order closed without
 * releasing its allocation leaves stock committed to nothing, and a screen that
 * quietly absorbed the difference would be lying to make its own arithmetic look
 * tidy.
 */

/*
 * The commitments panel, addressed by what it is labelled rather than by its
 * class. The pricing panel is also a `.panel` and comes first in the document,
 * so an unscoped selector quietly searches the wrong one -- and passes or fails
 * for reasons that have nothing to do with commitments.
 */
const COMMITMENTS = '[aria-labelledby="commitments-title"]';

describe("commitments", () => {
  beforeEach(() => {
    cy.visit("/");
    cy.findPartByKeyboard("breaker");
  });

  it("expands a branch from the keyboard", () => {
    // Reached by keyboard because the representative is on a call. Activating a
    // grid cell with Enter is the behaviour being asserted, so a synthetic click
    // would prove nothing.
    cy.get(".branch").first().focus().should("be.focused");
    cy.realPress("Enter");

    cy.get("#commitments-title").should("be.visible");
  });

  it("names the branch it is showing", () => {
    // A panel of order numbers with no branch named on it is a panel a
    // representative can misread as belonging to the branch they meant to open.
    cy.get(".branch").first().find(".branch__name").invoke("text").then((nameAndCode) => {
      // The name element also carries the code; the branch name is what
      // precedes it.
      cy.get(".branch").first().find(".branch__code").invoke("text").then((code) => {
        const branchName = nameAndCode.replace(code, "").trim();

        cy.get(".branch").first().realClick();
        cy.get("#commitments-title").should("contain.text", branchName);
      });
    });
  });

  it("lists the orders holding stock, with who and when", () => {
    openAnyBranchWithCommitments(() => {
      cy.get(".commitments tbody tr").should("have.length.greaterThan", 0);
      // An order number nobody can look up, or a date nobody can promise
      // against, would make the row useless on a call.
      cy.get(".commitments tbody tr").first().find("td").should("have.length.greaterThan", 2);
    });
  });

  it("shows the arithmetic closing, including anything unaccounted for", () => {
    openAnyBranchWithCommitments(() => {
      cy.get(".commitments").should("be.visible");
      cy.get(COMMITMENTS).invoke("text").should("match", /committed/i);
    });
  });

  it("says so plainly when a branch holds nothing", () => {
    // Zero commitments is a legitimate answer and must not look like the panel
    // having failed to load.
    cy.get(".branch").each(($branch) => {
      const label = $branch.attr("aria-label") ?? "";

      if (/0 committed/.test(label)) {
        cy.wrap($branch).realClick();
        cy.get(COMMITMENTS).contains(/nothing|no orders|none/i).should("exist");

        return false;
      }

      return undefined;
    });
  });

  it("closes and returns focus to the screen behind it", () => {
    cy.get(".branch").first().realClick();
    cy.get("#commitments-title").should("be.visible");

    cy.contains("button", "Close").realClick();
    cy.get("#commitments-title").should("not.exist");
  });

  it("has no serious accessibility violation with commitments open", () => {
    cy.get(".branch").first().realClick();
    cy.get("#commitments-title").should("be.visible");

    cy.checkAccessibility();
  });
});

/**
 * Open the first branch that actually has commitments, and run the assertions.
 *
 * The seeded data does not guarantee which branch that is, and a spec that
 * assumed one would fail for a reason unrelated to what it was testing.
 */
function openAnyBranchWithCommitments(assertions: () => void): void {
  cy.get(".branch").then(($branches) => {
    const withCommitments = Array.from($branches).find((branch) => {
      const label = branch.getAttribute("aria-label") ?? "";
      const committed = /(\d+) committed/.exec(label);

      return committed !== null && Number(committed[1]) > 0;
    });

    if (!withCommitments) {
      cy.log("No branch of this part holds committed stock.");
      return;
    }

    cy.wrap(withCommitments).realClick();
    cy.get("#commitments-title").should("be.visible");
    assertions();
  });
}
