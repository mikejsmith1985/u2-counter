/**
 * Finding a part, from one input, without touching a mouse.
 *
 * The representative is on the phone. They type what the customer said, watch
 * the list narrow, and press Enter. Results are announced to assistive
 * technology as they change, because a screen-reader user otherwise has no way
 * to know the list moved beneath them.
 */

import { useEffect, useId, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, ApiFailure } from "../../api/client";
import type { SearchResult } from "../../api/types";

/** Wait after the last keystroke before asking the API. */
const SETTLE_MS = 180;

/** Below this, a search matches too much to be useful. */
const MINIMUM_QUERY_LENGTH = 2;

const RESULT_LIMIT = 12;

interface Props {
  /** Called when the representative chooses a part. */
  onSelect: (partNumber: string) => void;
}

export function PartSearch({ onSelect }: Props): React.JSX.Element {
  const [text, setText] = useState("");
  const [settled, setSettled] = useState("");
  const [highlighted, setHighlighted] = useState(0);
  const [isOpen, setIsOpen] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const listId = useId();

  useEffect(() => {
    const timer = setTimeout(() => setSettled(text.trim()), SETTLE_MS);
    return () => clearTimeout(timer);
  }, [text]);

  // The search box takes focus on load and on `/`, so the first keystroke of a
  // call always lands somewhere useful.
  useEffect(() => {
    inputRef.current?.focus();

    function onSlash(event: KeyboardEvent): void {
      const target = event.target as HTMLElement | null;
      const isTyping = target?.tagName === "INPUT" || target?.tagName === "TEXTAREA";

      if (event.key === "/" && !isTyping) {
        event.preventDefault();
        inputRef.current?.focus();
      }
    }

    window.addEventListener("keydown", onSlash);
    return () => window.removeEventListener("keydown", onSlash);
  }, []);

  const isSearchable = settled.length >= MINIMUM_QUERY_LENGTH;

  const { data, error, isFetching } = useQuery({
    queryKey: ["parts", settled],
    queryFn: ({ signal }) => api.searchParts(settled, RESULT_LIMIT, signal),
    enabled: isSearchable,
    staleTime: 30_000,
  });

  const results: SearchResult[] = data?.results ?? [];
  const failure = error instanceof ApiFailure ? error : null;

  /**
   * Select a part and let go of the search box.
   *
   * The blur is load-bearing. The shortcuts printed in the header are ignored
   * while a text field has focus -- correctly, or typing an R into a part number
   * would open a drawer -- so leaving focus in the box after a selection makes
   * every one of them do nothing. The person has just been shown a hint that
   * says "R record", presses R, and the interface ignores them.
   *
   * Focus moves to the answer instead, which is also what a screen-reader user
   * needs: without it, choosing a result announces nothing and the reader is
   * left in a search box whose list has silently vanished.
   */
  function choose(index: number): void {
    const chosen = results[index];
    if (chosen) {
      onSelect(chosen.partNumber);
      setIsOpen(false);
      setText("");
      inputRef.current?.blur();
    }
  }

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
    }
  }

  return (
    <div className="search">
      <input
        ref={inputRef}
        className="search__input"
        type="search"
        role="combobox"
        aria-expanded={isOpen && isSearchable}
        aria-controls={listId}
        aria-autocomplete="list"
        aria-label="Search for a part by number, description or manufacturer"
        placeholder="Part number, description or manufacturer…"
        value={text}
        onChange={(event) => {
          setText(event.target.value);
          setHighlighted(0);
          setIsOpen(true);
        }}
        onFocus={() => setIsOpen(true)}
        onKeyDown={onKeyDown}
      />

      {/* Announced rather than shown: sighted users can see the list change. */}
      <div className="visually-hidden" role="status" aria-live="polite">
        {isSearchable && !isFetching
          ? `${results.length} ${results.length === 1 ? "part" : "parts"} found`
          : ""}
      </div>

      {isOpen && isSearchable && (
        <ul className="search__results" id={listId} role="listbox" aria-label="Matching parts">
          {failure && (
            <li className="search__empty">
              {failure.kind === "unreachable"
                ? "The system could not reach the stock data. Press Enter to try again."
                : failure.message}
            </li>
          )}

          {!failure && results.length === 0 && !isFetching && (
            <li className="search__empty">
              Nothing matched “{settled}”. Try part of the description, or the manufacturer.
            </li>
          )}

          {results.map((result, index) => (
            <li
              key={result.partNumber}
              className="search__result"
              role="option"
              aria-selected={index === highlighted}
              onMouseEnter={() => setHighlighted(index)}
              onClick={() => choose(index)}
            >
              <span className="search__result-number">{result.partNumber}</span>
              <span className="search__result-description">
                {result.description}
                {result.isDiscontinued && " · discontinued"}
              </span>
              <span className="search__result-free figures">
                {result.totalFreeToSell} {result.unitOfMeasure}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
