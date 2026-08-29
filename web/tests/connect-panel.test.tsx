/**
 * The answer to the question anybody serious asks within a minute.
 *
 * Nobody evaluating software wants to send their inventory through somebody
 * else's API key, and nobody wants to judge a system on data they have never
 * seen. So the offer to run it against their own database has to be present, has
 * to be short, and has to be accurate — an instruction that is wrong is worse
 * than no instruction, because it is discovered at a keyboard.
 *
 * These pin the parts that would silently rot: the variable names, and the claim
 * about which half of the application fits an unfamiliar schema.
 */

import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { ConnectPanel } from "../src/features/connect/ConnectPanel";

describe("the connect panel", () => {
  it("names every variable somebody has to set", () => {
    // Written out because a reader is going to paste them. A missing one is a
    // support conversation; a wrong one is worse.
    render(<ConnectPanel onClose={() => {}} />);

    const shown = document.body.textContent ?? "";

    for (const variable of [
      "U2_HOST",
      "U2_USER",
      "U2_PASSWORD",
      "U2_ACCOUNT",
      "ANTHROPIC_API_KEY",
    ]) {
      expect(shown).toContain(variable);
    }
  });

  it("names the one command that checks the connection", () => {
    render(<ConnectPanel onClose={() => {}} />);

    expect(document.body.textContent).toContain("try-it-here.py");
  });

  it("says writes are refused by default", () => {
    // The first thing a database administrator asks, and the answer changed:
    // U2_READ_ONLY now defaults to true. A page still saying otherwise would be
    // telling them to set something they do not need to.
    render(<ConnectPanel onClose={() => {}} />);

    expect(document.body.textContent).toContain("U2_READ_ONLY");
    expect(screen.getByText(/refused by default/i)).toBeTruthy();
  });

  it("does not overstate what fits an unfamiliar schema", () => {
    // The counter screens need a mapping, and saying otherwise would be found
    // out in an afternoon. The page has to admit it in the same breath as the
    // parts that do fit.
    render(<ConnectPanel onClose={() => {}} />);

    const shown = document.body.textContent ?? "";

    expect(shown).toContain("Need a mapping");
    expect(shown).toContain("ErpFiles.cs");
  });

  it("closes on Escape", async () => {
    const closed = vi.fn();
    render(<ConnectPanel onClose={closed} />);

    await userEvent.keyboard("{Escape}");

    expect(closed).toHaveBeenCalled();
  });

  it("closes when the backdrop is clicked but not the panel", async () => {
    // Clicking inside a dialog must not dismiss it — somebody selecting a
    // command to copy would lose the page mid-drag.
    const closed = vi.fn();
    render(<ConnectPanel onClose={closed} />);

    await userEvent.click(screen.getByRole("dialog"));
    expect(closed).not.toHaveBeenCalled();

    await userEvent.click(screen.getByRole("dialog").parentElement!);
    expect(closed).toHaveBeenCalled();
  });
});
