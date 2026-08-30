/**
 * The first ninety seconds, which is the only part everybody sees.
 *
 * Somebody opening this has a screen full of controls that do not announce
 * themselves, and the tour is the whole of the explanation. If it silently drops
 * a step, points at nothing, or lays a card over the data it is describing, the
 * reader does not report a bug — they close the tab, and the rest of the work is
 * never looked at.
 *
 * These pin the failures that actually happened, all of which looked fine from
 * the code: the assistant steps disappearing because a timer lost a race against
 * a fetch, the card covering the very thing it was pointing at, and panels being
 * described but never opened.
 */

import { TOUR_SEEN_KEY } from "../support/e2e";
import { TOUR_STEPS } from "../../src/features/tour/steps";

/** Titles of the steps that describe something inside a drawer. */
const DRAWER_STEP_TITLES = TOUR_STEPS.filter(
  (step) => step.needsRecord || step.needsActivity || step.needsConnect,
).map((step) => step.title);

/**
 * Open the page as somebody who has never been here, and wait for the tour.
 *
 * The wait is load-bearing. Cypress queues the whole walk in one tick, so
 * without it every "is the card there?" check resolved before React had painted
 * anything, the walk skipped all twelve steps, and the test reported that the
 * tour contained no steps -- which reads as an application fault and is a fault
 * in the test. Two of these tests passed that way, having checked nothing.
 */
function visitAsNewcomer(): void {
  cy.on("window:before:load", (win) => {
    win.localStorage.removeItem(TOUR_SEEN_KEY);
  });
  cy.visit("/");
  cy.get(".tour__card").should("be.visible");
}

/** Walk to the next step and wait for the card to settle on its new subject. */
function advance(): void {
  cy.get(".tour__card").should("be.visible");
  cy.contains(".tour__card button", /^(Next|Done)$/).click();
}

