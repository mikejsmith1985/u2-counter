/**
 * Turning what is on screen into text someone can paste into an order or an email.
 *
 * Two things travel with it that a reader might not think to ask for: the time
 * it was taken, and a note that this is demonstration data. A stock figure
 * pasted into an email loses all its context, and weeks later nobody can tell
 * whether it was live, current, or real. Stamping it is the difference between a
 * useful note and a misleading one.
 */

import type { AvailabilityResponse } from "../../api/types";

const CURRENCY = new Intl.NumberFormat(undefined, {
  style: "currency",
  currency: "USD",
});

/**
 * Build the plain-text summary for the clipboard.
 *
 * @param availability What the screen is showing.
 * @param customerName Whose price is quoted, or null for list.
 * @returns Text containing nothing the user cannot see on screen.
 */
export function buildSummary(
  availability: AvailabilityResponse,
  customerName: string | null,
): string {
  const lines: string[] = [];

  lines.push(`${availability.part.partNumber} — ${availability.part.description}`);
  lines.push(`${availability.part.manufacturer} · sold by ${availability.part.unitOfMeasure}`);

  if (availability.part.isDiscontinued) {
    lines.push("This part is discontinued.");
  }

  lines.push("");

  if (!availability.isStockKnown) {
    lines.push("Stock is not recorded for this part, so its availability is unknown.");
  } else {
    lines.push(`Free to sell across all branches: ${availability.totalFreeToSell}`);
    lines.push("");

    for (const branch of availability.branches) {
      const detail =
        `${branch.freeToSell} free · ${branch.onHand} on hand · ${branch.committed} committed` +
        (branch.onOrder > 0 ? ` · ${branch.onOrder} on order` : "");

      lines.push(`  ${branch.branchName.padEnd(20)} ${detail}`);
    }
  }

  lines.push("");

  const pricing = availability.pricing;
  if (pricing.basis === "Contract" && pricing.multiplier !== null) {
    lines.push(
      `Price for ${customerName ?? "this customer"}: ` +
        `${CURRENCY.format(pricing.netPrice)} ` +
        `(list ${CURRENCY.format(pricing.listPrice)} × ${pricing.multiplier.toFixed(2)})`,
    );
  } else {
    lines.push(`List price: ${CURRENCY.format(pricing.listPrice)}`);
  }

  lines.push("");
  lines.push(`Read at ${new Date(availability.envelope.retrievedAt).toLocaleString()}.`);

  if (availability.envelope.isDemonstrationData) {
    lines.push("These are demonstration figures, not live stock.");
  }

  if (!availability.envelope.isComplete && availability.envelope.warning) {
    lines.push(availability.envelope.warning);
  }

  return lines.join("\n");
}
