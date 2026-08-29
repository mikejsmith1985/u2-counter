/**
 * Changing a value, and being shown what it did.
 *
 * A MultiValue database will not catch a bad write. There are no constraints to
 * break, so a record that has just had a quantity moved onto the wrong branch is
 * still a perfectly valid record and every later read agrees with it. The only
 * evidence available is whether any field changed length — which is why the
 * panel shows that comparison rather than a success message.
 *
 * These cover the parts that are judgement rather than looks: that no edit is
 * offered where writing is impossible, that a write is confirmed before it
 * happens, and that a change which moved a value is reported as a failure rather
 * than dressed up as a success.
 */

import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { createElement } from "react";

import { UpdateValue } from "../src/features/explore/UpdateValue";
import type { DictionaryField } from "../src/api/types";

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  });
  return createElement(QueryClientProvider, { client }, children);
}

const onHand: DictionaryField = {
  name: "ON.HAND",
  position: 2,
  heading: "On Hand",
  format: "8R",
  isMultiValued: true,
  conversion: "",
};

/** A change that left every field the length it was. */
const clean = {
  recordId: "P-1",
  before: "a",
  after: "b",
  fieldLengthsBefore: [4, 4, 4],
  fieldLengthsAfter: [4, 4, 4],
  isAlignmentPreserved: true,
};

/** A change that moved a value onto a different position. */
const moved = {
  ...clean,
  fieldLengthsAfter: [4, 5, 4],
  isAlignmentPreserved: false,
};

function answer(canWrite: boolean, change: unknown = clean) {
  return vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);

    if (url.includes("/records/status")) {
      return Promise.resolve(
        new Response(JSON.stringify({ canWrite }), { status: 200 }),
      );
    }

    if (init?.method === "POST") {
      return Promise.resolve(new Response(JSON.stringify(change), { status: 200 }));
    }

    return Promise.reject(new Error(`unexpected request: ${url}`));
  });
}

function renderEditor() {
  return render(
    <UpdateValue
      file="INVENTORY"
      recordId="P-1"
      field={onHand}
      index={0}
      current="16"
      onClose={() => {}}
      onChanged={() => {}}
    />,
    { wrapper },
  );
}

describe("changing a value", () => {
  beforeEach(() => vi.stubGlobal("fetch", answer(true)));
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("offers no editor at all where the deployment cannot write", async () => {
    // Not a disabled button. A write control that exists and refuses invites the
    // question of what else is switched off.
    vi.stubGlobal("fetch", answer(false));

    renderEditor();

    await waitFor(() => expect(screen.getByText(/reads only/i)).toBeTruthy());
    expect(screen.queryByRole("button", { name: /review the change/i })).toBeNull();
  });

  it("will not offer to write until something has changed", async () => {
    renderEditor();

    await waitFor(() =>
      expect(screen.getByRole("button", { name: /review the change/i })).toBeTruthy(),
    );

    expect(
      screen.getByRole("button", { name: /review the change/i }),
    ).toHaveProperty("disabled", true);
  });

  it("confirms before writing, naming the record and the position", async () => {
    // Somebody confirming a write should be told which record and which
    // position, not asked to remember what they clicked two screens ago.
    renderEditor();

    await waitFor(() => screen.getByRole("button", { name: /review the change/i }));

    const box = screen.getByLabelText("Change to");
    await userEvent.clear(box);
    await userEvent.type(box, "20");
    await userEvent.click(screen.getByRole("button", { name: /review the change/i }));

    const confirmation = screen.getByText(/write/i, { selector: ".update__confirm" });

    expect(confirmation.textContent).toContain("P-1");
    expect(confirmation.textContent).toContain("field 2");
  });

  it("reports a clean change as having kept every field's length", async () => {
    renderEditor();
    await waitFor(() => screen.getByRole("button", { name: /review the change/i }));

    const box = screen.getByLabelText("Change to");
    await userEvent.clear(box);
    await userEvent.type(box, "20");
    await userEvent.click(screen.getByRole("button", { name: /review the change/i }));
    await userEvent.click(screen.getByRole("button", { name: /write it/i }));

    await waitFor(() =>
      expect(screen.getByText(/still holds the same number of values/i)).toBeTruthy(),
    );
  });

  it("reports a change that moved a value as a failure, not a success", async () => {
    // The whole point. The write succeeded, the record is valid, and it is
    // wrong -- so "written" on its own would be a true statement that misleads.
    vi.stubGlobal("fetch", answer(true, moved));

    renderEditor();
    await waitFor(() => screen.getByRole("button", { name: /review the change/i }));

    const box = screen.getByLabelText("Change to");
    await userEvent.clear(box);
    await userEvent.type(box, "20");
    await userEvent.click(screen.getByRole("button", { name: /review the change/i }));
    await userEvent.click(screen.getByRole("button", { name: /write it/i }));

    await waitFor(() =>
      expect(screen.getByText(/moved onto a different position/i)).toBeTruthy(),
    );

    // And the field that moved is marked, rather than left for somebody to spot
    // by comparing two rows of numbers.
    expect(document.querySelectorAll(".update__moved")).toHaveLength(1);
  });
});
