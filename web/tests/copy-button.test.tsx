/**
 * A copy button that must never claim to have worked when it has not.
 *
 * This is the escape hatch on the one screen that matters for getting somebody
 * started: the setup instructions exist because the four variables and one
 * command are where most people stop, and copying them out is the whole point
 * of the panel.
 *
 * Clipboard access is refused in some browsers and over plain HTTP — which is
 * exactly how somebody runs this locally the first time. A button that says
 * "Copied" and copied nothing is worse than one that fails visibly: the person
 * is about to paste, and would paste whatever happened to be there before,
 * into a terminal.
 *
 * The failure path had no test.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { CopyButton } from "../src/features/connect/CopyButton";

/**
 * Comfortably past the button's own settle, which is 1.6 seconds.
 *
 * Named rather than written twice, because the two are not the same number by
 * coincidence -- if the component's settle grows past this, these tests should
 * be updated deliberately rather than start failing mysteriously.
 */
const SETTLES_AFTER_MS = 5_000;

/** What the button copies, and what a test asserts was handed to the clipboard. */
const SOMETHING_TO_COPY = "U2_HOST=uv.internal.example.com";

/**
 * Stand in for the browser clipboard.
 *
 * @param behaviour Whether writing succeeds or is refused.
 * @returns The stub, so a test can read what it was given.
 */
function clipboardThat(behaviour: "accepts" | "refuses") {
  const writeText = vi.fn(() =>
    behaviour === "accepts"
      ? Promise.resolve()
      : Promise.reject(new Error("Write permission denied.")),
  );

  Object.defineProperty(navigator, "clipboard", {
    value: { writeText },
    configurable: true,
    writable: true,
  });

  return writeText;
}

describe("copying something out of the page", () => {
  beforeEach(() => {
    vi.useRealTimers();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("hands the clipboard exactly what it was given", async () => {
    const writeText = clipboardThat("accepts");

    render(<CopyButton text={SOMETHING_TO_COPY} />);
    await userEvent.click(screen.getByRole("button"));

    expect(writeText).toHaveBeenCalledWith(SOMETHING_TO_COPY);
  });

  it("says it copied, once it has", async () => {
    clipboardThat("accepts");

    render(<CopyButton text={SOMETHING_TO_COPY} />);
    await userEvent.click(screen.getByRole("button"));

    expect(await screen.findByText("Copied")).toBeTruthy();
  });

  it("says it failed rather than claiming success", async () => {
    // The defect this exists for. Somebody is about to paste into a terminal.
    clipboardThat("refuses");

    render(<CopyButton text={SOMETHING_TO_COPY} />);
    await userEvent.click(screen.getByRole("button"));

    expect(await screen.findByText("Copy failed")).toBeTruthy();
    expect(screen.queryByText("Copied")).toBeNull();
  });

  it("tells a screen reader which of the two happened", async () => {
    // The visible label changes; the accessible name has to change with it, or
    // the one person who cannot see the button is the one told it worked.
    clipboardThat("accepts");

    render(<CopyButton text={SOMETHING_TO_COPY} label="Copy the setup" />);

    expect(screen.getByRole("button", { name: "Copy the setup" })).toBeTruthy();

    await userEvent.click(screen.getByRole("button"));

    expect(await screen.findByRole("button", { name: "Copied" })).toBeTruthy();
  });

  it("uses the label it was given until something happens", () => {
    clipboardThat("accepts");

    render(<CopyButton text={SOMETHING_TO_COPY} label="Copy the setup" />);

    expect(screen.getByRole("button").textContent).toBe("Copy the setup");
  });

  it("goes back to offering a copy, so it can be used twice", async () => {
    // Somebody copies the prompt, loses it, and comes back. A button stuck on
    // "Copied" reads as one that cannot be pressed again.
    vi.useFakeTimers({ shouldAdvanceTime: true });
    clipboardThat("accepts");

    render(<CopyButton text={SOMETHING_TO_COPY} label="Copy" />);
    await userEvent.click(screen.getByRole("button"));

    expect(screen.getByRole("button").textContent).toBe("Copied");

    // Wrapped, because advancing the clock fires the timeout but React does
    // not flush the state change it causes until act lets it.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(SETTLES_AFTER_MS);
    });

    expect(screen.getByRole("button").textContent).toBe("Copy");
  });

  it("recovers from a refusal too", async () => {
    // A refusal is often about the page not being focused rather than about
    // permission, so trying again is a reasonable thing to offer.
    vi.useFakeTimers({ shouldAdvanceTime: true });
    clipboardThat("refuses");

    render(<CopyButton text={SOMETHING_TO_COPY} label="Copy" />);
    await userEvent.click(screen.getByRole("button"));

    expect(screen.getByRole("button").textContent).toBe("Copy failed");

    await act(async () => {
      await vi.advanceTimersByTimeAsync(SETTLES_AFTER_MS);
    });

    expect(screen.getByRole("button").textContent).toBe("Copy");
  });
});
