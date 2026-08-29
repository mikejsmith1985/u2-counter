/**
 * What the customer on the phone pays, and how that number was arrived at.
 *
 * List, multiplier and net are shown together on purpose. A representative
 * reading a price aloud is routinely asked "why that much?", and a screen
 * showing only the net figure forces them to escalate a question they could
 * have answered in the same breath.
 */

import type { PricingView } from "../../api/types";

interface Props {
  pricing: PricingView;
  /** Null when nobody has been selected, which changes what is offered. */
  customerName: string | null;
}

const CURRENCY = new Intl.NumberFormat(undefined, {
  style: "currency",
  currency: "USD",
});

export function PricingPanel({ pricing, customerName }: Props): React.JSX.Element {
  const isContract = pricing.basis === "Contract";

  return (
    <section className="panel" aria-labelledby="pricing-title">
      <h2 className="panel__title" id="pricing-title">
        Price {customerName ? `for ${customerName}` : "(list)"}
      </h2>

      <div className="pricing">
        <div className="pricing__figure">
          <span className="pricing__label">List</span>
          <span className="pricing__value figures">{CURRENCY.format(pricing.listPrice)}</span>
        </div>

        {isContract && pricing.multiplier !== null && (
          <div className="pricing__figure">
            <span className="pricing__label">Multiplier</span>
            <span className="pricing__value figures">
              ×{pricing.multiplier.toFixed(2)}
            </span>
          </div>
        )}

        <div className="pricing__figure">
          <span className="pricing__label">{isContract ? "Net" : "Price"}</span>
          <span className="pricing__value pricing__value--net figures">
            {CURRENCY.format(pricing.netPrice)}
          </span>
        </div>
      </div>

      {isContract && pricing.termsDescription && (
        <p className="pricing__terms">Under {pricing.termsDescription}.</p>
      )}

      {!isContract && !customerName && (
        <p className="pricing__terms">
          This is list price. Select a customer to see what they pay.
        </p>
      )}

      {!isContract && customerName && (
        <p className="pricing__terms">
          {customerName} has no contract terms covering this category, so this is list price.
        </p>
      )}

      {/* Terms that did not apply. Present because an unexpectedly high price is
          exactly when someone asks, and the answer is usually an expired
          promotion rather than a mistake. */}
      {pricing.disregardedTerms.length > 0 && (
        <div className="pricing__disregarded">
          <p style={{ margin: "0 0 0.2rem" }}>Terms on file that did not apply:</p>
          <ul style={{ margin: 0, paddingLeft: "1rem" }}>
            {pricing.disregardedTerms.map((terms) => (
              <li key={`${terms.termsDescription}-${terms.reason}`}>
                {terms.termsDescription} — {terms.reason}
              </li>
            ))}
          </ul>
        </div>
      )}
    </section>
  );
}
