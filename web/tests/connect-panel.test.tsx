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
    // Scoped to the note. The phrase also appears inside the setup prompt, which
    // is correct -- the agent doing the setup needs telling too.
    const note = document.querySelector(".connect__note--good");
    expect(note?.textContent).toMatch(/writes are refused by default/i);
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

  it("offers both paths and does not pretend they are equivalent", () => {
    // The old page blurred these. Looking at a hosted demonstration proves the
    // thing runs; running it against your own database is the only version that
    // proves anything about your data, and the page has to say so.
    render(<ConnectPanel onClose={() => {}} />);

    const shown = document.body.textContent ?? "";

    expect(shown).toContain("Look at it here");
    expect(shown).toContain("Run it yourself");
    expect(shown).toContain("github.com/mikejsmith1985/u2-mcp");
    expect(shown).toContain("github.com/mikejsmith1985/u2-counter");
  });

  it("rewrites the commands with whatever values are typed", async () => {
    // The point of the form. If the boxes take input and the commands below
    // still show placeholders, somebody pastes a command naming a machine that
    // is not theirs -- which fails in a way that reads like our bug.
    render(<ConnectPanel onClose={() => {}} />);

    await userEvent.type(screen.getByLabelText("Host"), "uv.acme.internal");
    await userEvent.type(screen.getByLabelText("Account"), "LIVE.WHOLESALE");

    const shown = document.body.textContent ?? "";

    expect(shown).toContain("uv.acme.internal");
    expect(shown).toContain("LIVE.WHOLESALE");
    expect(shown).not.toContain("uv.internal.example.com");
  });

  it("never asks for a password in the page", () => {
    // A password belongs in the environment. A box for one on a web page invites
    // it into a screenshot, a bug report, or a browser's saved form data.
    render(<ConnectPanel onClose={() => {}} />);

    for (const field of screen.getAllByRole("textbox")) {
      expect(field.getAttribute("type")).not.toBe("password");
      expect((field.getAttribute("aria-label") ?? "").toLowerCase()).not.toContain("password");
    }

    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("hands the whole setup to somebody else's agent, including the refusals", async () => {
    // The prompt is only useful if it carries the parts that make the setup
    // safe: no guessing at credentials, and stop rather than work around a
    // failed step. A prompt that omits those is worse than none.
    render(<ConnectPanel onClose={() => {}} />);

    await userEvent.click(screen.getByText(/Read it first/i));

    const prompt = document.querySelector(".connect__prompt pre")?.textContent ?? "";

    expect(prompt).toContain("try-it-here.py");
    expect(prompt).toContain("do not guess");
    expect(prompt).toContain("stop at the first step");
    expect(prompt).toContain("restored copy");
    expect(prompt).not.toMatch(/sk-ant-[A-Za-z0-9]/);
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
