/**
 * Choosing the customer on the phone, so the price shown is theirs.
 *
 * Kept in the header rather than on the part screen: the customer is fixed for
 * the length of a call, while the parts they ask about are not. Changing part
 * must not lose the customer.
 */

import { useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api } from "../../api/client";

const SETTLE_MS = 180;
const MINIMUM_QUERY_LENGTH = 2;

interface Props {
  selectedAccount: string | null;
  onSelect: (account: string | null, name: string | null) => void;
}

export function CustomerSelector({ selectedAccount, onSelect }: Props): React.JSX.Element {
  const [text, setText] = useState("");
  const [settled, setSettled] = useState("");
  const [isOpen, setIsOpen] = useState(false);
  const [chosenName, setChosenName] = useState<string | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const timer = setTimeout(() => setSettled(text.trim()), SETTLE_MS);
    return () => clearTimeout(timer);
  }, [text]);

  const { data } = useQuery({
    queryKey: ["customers", settled],
    queryFn: ({ signal }) => api.searchCustomers(settled, signal),
    enabled: settled.length >= MINIMUM_QUERY_LENGTH && isOpen,
    staleTime: 60_000,
  });

  if (selectedAccount && chosenName) {
    return (
      <div className="search" style={{ maxWidth: "18rem" }} ref={containerRef}>
        <button
          type="button"
          className="button button--quiet"
          style={{ width: "100%", textAlign: "left" }}
          onClick={() => {
            onSelect(null, null);
            setChosenName(null);
            setText("");
          }}
          aria-label={`Serving ${chosenName}. Activate to clear the customer.`}
        >
          {chosenName} <span className="branch__code">clear</span>
        </button>
      </div>
    );
  }

  return (
    <div className="search" style={{ maxWidth: "18rem" }} ref={containerRef}>
      <input
        className="search__input"
        type="search"
        aria-label="Select the customer being served"
        placeholder="Customer (for their price)…"
        value={text}
        onChange={(event) => {
          setText(event.target.value);
          setIsOpen(true);
        }}
        onFocus={() => setIsOpen(true)}
        onKeyDown={(event) => {
          if (event.key === "Escape") {
            setText("");
            setIsOpen(false);
          }
        }}
      />

      {isOpen && data && data.results.length > 0 && (
        <ul className="search__results" role="listbox" aria-label="Matching customers">
          {data.results.map((customer) => (
            <li
              key={customer.accountNumber}
              className="search__result"
              role="option"
              aria-selected={false}
              style={{ gridTemplateColumns: "7rem 1fr auto" }}
              onClick={() => {
                onSelect(customer.accountNumber, customer.name);
                setChosenName(customer.name);
                setIsOpen(false);
                setText("");
              }}
            >
              <span className="search__result-number">{customer.accountNumber}</span>
              <span className="search__result-description">{customer.name}</span>
              <span className="branch__code">
                {customer.priceClass || "list price"}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
