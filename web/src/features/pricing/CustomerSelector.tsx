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
  const [highlighted, setHighlighted] = useState(0);
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

  const results = data?.results ?? [];

  /**
   * Serve this customer, and put the box back the way it was.
   *
   * Shared by the mouse and the keyboard so the two cannot drift: a selector
   * where clicking a row and pressing Enter on it did different things is one
   * that eventually does the wrong one.
   */
  function choose(index: number): void {
    const customer = results[index];
    if (!customer) {
      return;
    }

    onSelect(customer.accountNumber, customer.name);
    setChosenName(customer.name);
    setIsOpen(false);
    setText("");
    setHighlighted(0);
  }

  /**
   * Arrow keys move through the list, Enter takes the highlighted one.
   *
   * The part search has behaved this way from the start and this did not, which
   * made the customer step the one place a representative had to reach for the
   * mouse -- in the middle of a call, holding a telephone. A journey is only
   * keyboard-operable if every step of it is.
   */
  function onKeyDown(event: React.KeyboardEvent<HTMLInputElement>): void {
    if (event.key === "ArrowDown") {
      event.preventDefault();
      setHighlighted((current) => Math.min(current + 1, results.length - 1));
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      setHighlighted((current) => Math.max(current - 1, 0));
    } else if (event.key === "Enter") {
      event.preventDefault();
      choose(highlighted);
    } else if (event.key === "Escape") {
      setText("");
      setIsOpen(false);
      setHighlighted(0);
    }
  }

  if (selectedAccount && chosenName) {
    return (
      <div className="search search--customer" style={{ maxWidth: "18rem" }} ref={containerRef}>
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
    <div className="search search--customer" style={{ maxWidth: "18rem" }} ref={containerRef}>
      <input
        className="search__input"
        type="search"
        aria-label="Select the customer being served"
        placeholder="Customer (for their price)…"
        value={text}
        role="combobox"
        aria-expanded={isOpen && results.length > 0}
        aria-autocomplete="list"
        onChange={(event) => {
          setText(event.target.value);
          setHighlighted(0);
          setIsOpen(true);
        }}
        onFocus={() => setIsOpen(true)}
        onKeyDown={onKeyDown}
      />

      {isOpen && results.length > 0 && (
        <ul className="search__results" role="listbox" aria-label="Matching customers">
          {results.map((customer, index) => (
            <li
              key={customer.accountNumber}
              className="search__result"
              role="option"
              aria-selected={index === highlighted}
              style={{ gridTemplateColumns: "7rem 1fr auto" }}
              onMouseEnter={() => setHighlighted(index)}
              onClick={() => choose(index)}
            >
              <span className="search__result-number">{customer.accountNumber}</span>
              <span className="search__result-description">
                {customer.name}
                {customer.addressLine && (
                  <span className="branch__code"> · {customer.addressLine}</span>
                )}
              </span>
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
