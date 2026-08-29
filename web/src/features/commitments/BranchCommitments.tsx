/**
 * What is holding a branch's committed stock.
 *
 * This is what turns "no" into "not until Thursday", which is a materially
 * better answer for the customer. The shortfall row is the unusual part: where
 * the listed orders do not account for everything committed, the difference is
 * shown rather than quietly absorbed. A discrepancy someone can see is worth
 * more than a screen that adds up.
 */

import { useQuery } from "@tanstack/react-query";
import { api, ApiFailure } from "../../api/client";

interface Props {
  partNumber: string;
  branchCode: string;
  branchName: string;
  onClose: () => void;
}

export function BranchCommitments({
  partNumber,
  branchCode,
  branchName,
  onClose,
}: Props): React.JSX.Element {
  const { data, error, isPending, refetch } = useQuery({
    queryKey: ["commitments", partNumber, branchCode],
    queryFn: ({ signal }) => api.commitments(partNumber, branchCode, signal),
  });

  const failure = error instanceof ApiFailure ? error : null;

  return (
    <section className="panel" aria-labelledby="commitments-title">
      <div className="drawer__header">
        <h2 className="panel__title" id="commitments-title">
          What is holding stock at {branchName}
        </h2>
        <button type="button" className="button button--quiet" onClick={onClose}>
          Close
        </button>
      </div>

      {isPending && <p className="state__detail">Reading orders…</p>}

      {failure && (
        <div role="alert">
          <p className="state__detail">
            {failure.kind === "unreachable"
              ? "The system could not reach the order data. This is not the same as there being no commitments."
              : failure.message}
          </p>
          <button type="button" className="button button--quiet" onClick={() => void refetch()}>
            Try again
          </button>
        </div>
      )}

      {data && data.commitments.length === 0 && data.unaccounted === 0 && (
        <p className="state__detail">
          Nothing is committed at {branchName}. Every unit on hand is free to sell.
        </p>
      )}

      {data && (data.commitments.length > 0 || data.unaccounted > 0) && (
        <table className="commitments">
          <caption className="visually-hidden">
            Orders holding stock of {partNumber} at {branchName}
          </caption>
          <thead>
            <tr>
              <th scope="col">Order</th>
              <th scope="col">Customer</th>
              <th scope="col">State</th>
              <th scope="col">Promised</th>
              <th scope="col" style={{ textAlign: "right" }}>
                Quantity
              </th>
            </tr>
          </thead>
          <tbody>
            {data.commitments.map((commitment) => (
              <tr key={commitment.orderNumber}>
                <td className="governance__mono">{commitment.orderNumber}</td>
                <td>{commitment.customerName}</td>
                <td>{commitment.state.toLowerCase()}</td>
                <td className="figures">{commitment.promisedDate}</td>
                <td className="figures" style={{ textAlign: "right" }}>
                  {commitment.quantity}
                </td>
              </tr>
            ))}

            {data.unaccounted > 0 && (
              <tr className="commitments__unaccounted">
                <td colSpan={4}>
                  Committed to no order we can find — worth checking with the branch
                </td>
                <td className="figures" style={{ textAlign: "right" }}>
                  {data.unaccounted}
                </td>
              </tr>
            )}
          </tbody>
          <tfoot>
            <tr>
              <td colSpan={4} style={{ fontWeight: 600 }}>
                Committed in total
              </td>
              <td className="figures" style={{ textAlign: "right", fontWeight: 600 }}>
                {data.committedTotal}
              </td>
            </tr>
          </tfoot>
        </table>
      )}
    </section>
  );
}
