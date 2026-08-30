/**
 * The explore screen, reachable at any time rather than only before a search.
 *
 * It used to render only while no part was selected. Picking one made it
 * disappear, and nothing offered a way back — so the screen that lists the files,
 * reads their dictionaries and holds the only editor in the application was
 * unreachable for anybody who had done the first thing the page invites them to
 * do. Two statements elsewhere kept pointing at it: the tour describes it, and
 * the standing notice says this deployment has its editor switched on.
 *
 * A drawer rather than a section, because it is now opened deliberately from the
 * header and everything else opened that way is a drawer. The same Escape, the
 * same backdrop, the same place on the screen.
 */

import { useEffect, useRef } from "react";
import { SchemaExplorer } from "./SchemaExplorer";

interface Props {
  /** Close the drawer. */
  onClose: () => void;
}

export function ExploreDrawer({ onClose }: Props): React.JSX.Element {
  const panelRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    panelRef.current?.focus();

    function onKeyDown(event: KeyboardEvent): void {
      if (event.key === "Escape") {
        onClose();
      }
    }

    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [onClose]);

  return (
    <div
      className="drawer"
      onClick={(event) => {
        if (event.target === event.currentTarget) {
          onClose();
        }
      }}
    >
      <div
        ref={panelRef}
        className="drawer__panel"
        role="dialog"
        aria-modal="true"
        aria-labelledby="explore-title"
        tabIndex={-1}
      >
        <div className="drawer__header">
          <h2 id="explore-title" className="connect__title">
            The database, as it describes itself
          </h2>
          <span className="header__spacer" />
          <button type="button" className="button button--quiet" onClick={onClose}>
            Close (Esc)
          </button>
        </div>

        <SchemaExplorer />
      </div>
    </div>
  );
}
