/**
 * The parsed record, and the claim it is supposed to make visible.
 *
 * "Position three of every field describes the same branch" was written on the
 * screen and then contradicted by the screen: fields were listed one per line,
 * values joined by dots, and a single-valued field sat in the list looking as
 * though it ought to have a third position too. Read against a real record the
 * claim looks false, which is worse than not making it.
 *
 * These pin the layout that makes it true instead of asserted — parallel fields
 * as columns, positions as rows — and the case that matters most: a record whose
 * fields disagree on length must show the disagreement rather than pad it away.
 */

import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";

import { ParallelFields } from "../src/features/record/ParallelFields";

/** A well-formed record: two single-valued fields, three parallel ones. */
const ALIGNED: Record<string, string[]> = {
  "1": ["20A AFCI Breaker"],
  "2": ["EA"],
  "3": ["DEN", "AUR", "BOU"],
  "4": ["20", "10", "417"],
  "5": ["A-37", "B-07", "D-36"],
};

/** The damage: one field holds fewer values than its neighbours. */
const RAGGED: Record<string, string[]> = {
  "1": ["DEN", "AUR", "BOU", "COS"],
  "2": ["20", "10", "417", "27"],
  "3": ["A-37", "B-07"],
};

describe("the parsed record", () => {
  it("keeps single-valued fields out of the grid", () => {
    // The original mistake. A description has no third branch, and listing it
    // beside fields that do implies a structure that is not there.
    render(<ParallelFields parsed={ALIGNED} />);

    const grid = document.querySelector(".parallel__table");
    expect(grid?.textContent).not.toContain("20A AFCI Breaker");
    expect(document.querySelector(".parallel__single")?.textContent).toContain(
      "20A AFCI Breaker",
    );
  });

  it("puts each position on one row, across every parallel field", () => {
    // The claim, made structural. Row three holds BOU, 417 and D-36 because
    // those are the same branch -- not because a caption says so.
    render(<ParallelFields parsed={ALIGNED} />);

    const rows = document.querySelectorAll(".parallel__table tbody tr");
    expect(rows).toHaveLength(3);

    const third = rows[2].textContent ?? "";
    expect(third).toContain("BOU");
    expect(third).toContain("417");
    expect(third).toContain("D-36");
  });

  it("says how many values each parallel field holds, when they agree", () => {
    render(<ParallelFields parsed={ALIGNED} />);

    expect(document.body.textContent).toMatch(/every one holds/i);
  });

  it("shows a disagreement rather than padding it away", () => {
    // The failure the write path exists to prevent. A record whose fields no
    // longer line up is still a valid record and every later read agrees with
    // it, which is exactly why it has to be visible here.
    render(<ParallelFields parsed={RAGGED} />);

    expect(document.body.textContent).toMatch(/do not agree on how many/i);

    const rows = document.querySelectorAll(".parallel__table tbody tr");
    expect(rows).toHaveLength(4);

    // The positions the short field does not have are marked, not blank.
    expect(document.querySelectorAll(".parallel__missing").length).toBe(2);
  });

  it("renders nothing surprising for a record with no parallel fields at all", () => {
    render(<ParallelFields parsed={{ "1": ["ALPINE6"], "2": ["Alpine Electrical"] }} />);

    expect(document.querySelector(".parallel__table")).toBeNull();
    expect(screen.getByText(/describe the record, not a position/i)).toBeTruthy();
  });
});
