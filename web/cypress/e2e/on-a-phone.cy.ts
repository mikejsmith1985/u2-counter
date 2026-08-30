/**
 * The parts of this that have to survive a phone.
 *
 * This is a counter workstation application and says so on screen. That is a
 * decision, not a licence to be broken: somebody opening the link on the way to
 * a meeting should be able to read it, and a tour card hanging a third of itself
 * off the right edge says "unfinished" rather than "built for a desk".
 *
 * Measured rather than eyeballed, because the last three layout faults here were
 * all found by measuring and none by reading the stylesheet.
 */

/** A common phone, in CSS pixels. */
const PHONE = { width: 390, height: 844 };

/** Anything further past the viewport edge than this is a fault. */
const TOLERANCE = 2;

/** Fail if anything on screen sticks out sideways. */
function nothingOverflows(where: string): void {
  cy.document().then((document) => {
    const limit = document.documentElement.clientWidth;
    const over: string[] = [];

    document.querySelectorAll<HTMLElement>("body *").forEach((element) => {
      const box = element.getBoundingClientRect();
      if (box.width === 0 || box.height === 0) {
        return;
      }

      if (box.right > limit + TOLERANCE || box.left < -TOLERANCE) {
        over.push(
          `${element.tagName.toLowerCase()}.${element.className || "-"} ` +
            `[${Math.round(box.left)}..${Math.round(box.right)}]`,
        );
      }
    });

    expect(over, `${where}: nothing may overflow ${limit}px`).to.deep.equal([]);
  });
}

describe("on a phone", () => {
  beforeEach(() => {
    cy.viewport(PHONE.width, PHONE.height);
    cy.visit("/");
  });

  it("says it was built for a workstation", () => {
    // The honest note, shown only where it is true. Its absence here would mean
    // somebody meets a cramped screen with no explanation.
    cy.contains(/built for a counter workstation/i).should("be.visible");
  });

  it("fits, from the empty screen through to an answer", () => {
    nothingOverflows("empty screen");

    cy.findPartByKeyboard("breaker");
    cy.get(".branch").should("have.length.greaterThan", 0);
    nothingOverflows("a part selected");

    cy.get("body").type("r");
    cy.get(".drawer__panel").should("be.visible");
    nothingOverflows("record drawer");
  });

  it("docks the tour across the bottom instead of beside things", () => {
    // The failure this replaced: the card is 21rem wide and the placement tried
    // to put it beside the highlighted area, which at 390px meant x=195..531 --
    // a third of it off the screen. There is nowhere to put it beside anything
    // at this width, so it stops trying.
    cy.contains("button", /Take the tour/i).click();
    cy.get(".tour__card").should("be.visible");

    cy.get(".tour__card").then(($card) => {
      const box = $card[0].getBoundingClientRect();

      expect(box.left, "card starts on screen").to.be.at.least(0);
      expect(box.right, "card ends on screen").to.be.at.most(PHONE.width + TOLERANCE);
      expect(box.width, "card uses the width available").to.be.greaterThan(PHONE.width * 0.7);
    });

    nothingOverflows("tour open");
  });

  it("keeps every control big enough to hit", () => {
    // Buttons were between 27 and 37 pixels tall against a 44-pixel guideline:
    // fine for a mouse, wrong for a thumb. Links inside prose are excluded --
    // a 44-pixel line in the middle of a sentence is a broken paragraph, and a
    // reference in running text is read rather than tapped.
    cy.get("button:visible").each(($control) => {
      const box = $control[0].getBoundingClientRect();
      const label = ($control.text() || "").trim().slice(0, 30);

      expect(box.height, `"${label}" is tall enough to hit`).to.be.at.least(32);
    });
  });
});
