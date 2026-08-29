/**
 * What gets copied is what was on screen, and nothing else.
 *
 * A stock figure pasted into an email loses every piece of context that made it
 * safe to read. The copied text therefore carries the same qualifications the
 * screen carried -- the time it was taken, that these are demonstration figures,
 * that stock is unknown rather than zero -- because the person reading the paste
 * a week later has nothing else to go on.
 *
 * It must also contain nothing the screen did not show. A summary that quietly
 * added a figure would be the one place in this application where the pasted
 * number and the displayed number could disagree.
 */

import { describe, expect, it } from "vitest";
import { buildSummary } from "../src/features/availability/copySummary";
import type { AvailabilityResponse } from "../src/api/types";

const RETRIEVED_AT = "2026-08-28T17:04:00.000Z";

function anAvailability(
  overrides: Partial<AvailabilityResponse> = {},
): AvailabilityResponse {
  return {
    part: {
      partNumber: "E-BRK00208",
      description: "100A GFCI Breaker",
      manufacturer: "Eaton",
      manufacturerPartNumber: "MPN00208",
      unitOfMeasure: "EA",
      isDiscontinued: false,
    },
    isStockKnown: true,
    totalFreeToSell: 805,
    branches: [
      {
        branchCode: "COS",
        branchName: "Colorado Springs",
        city: "Colorado Springs",
        onHand: 400,
        committed: 2,
        freeToSell: 398,
        onOrder: 0,
        bin: "A-12",
        stockState: "Available",
        isBranchKnown: true,
      },
      {
        branchCode: "AUR",
        branchName: "Aurora",
        city: "Aurora",
        onHand: 5,
        committed: 5,
        freeToSell: 0,
        onOrder: 200,
        bin: "C-04",
        stockState: "AllCommitted",
        isBranchKnown: true,
      },
    ],
    pricing: {
      listPrice: 384.99,
      netPrice: 384.99,
      multiplier: null,
      basis: "List",
      termsDescription: null,
      disregardedTerms: [],
    },
    envelope: {
      isComplete: true,
      warning: null,
      isDemonstrationData: true,
      retrievedAt: RETRIEVED_AT,
    },
    ...overrides,
  };
}

describe("the copied summary", () => {
  it("names the part the way the screen does", () => {
    const text = buildSummary(anAvailability(), null);

    expect(text).toContain("E-BRK00208");
    expect(text).toContain("100A GFCI Breaker");
    expect(text).toContain("Eaton");
  });

  it("carries every branch figure that was on screen", () => {
    const text = buildSummary(anAvailability(), null);

    expect(text).toContain("Colorado Springs");
    expect(text).toContain("398 free");
    expect(text).toContain("400 on hand");
    expect(text).toContain("Aurora");
    expect(text).toContain("200 on order");
  });

  it("contains no figure that was not on screen", () => {
    // Every number in the text has to be one the response carried. A summary
    // that computed something of its own would be the one place where the paste
    // and the screen could disagree.
    const availability = anAvailability();
    const text = buildSummary(availability, null);

    const permitted = new Set<string>([
      String(availability.totalFreeToSell),
      String(availability.pricing.listPrice),
      ...availability.branches.flatMap((branch) => [
        String(branch.onHand),
        String(branch.committed),
        String(branch.freeToSell),
        String(branch.onOrder),
      ]),
    ]);

    const onScreenText = [
      availability.part.partNumber,
      availability.part.description,
      availability.part.manufacturer,
      availability.part.unitOfMeasure,
      ...availability.branches.flatMap((branch) => [branch.branchName, branch.bin]),
    ].join(" ");

    // The timestamp line is excluded: its numbers are a rendering of a value the
    // response did carry, in whatever form the reader's locale writes dates.
    const withoutTimestamp = text
      .split("\n")
      .filter((line) => !line.startsWith("Read at "))
      .join("\n");

    for (const number of withoutTimestamp.match(/\d+/g) ?? []) {
      const isPermitted =
        permitted.has(number) ||
        // Text the screen showed can carry digits of its own -- a part number,
        // and a description like "100A GFCI Breaker". Those are on screen too.
        onScreenText.includes(number) ||
        // A price splits into digit runs either side of its decimal point.
        String(availability.pricing.listPrice).includes(number);

      expect(isPermitted, `${number} appears in the summary but not on screen`).toBe(true);
    }
  });

  it("says stock is unknown rather than reporting none", () => {
    // The distinction the whole application exists to keep. Pasted into an
    // email, "0 free to sell" is a statement someone will act on.
    const text = buildSummary(
      anAvailability({ isStockKnown: false, branches: [], totalFreeToSell: 0 }),
      null,
    );

    expect(text).toMatch(/not recorded/i);
    expect(text).not.toMatch(/Free to sell across all branches/);
  });

  it("stamps the time the figures were read", () => {
    const text = buildSummary(anAvailability(), null);

    expect(text).toMatch(/^Read at /m);
  });

  it("says plainly that the figures are a demonstration", () => {
    // Without this line the paste is indistinguishable from live stock, and the
    // person reading it has no way to find out.
    const text = buildSummary(anAvailability(), null);

    expect(text).toMatch(/demonstration/i);
  });

  it("names the customer whose price is quoted", () => {
    const text = buildSummary(
      anAvailability({
        pricing: {
          listPrice: 384.99,
          netPrice: 250.24,
          multiplier: 0.65,
          basis: "Contract",
          termsDescription: "Contract terms",
          disregardedTerms: [],
        },
      }),
      "Front Range Electric",
    );

    // A contract price with no customer beside it is a number nobody can check.
    expect(text).toContain("Front Range Electric");
    expect(text).toContain("0.65");
  });

  it("marks a discontinued part as discontinued", () => {
    const text = buildSummary(
      anAvailability({
        part: { ...anAvailability().part, isDiscontinued: true },
      }),
      null,
    );

    expect(text).toMatch(/discontinued/i);
  });

  it("carries a partial-answer warning rather than dropping it", () => {
    // The warning is the reason the figures might be incomplete. Copying the
    // figures without it is the one case where the paste is worse than nothing.
    const text = buildSummary(
      anAvailability({
        envelope: {
          isComplete: false,
          warning: "Only the first 500 orders were considered.",
          isDemonstrationData: true,
          retrievedAt: RETRIEVED_AT,
        },
      }),
      null,
    );

    expect(text).toContain("Only the first 500 orders were considered.");
  });
});