describe("the guided tour", () => {
  it("opens by itself the first time, and not the second", () => {
    // The tour is the explanation, so it cannot wait to be found. It also must
    // not reappear for somebody who has already sat through it.
    visitAsNewcomer();

    cy.contains(".tour__card button", /^(Skip tour|Close)$/).click();
    cy.get(".tour__card").should("not.exist");

    cy.reload();
    cy.get(".tour__card").should("not.exist");
  });

  it("explains the assistant and the protocol it reaches the database through", () => {
    // These two steps went missing on a slower deployment. The tour looked for
    // each step's element after a fixed delay, and the assistant panel renders
    // nothing until its own request comes back -- so the tour concluded there was
    // no assistant and quietly skipped the explanation of the whole point.
    //
    // Asserted against the API rather than against a guess: if this deployment
    // has an assistant, the tour must talk about it.
    cy.request("/api/v1/ask/status").then((response) => {
      if (!response.body.isConfigured) {
        cy.log("no assistant on this deployment; nothing to explain");
        return;
      }

      visitAsNewcomer();

      const titles: string[] = [];

      // Twelve steps at most; the loop stops when the tour does.
      Cypress._.times(14, () => {
        cy.get("body").then(($body) => {
          if ($body.find(".tour__card").length === 0) {
            return;
          }

          cy.get(".tour__title")
            .invoke("text")
            .then((text) => titles.push(text));

          advance();
        });
      });

      cy.wrap(null).then(() => {
        const everything = titles.join(" | ");
        expect(everything, "the assistant is explained").to.match(/plain words|ask/i);
        expect(everything, "MCP is explained").to.match(/reaches the database|how it reaches/i);
      });
    });
  });

  it("never lays its card over the thing it is pointing at", () => {
    let comparisons = 0;

    // This happened. When the spotlight filled the viewport the card was clamped
    // to the top, which is exactly where the data was -- so the step describing
    // the branch grid hid the branch grid.
    visitAsNewcomer();

    Cypress._.times(14, () => {
      cy.get("body").then(($body) => {
        if ($body.find(".tour__card").length === 0) {
          return;
        }

        const stepTitle = $body.find(".tour__title").text().trim();

        cy.get(".tour__card").then(($card) => {
          const card = $card[0].getBoundingClientRect();

          cy.get("body").then(($again) => {
            const $spotlight = $again.find(".tour__ring");
            if ($spotlight.length === 0) {
              return;
            }

            const spot = $spotlight[0].getBoundingClientRect();

            // Touching edges is fine; overlapping is not.
            const overlaps =
              card.left < spot.right &&
              card.right > spot.left &&
              card.top < spot.bottom &&
              card.bottom > spot.top;

            comparisons += 1;
            expect(
              overlaps,
              `"${stepTitle}": card ${JSON.stringify({ t: Math.round(card.top), b: Math.round(card.bottom), l: Math.round(card.left), r: Math.round(card.right) })} ` +
                `overlaps ring ${JSON.stringify({ t: Math.round(spot.top), b: Math.round(spot.bottom), l: Math.round(spot.left), r: Math.round(spot.right) })}`,
            ).to.equal(false);
          });
        });

        advance();
      });
    });

    cy.wrap(null).then(() => {
      expect(comparisons, "the card was actually compared against a spotlight").to.be.greaterThan(0);
    });
  });

  it("opens each panel it describes, rather than describing a closed one", () => {
    // The steps for the stored record, the activity log and "use your own data"
    // all talk about a drawer. A step that describes a panel nobody can see is
    // worse than no step: the reader is told to look at something that is not
    // there and concludes the thing is broken.
    //
    // Which steps those are comes from the step definitions rather than from a
    // phrase matched against the card. The first version of this guessed at the
    // wording, matched nothing, and reported that no drawer step existed --
    // a test that passes its own mistake off as the application's.
    expect(DRAWER_STEP_TITLES.length, "there are drawer steps to check").to.be.greaterThan(0);

    visitAsNewcomer();

    const seen: string[] = [];
    const walked: string[] = [];

    Cypress._.times(TOUR_STEPS.length + 2, () => {
      cy.get("body").then(($body) => {
        if ($body.find(".tour__card").length === 0) {
          return;
        }

        const title = $body.find(".tour__title").text().trim();
        walked.push(title);

        if (DRAWER_STEP_TITLES.includes(title)) {
          seen.push(title);
          // On screen and big enough to read, rather than Cypress's "visible".
          //
          // That check also asks whether a fixed element is covered, and a tour
          // dims everything it is not spotlighting on purpose -- so the drawer
          // the step is describing, plainly open in the screenshot, was reported
          // as not visible. The question worth asking is whether the reader can
          // see the panel, which is what this measures.
          cy.get(".drawer__panel")
            .should("exist")
            .then(($panel) => {
              const box = $panel[0].getBoundingClientRect();

              expect(box.width, `"${title}": the drawer has width`).to.be.greaterThan(200);
              expect(box.height, `"${title}": the drawer has height`).to.be.greaterThan(200);
              // The spec's own `window` is Cypress's frame, not the application's,
              // and reports a height of zero -- so every "is it on screen" check
              // measured against nothing and failed for the wrong reason.
              expect(box.top, `"${title}": the drawer starts on screen`).to.be.lessThan(
                Cypress.config("viewportHeight"),
              );
              expect(box.bottom, `"${title}": the drawer reaches onto the screen`).to.be.greaterThan(0);
            });
        }

        advance();
      });
    });

    cy.wrap(null).then(() => {
      expect(walked.length, "the walk actually visited steps").to.be.greaterThan(0);
      expect(
        seen,
        `every drawer step was reached and opened its drawer; walked: ${walked.join(" > ")}`,
      ).to.deep.equal(DRAWER_STEP_TITLES);
    });
  });

  it("leaves on Escape, from anywhere in it", () => {
    // Somebody who wants to get on with it must not have to find a button.
    visitAsNewcomer();

    advance();
    advance();

    cy.get("body").realPress("Escape");
    cy.get(".tour__card").should("not.exist");
    cy.get(".tour__backdrop").should("not.exist");
  });
});
