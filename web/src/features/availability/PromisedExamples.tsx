/**
 * A way in to the one thing worth showing, on a screen that has nothing yet.
 *
 * Free to sell is on hand minus committed, and that difference is the reason
 * this application exists. It is also invisible on most of the catalogue: most
 * parts have nothing committed at any branch, so somebody who picks a part at
 * random reads a column of identical zeroes and concludes it is decoration. The
 * claim is made in the tour, in the answer, and in the record, and then the data
 * they happen to choose quietly fails to demonstrate it.
 *
 * So the empty screen offers a few parts where it is real, with the numbers on
 * the button: forty on hand, thirty-nine promised, one to sell. Nothing is
 * hardcoded — the account is asked — and if it answers that nothing is committed
 * anywhere, which a quiet real ERP would, this renders nothing at all rather
 * than making a claim the data cannot support.
 */

import { useQuery } from "@tanstack/react-query";
import { api } from "../../api/client";

/** How many examples to offer. Enough to look like a pattern, few enough to scan. */
const EXAMPLES = 3;

interface Props {
  /** Open one of them. */
  onSelect: (partNumber: string) => void;
}

export function PromisedExamples({ onSelect }: Props): React.JSX.Element | null {
  const { data } = useQuery({
    queryKey: ["parts", "promised"],
    queryFn: ({ signal }) => api.promisedParts(EXAMPLES, signal),
    staleTime: Infinity,
    retry: false,
  });

  if (!data || data.length === 0) {
    return null;
  }

  return (
    <section className="promised" aria-label="Parts with stock already promised">
      <h3 className="promised__title">Stock that is already spoken for</h3>
      <p className="promised__lead">
        On most parts nothing is committed, so free to sell and on hand are the
        same number and the difference does not show. These are parts where it
        does — which is the distinction the whole screen is built around.
      </p>

      <div className="promised__list">
        {data.map((part) => (
          <button
            key={part.partNumber}
            type="button"
            className="promised__part"
            onClick={() => onSelect(part.partNumber)}
          >
            <span className="promised__number">{part.partNumber}</span>
            <span className="promised__description">{part.description}</span>
            <span className="promised__sum">
              at {part.branchCode}:{" "}
              <span className="figures">{part.onHand}</span> on hand,{" "}
              <span className="figures">{part.committed}</span> promised,{" "}
              <strong className="figures">{part.freeToSell}</strong> free to sell
            </span>
          </button>
        ))}
      </div>
    </section>
  );
}
