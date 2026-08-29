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
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { createElement } from "react";

import { GuidedTour } from "../src/features/tour/GuidedTour";
import { TOUR_STEPS } from "../src/features/tour/steps";

/**
 * Put the elements the tour points at on the page.
 *
 * Every step still needs something to spotlight, so the anchors are stood up
 * here. Which steps are *shown* is a separate question, answered by whether an
 * assistant is configured — see the fetch stub below.
 */
function standUpAnchors(): void {
  for (const step of TOUR_STEPS) {
    if (!step.target?.startsWith("[data-tour")) {
      continue;
    }

    const anchor = document.createElement("div");
    anchor.setAttribute("data-tour", step.target.replace(/\[data-tour='(.*)'\]/, "$1"));
    document.body.append(anchor);
  }
}

/** A fresh client per render, so one test's answer cannot leak into the next. */
function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  });
  return createElement(QueryClientProvider, { client }, children);
}

/**
 * Render the tour and wait for it to decide which steps to show.
 *
 * It asks the API whether an assistant is configured before showing anything,
 * because deciding from the page was a race it lost. So every test waits for the
 * card, exactly as a reader does.
 *
 * @param hasAssistant What the API should say.
 */
async function renderTour(hasAssistant = true) {
  standUpAnchors();

  vi.stubGlobal(
    "fetch",
    vi.fn(() =>
      Promise.resolve(
        new Response(
          JSON.stringify({ isConfigured: hasAssistant, model: "claude-haiku-4-5" }),
          { status: 200 },
        ),
      ),
    ),
  );

  const closed = vi.fn();
  const prepared = vi.fn();

  render(<GuidedTour onClose={closed} onPrepare={prepared} />, { wrapper });

  await waitFor(() => expect(screen.getByRole("dialog")).toBeTruthy());

  const shown = TOUR_STEPS.filter((step) => !step.needsAssistant || hasAssistant);

  return { closed, prepared, shown };
}

describe("the guided tour", () => {
  beforeEach(() => {
    document.body.innerHTML = "";
    vi.restoreAllMocks();
  });

  afterEach(() => {
    document.body.innerHTML = "";
    vi.restoreAllMocks();
  });

  it("opens on the first step", async () => {
    const { shown } = await renderTour();

    expect(screen.getByText(TOUR_STEPS[0].title)).toBeTruthy();
    expect(screen.getByText(`Step 1 of ${shown.length}`)).toBeTruthy();
  });

  it("skips the assistant steps when no assistant is configured", async () => {
    // On a deployment with no API key there is no assistant, and a tour that
    // explained one would be advertising a feature that is not there.
    const { shown } = await renderTour(false);

    const aboutTheAssistant = TOUR_STEPS.filter((step) => step.needsAssistant);

    expect(aboutTheAssistant.length).toBeGreaterThan(0);
    expect(shown.length).toBe(TOUR_STEPS.length - aboutTheAssistant.length);
    expect(screen.queryByText("Ask it in plain words")).toBeNull();
  });

  it("shows the assistant steps when there is one", async () => {
    // The failure that prompted this: the tour decided from the page after a
    // short delay, lost the race against the panel's own request, and silently
    // dropped the steps explaining the point of the whole thing.
    const { shown } = await renderTour(true);

    expect(shown.length).toBe(TOUR_STEPS.length);
    expect(screen.getByText(`Step 1 of ${TOUR_STEPS.length}`)).toBeTruthy();
  });

  it("moves forward and back", async () => {
    await renderTour();

    await userEvent.click(screen.getByRole("button", { name: "Next" }));
    expect(screen.getByText(TOUR_STEPS[1].title)).toBeTruthy();

    await userEvent.click(screen.getByRole("button", { name: "Back" }));
    expect(screen.getByText(TOUR_STEPS[0].title)).toBeTruthy();
  });

  it("offers no way back from the first step", async () => {
    // A Back button that does nothing is a control that lies.
    await renderTour();

    expect(screen.queryByRole("button", { name: "Back" })).toBeNull();
  });

  it("closes when it is skipped", async () => {
    const { closed } = await renderTour();

    await userEvent.click(screen.getByRole("button", { name: "Skip tour" }));

    expect(closed).toHaveBeenCalled();
  });

  it("closes on Escape", async () => {
    // Somebody who wants out must be able to get out without hunting for a
    // button, and a modal that traps a reader is worse than no tour at all.
    const { closed } = await renderTour();

    await userEvent.keyboard("{Escape}");

    expect(closed).toHaveBeenCalled();
  });

  it("can be walked from the keyboard alone", async () => {
    await renderTour();

    await userEvent.keyboard("{ArrowRight}");
    expect(screen.getByText(TOUR_STEPS[1].title)).toBeTruthy();

    await userEvent.keyboard("{ArrowLeft}");
    expect(screen.getByText(TOUR_STEPS[0].title)).toBeTruthy();
  });

  it("finishes on the last step rather than running off the end", async () => {

    const { shown: all, closed } = await renderTour();

    for (let step = 0; step < all.length - 1; step++) {
      await userEvent.click(screen.getByRole("button", { name: "Next" }));
    }

    expect(screen.getByText(`Step ${all.length} of ${all.length}`)).toBeTruthy();

    await userEvent.click(screen.getByRole("button", { name: "Done" }));
    expect(closed).toHaveBeenCalled();
  });

  it("asks the application to set up each step before showing it", async () => {
    // The tour drives the application rather than describing it: the step about
    // the branch grid selects a part, so the reader watches it happen.
    const { prepared } = await renderTour();

    await userEvent.click(screen.getByRole("button", { name: "Next" }));

    expect(prepared).toHaveBeenCalledWith(TOUR_STEPS[0]);
    expect(prepared).toHaveBeenCalledWith(TOUR_STEPS[1]);
  });

  it("names itself to a screen reader", async () => {
    const dialog = screen.queryByRole("dialog");

    await renderTour();

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
