/**
 * What every browser test has available.
 *
 * Two imports and nothing else. `cypress-real-events` adds the commands that go
 * through the browser's own input handling; `cypress-axe` adds the accessibility
 * check. Both are used in nearly every spec, so importing them here rather than
 * per file keeps the specs about what they are testing.
 */

import "cypress-real-events";
import "cypress-axe";
import "./commands";

/** What the application writes once somebody has been shown round. */
export const TOUR_SEEN_KEY = "counter.tour.seen";

/**
 * Every spec starts as a returning visitor, unless it says otherwise.
 *
 * The tour opens by itself on a first visit, which is what it is for -- and
 * Cypress clears local storage between tests, so every test was a first visit
 * and every test got a tour laid over the thing it was trying to look at. Two
 * assertions failed on a footer that was never hidden, only covered, and the
 * message named a CSS property rather than the tour.
 *
 * Seeded before the page script runs rather than after, because by the time a
 * test could call setItem the application has already decided.
 */
beforeEach(() => {
  cy.on("window:before:load", (win) => {
    win.localStorage.setItem(TOUR_SEEN_KEY, "yes");
  });
});
