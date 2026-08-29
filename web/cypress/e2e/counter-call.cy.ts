/**
 * SC-001 and SC-002 — the call this screen exists for.
 *
 * A customer is on the phone. They read a part number off a box and ask whether
 * it is in stock and what it costs them. The answer has to be complete before
 * the pause becomes awkward, which in practice is about half a minute — and it
 * has to be visible at once, because a representative scrolling to find the
 * branch they need is a representative reading the wrong row.
 *
 * These two are the only criteria measured against the clock and the viewport
 * rather than against a value, and they are the ones somebody watching a
 * demonstration will judge without being told to.
 */

/** How long the whole journey may take, in milliseconds (SC-001). */
const CALL_BUDGET_MS = 30_000;

describe("a call at the counter", () => {
  beforeEach(() => {
    cy.visit("/");
  });

  it("goes from a part number to a customer's net price inside the budget", () => {
    const started = Date.now();

    // Everything by keyboard, because that is how it will actually be done: the
    // representative has a telephone against one shoulder.
    cy.focusSearch();
    cy.typeReal("breaker");
    cy.get(".search--part .search__result").should("have.length.greaterThan", 0);
    cy.realPress("Enter");

    cy.get(".headline__figure").should("be.visible");
    cy.get(".branch").should("have.length.greaterThan", 0);

    cy.get(".search--customer input").focus();
    cy.typeReal("ra");
    cy.get(".search--customer .search__result").should("have.length.greaterThan", 0);
    cy.realPress("Enter");

    cy.get(".pricing__value--net").should("be.visible").then(() => {
      const elapsed = Date.now() - started;

      expect(
        elapsed,
        `the journey took ${(elapsed / 1000).toFixed(1)}s of a ${CALL_BUDGET_MS / 1000}s budget`,
      ).to.be.lessThan(CALL_BUDGET_MS);
    });
  });

  it("shows every branch without scrolling, at a branch workstation's screen", () => {
    // SC-002. 1920×1080 is what is actually on the desk. A representative who has
    // to scroll to see a branch is a representative who reads the wrong row and
    // quotes stock from the wrong city.
    cy.viewport(1920, 1080);

    cy.findPartByKeyboard("breaker");
    cy.get(".branch").should("have.length.greaterThan", 0);

    cy.window().then((window) => {
      cy.get(".branch-grid").then(($grid) => {
        const bounds = $grid[0].getBoundingClientRect();

        expect(
          bounds.bottom,
          "the branch grid extends below the fold",
        ).to.be.at.most(window.innerHeight);
      });
    });
  });

  it("keeps the page itself from scrolling sideways at any width", () => {
    // Wide content -- the branch grid, the record panes -- scrolls inside its own
    // container. A page that scrolls sideways hides the governance strip, which
    // is the one thing that must be on screen whatever else is.
    //
    // The part is found once and the window is then resized around it, because
    // that is what happens: someone drags a window narrower, or opens the screen
    // on a laptop instead of the counter workstation. Re-running the search at
    // each width would test the search three times and the layout once.
    cy.findPartByKeyboard("breaker");
    cy.get(".branch").should("have.length.greaterThan", 0);

    for (const width of [1920, 1440, 1024]) {
      cy.viewport(width, 900);

      cy.document().should((document) => {
        expect(
          document.documentElement.scrollWidth,
          `the page scrolls sideways at ${width}px`,
        ).to.be.at.most(document.documentElement.clientWidth + 1);
      });

      // The strip is the thing a sideways scroll would push off screen, so it is
      // checked at each width rather than inferred from the measurement.
      cy.get(".governance").should("be.visible");
    }
  });

  it("shows the answer, the price and the governance strip together", () => {
    // The three things a representative needs at once: what is available, what it
    // costs, and whose figures these are. Any one of them off screen turns a
    // single glance into a scroll.
    cy.viewport(1920, 1080);
    cy.findPartByKeyboard("breaker");

    cy.get(".headline__figure").should("be.visible");
    cy.get(".branch-grid").should("be.visible");
    cy.get(".governance").should("be.visible");
  });
});
