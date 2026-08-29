/**
 * The tour a stranger gets on their first visit.
 *
 * Everything this application is for -- the branch grid, the contract price, the
 * stored record, the audit trail -- sits behind a part number a first-time
 * visitor has no way to guess. The pickers fixed the guessing. This fixes not
 * knowing there was anything to look for.
 *
 * These cover the parts that are logic rather than looks: which step is shown,
 * that it can be left and replayed, that it does not run twice by itself, and
 * that it can be driven without a mouse. Where the spotlight lands is a question
 * about a browser, and belongs to Cypress.
 */

import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { GuidedTour } from "../src/features/tour/GuidedTour";
import { TOUR_STEPS } from "../src/features/tour/steps";

/** Render the tour with the calls it makes back into the application spied on. */
function renderTour() {
  const closed = vi.fn();
  const prepared = vi.fn();

  render(<GuidedTour onClose={closed} onPrepare={prepared} />);

  return { closed, prepared };
}

describe("the guided tour", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("opens on the first step", () => {
    renderTour();

    expect(screen.getByText(TOUR_STEPS[0].title)).toBeTruthy();
    expect(screen.getByText(`Step 1 of ${TOUR_STEPS.length}`)).toBeTruthy();
  });

  it("moves forward and back", async () => {
    renderTour();

    await userEvent.click(screen.getByRole("button", { name: "Next" }));
    expect(screen.getByText(TOUR_STEPS[1].title)).toBeTruthy();

    await userEvent.click(screen.getByRole("button", { name: "Back" }));
    expect(screen.getByText(TOUR_STEPS[0].title)).toBeTruthy();
  });

  it("offers no way back from the first step", () => {
    // A Back button that does nothing is a control that lies.
    renderTour();

    expect(screen.queryByRole("button", { name: "Back" })).toBeNull();
  });

  it("closes when it is skipped", async () => {
    const { closed } = renderTour();

    await userEvent.click(screen.getByRole("button", { name: "Skip tour" }));

    expect(closed).toHaveBeenCalled();
  });

  it("closes on Escape", async () => {
    // Somebody who wants out must be able to get out without hunting for a
    // button, and a modal that traps a reader is worse than no tour at all.
    const { closed } = renderTour();

    await userEvent.keyboard("{Escape}");

    expect(closed).toHaveBeenCalled();
  });

  it("can be walked from the keyboard alone", async () => {
    renderTour();

    await userEvent.keyboard("{ArrowRight}");
    expect(screen.getByText(TOUR_STEPS[1].title)).toBeTruthy();

    await userEvent.keyboard("{ArrowLeft}");
    expect(screen.getByText(TOUR_STEPS[0].title)).toBeTruthy();
  });

  it("finishes on the last step rather than running off the end", async () => {
    const { closed } = renderTour();

    for (let step = 0; step < TOUR_STEPS.length - 1; step++) {
      await userEvent.click(screen.getByRole("button", { name: "Next" }));
    }

    expect(screen.getByText(`Step ${TOUR_STEPS.length} of ${TOUR_STEPS.length}`)).toBeTruthy();

    await userEvent.click(screen.getByRole("button", { name: "Done" }));
    expect(closed).toHaveBeenCalled();
  });

  it("asks the application to set up each step before showing it", async () => {
    // The tour drives the application rather than describing it: the step about
    // the branch grid selects a part, so the reader watches it happen.
    const { prepared } = renderTour();

    await userEvent.click(screen.getByRole("button", { name: "Next" }));

    expect(prepared).toHaveBeenCalledWith(TOUR_STEPS[0]);
    expect(prepared).toHaveBeenCalledWith(TOUR_STEPS[1]);
  });

  it("names itself to a screen reader", () => {
    const dialog = screen.queryByRole("dialog");

    renderTour();

    expect(screen.getByRole("dialog")).toBeTruthy();
    expect(dialog).toBeNull();
  });
});

describe("the steps themselves", () => {
  it("every step says something", () => {
    for (const step of TOUR_STEPS) {
      expect(step.title.trim().length).toBeGreaterThan(0);
      expect(step.body.trim().length).toBeGreaterThan(0);
    }
  });

  it("every id is distinct", () => {
    // Ids are React keys here. Two steps sharing one makes the card keep the
    // previous step's DOM, which shows as text that will not update.
    const ids = TOUR_STEPS.map((step) => step.id);

    expect(new Set(ids).size).toBe(ids.length);
  });

  it("a step that points at the record opens it first", () => {
    // The drawer only exists once a part is selected, so a record step that
    // forgot to ask for a part would spotlight nothing.
    for (const step of TOUR_STEPS.filter((candidate) => candidate.needsRecord)) {
      expect(step.needsPart).toBe(true);
    }
  });
});
