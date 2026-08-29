/**
 * The strip says how this is hosted, because the cost choice shows as a delay.
 *
 * The deployment powers itself down when nobody is using it, so a first visit
 * after a quiet period waits about twenty seconds while a container starts. The
 * page is served by that same container, so for most of that wait the browser
 * has nothing at all to show -- the application's own waking screen cannot
 * render until the thing that serves it is running.
 *
 * That makes this note the only place a reviewer who waited can find out the
 * delay was chosen rather than suffered. A deliberate trade that goes unsaid
 * reads exactly like a system that is merely slow, which is the opposite of the
 * impression it is meant to give.
 */

import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";

import { GovernanceStrip } from "../src/features/governance/GovernanceStrip";
import type { SessionResponse } from "../src/api/types";

/** A signed-in session, which is when the strip renders its detail. */
const session: SessionResponse = {
  subject: "demo|dana",
  displayName: "Dana Whitfield",
  homeBranchCode: "DEN",
  databaseLogin: "u2demo@DEMO",
  databaseLoginIsShared: true,
  isReadOnly: true,
  isDemonstrationData: true,
};

function renderStrip() {
  return render(
    <GovernanceStrip
      session={session}
      onShowActivity={() => {}}
      onChangeIdentity={() => {}}
    />,
  );
}

describe("the hosting note", () => {
  it("says the deployment scales to zero", () => {
    renderStrip();

    expect(screen.getByText(/scales to zero/i)).toBeTruthy();
  });

  it("names what the delay buys", () => {
    // The number on its own is a curiosity. "$0 idle" is the reason, and the
    // reason is the whole point of saying anything.
    renderStrip();

    expect(screen.getByText(/\$0 idle/i)).toBeTruthy();
  });

  it("explains the wait in full for anyone who looks closer", () => {
    renderStrip();

    const note = screen.getByText(/scales to zero/i).closest("span");
    const explanation = note?.getAttribute("title") ?? "";

    // The three things a reviewer needs: that it powers down, roughly how long
    // the first visit takes, and that the alternative was paying continuously.
    expect(explanation).toMatch(/powers itself down/i);
    expect(explanation).toMatch(/twenty seconds/i);
    expect(explanation).toMatch(/cost/i);
  });

  it("is not a control, because activating it does nothing", () => {
    // It carries an explanation, not an action. Rendering it as something
    // clickable would promise a behaviour that does not exist.
    renderStrip();

    const note = screen.getByText(/scales to zero/i).closest("span");

    expect(note?.tagName).toBe("SPAN");
  });
});
