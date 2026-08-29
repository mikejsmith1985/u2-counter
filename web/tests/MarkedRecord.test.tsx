/**
 * The stored record is shown with its structure visible, never tidied away.
 *
 * This is the screen an ERP developer will look at hardest, and its entire claim
 * is that what it shows is what is stored. The separators are invisible
 * characters: printing them raw shows one unbroken run of text, which reads as
 * though the record has no structure at all. Hiding them is worse — it is the
 * interface deciding the reader does not need to know how the data is shaped.
 *
 * So each mark present becomes a labelled badge, in place, and these tests hold
 * that line.
 */

import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { MarkLegend, MarkedRecord } from "../src/features/record/RecordDrawer";
import type { MarkDescription } from "../src/api/types";

const ATTRIBUTE_MARK = String.fromCharCode(254);
const VALUE_MARK = String.fromCharCode(253);
const SUBVALUE_MARK = String.fromCharCode(252);

const ALL_MARKS: MarkDescription[] = [
  { character: ATTRIBUTE_MARK, code: 254, name: "Attribute mark", separates: "Fields" },
  { character: VALUE_MARK, code: 253, name: "Value mark", separates: "Values within a field" },
  {
    character: SUBVALUE_MARK,
    code: 252,
    name: "Subvalue mark",
    separates: "Sub-items within a value",
  },
];

describe("the stored record", () => {
  it("renders a badge for every mark the record contains", () => {
    const raw = `DEN${VALUE_MARK}AUR${ATTRIBUTE_MARK}60${VALUE_MARK}10`;

    const { container } = render(<MarkedRecord raw={raw} marks={ALL_MARKS} />);

    // Two value marks and one attribute mark, each shown where it occurs rather
    // than summarised at the end.
    expect(container.querySelectorAll(".mark--vm")).toHaveLength(2);
    expect(container.querySelectorAll(".mark--am")).toHaveLength(1);
  });

  it("keeps the data between the marks exactly as stored", () => {
    const raw = `DEN${VALUE_MARK}AUR${ATTRIBUTE_MARK}60`;

    const { container } = render(<MarkedRecord raw={raw} marks={ALL_MARKS} />);

    const text = container.textContent ?? "";

    expect(text).toContain("DEN");
    expect(text).toContain("AUR");
    expect(text).toContain("60");
  });

  it("never leaves an invisible character on the page", () => {
    // The failure this catches: a mark rendered as itself. It would look like
    // nothing at all, and the reader would conclude the fields run together.
    const raw = `a${ATTRIBUTE_MARK}b${VALUE_MARK}c${SUBVALUE_MARK}d`;

    const { container } = render(<MarkedRecord raw={raw} marks={ALL_MARKS} />);

    const text = container.textContent ?? "";

    expect(text).not.toContain(ATTRIBUTE_MARK);
    expect(text).not.toContain(VALUE_MARK);
    expect(text).not.toContain(SUBVALUE_MARK);
  });

  it("labels each badge with the character code it stands for", () => {
    // 254, 253 and 252 are how these are named in every MultiValue reference.
    // A badge without its number cannot be looked up.
    const { container } = render(
      <MarkedRecord raw={`a${ATTRIBUTE_MARK}b`} marks={ALL_MARKS} />,
    );

    expect(container.querySelector('[title="Character 254"]')).not.toBeNull();
  });

  it("shows an empty value as an empty gap between two marks", () => {
    // A branch with no bin recorded leaves a trailing empty value, and the file
    // contract is explicit that it must stay rather than shortening the field.
    const raw = `A-12${VALUE_MARK}${VALUE_MARK}C-04`;

    const { container } = render(<MarkedRecord raw={raw} marks={ALL_MARKS} />);

    expect(container.querySelectorAll(".mark--vm")).toHaveLength(2);
    expect(container.textContent).toContain("A-12");
    expect(container.textContent).toContain("C-04");
  });

  it("renders a record with no marks as plain text", () => {
    const { container } = render(<MarkedRecord raw="DEN" marks={[]} />);

    expect(container.querySelectorAll(".mark")).toHaveLength(0);
    expect(container.textContent).toBe("DEN");
  });
});

describe("the legend", () => {
  it("names every mark present and says what it separates", () => {
    // Written for someone who has never seen one of these records, because that
    // is exactly who the drawer is explaining the format to.
    render(<MarkLegend marks={ALL_MARKS} />);

    expect(screen.getByText(/Attribute mark/)).toBeDefined();
    expect(screen.getByText(/separates fields/i)).toBeDefined();
    expect(screen.getByText(/separates values within a field/i)).toBeDefined();
  });

  it("describes only the marks the record actually holds", () => {
    // Labelling a subvalue mark on a record with none would explain a structure
    // that is not there, which misleads exactly as much as hiding one.
    render(<MarkLegend marks={ALL_MARKS.slice(0, 1)} />);

    expect(screen.queryByText(/Subvalue mark/)).toBeNull();
  });
});
