/**
 * Giving focus back when a panel closes.
 */

import { useEffect } from "react";

/**
 * Return focus to whatever opened this panel, once it closes.
 *
 * A modal that closes leaving nothing focused strands a keyboard user at the
 * top of the document, with the whole page to tab through again to get back to
 * where they were. On a call that is the difference between a shortcut being
 * useful and being avoided.
 *
 * The element is captured on mount rather than passed in, because the thing that
 * opened the panel is simply whatever had focus at that moment -- a button, a
 * grid cell, or nothing at all if a keyboard shortcut did it.
 */
export function useReturnFocus(): void {
  useEffect(() => {
    const opener = document.activeElement as HTMLElement | null;

    return () => {
      // Only if it is still on the page. An opener that has been removed cannot
      // take focus, and asking it to would throw.
      if (opener && document.contains(opener)) {
        opener.focus();
      }
    };
  }, []);
}
