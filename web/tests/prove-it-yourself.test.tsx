/**
 * The answer to "how would anybody know this is real?".
 *
 * The question was asked of the record view in almost those words, and it is the
 * right one: every pixel on that screen is drawn by this application, so showing
 * separators proves that separators were drawn. The failure this guards against
 * is the tempting one — answering it with a more convincing picture, and quietly
 * implying the screen has settled something it cannot.
 *
 * So these pin the honesty rather than the rendering: that the limit is stated,
 * that the bytes are handed over rather than described, that the parser will
 * accept a record from somewhere else entirely, and that the only real proof is
 * named as being somewhere other than this screen.
 */

import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { ProveItYourself } from "../src/features/record/ProveItYourself";

/** A record shaped like this demonstration's inventory. */
const RECORD = ["E-BRK00008", "DENýAURýBOU", "20ý10ý417"].join("þ");

/** Open the panel, which is folded away until somebody asks. */
async function open(): Promise<void> {
  await userEvent.click(screen.getByText(/how would you know this is real/i));
}

describe("proving the record is real", () => {
  it("says outright that the screen cannot settle it", async () => {
    // The load-bearing sentence. A panel that answers this question with a
    // better picture is claiming something a picture cannot support.
    render(<ProveItYourself raw={RECORD} />);
    await open();

    const shown = document.body.textContent ?? "";

    expect(shown).toMatch(/you would not, from this screen/i);
    expect(shown).toMatch(/drawn by this application/i);
  });

  it("hands over the bytes rather than describing them", async () => {
    // Copied out and counted somewhere else is worth more than any rendering:
    // a fabricated record would have to survive somebody else's parser with
    // its parallel fields intact.
    render(<ProveItYourself raw={RECORD} />);
    await open();

    expect(document.querySelector(".prove__bytes pre")?.textContent).toBe(RECORD);
    expect(screen.getByRole("button", { name: /copy the raw record/i })).toBeTruthy();
  });

  it("names the byte values, so they can be checked without trusting the label", async () => {
    render(<ProveItYourself raw={RECORD} />);
    await open();

    const shown = document.body.textContent ?? "";

    expect(shown).toContain("254");
    expect(shown).toContain("253");
  });

  it("renders a record it has never seen", async () => {
    // The point of the exercise. A renderer written around one fixture falls
    // over on somebody else's data; this one must not.
    render(<ProveItYourself raw={RECORD} />);
    await open();

    const foreign = ["ACME-1", "BRASS WIDGET"].join("þ") + "þ" + ["N", "S"].join("ý");

    await userEvent.type(
      screen.getByLabelText(/paste a multivalue record/i),
      foreign.replace(/[{[]/g, "$&$&"),
    );

    const rendered = document.querySelector(".prove__rendered")?.textContent ?? "";

    expect(rendered).toContain("ACME-1");
    expect(rendered).toContain("BRASS WIDGET");
  });

  it("points at the only thing that actually settles it", async () => {
    // Running it against a database the reader controls. Naming it here keeps
    // the panel from implying that reading this page was enough.
    render(<ProveItYourself raw={RECORD} />);
    await open();

    const shown = document.body.textContent ?? "";

    expect(shown).toMatch(/neither of these is proof/i);
    expect(shown).toMatch(/database you control/i);
    expect(shown).toMatch(/use your own data/i);
  });
});
