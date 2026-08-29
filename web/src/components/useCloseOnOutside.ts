/**
 * Close a dropdown when attention moves somewhere else.
 *
 * Both pickers open a list and neither closed it when the other was clicked, so
 * two lists sat over each other with the one behind still catching the mouse.
 * Each had a container ref already; nothing was using it.
 *
 * Pointer and focus both, because they are different departures. A mouse user
 * clicks elsewhere on the page; a keyboard user tabs out and never generates a
 * click at all, and a list that stays open behind them is a list their next
 * Enter might select from.
 */

import { useEffect, type RefObject } from "react";

/**
 * Call `onClose` when a click or focus lands outside `container`.
 *
 * @param container The element the dropdown lives in.
 * @param isOpen Whether it is currently open. Nothing is listened for when not.
 * @param onClose What to do when attention leaves.
 */
export function useCloseOnOutside(
  container: RefObject<HTMLElement | null>,
  isOpen: boolean,
  onClose: () => void,
): void {
  useEffect(() => {
    if (!isOpen) {
      return;
    }

    function leftUs(target: EventTarget | null): boolean {
      return (
        target instanceof Node &&
        container.current !== null &&
        !container.current.contains(target)
      );
    }

    function onPointerDown(event: PointerEvent): void {
      if (leftUs(event.target)) {
        onClose();
      }
    }

    // Capturing, so the list closes before the click reaches whatever was
    // underneath it. Without that, clicking the second picker through the first
    // picker's open list closes one and opens the other in an order nobody can
    // predict.
    function onFocusIn(event: FocusEvent): void {
      if (leftUs(event.target)) {
        onClose();
      }
    }

    document.addEventListener("pointerdown", onPointerDown, true);
    document.addEventListener("focusin", onFocusIn, true);

    return () => {
      document.removeEventListener("pointerdown", onPointerDown, true);
      document.removeEventListener("focusin", onFocusIn, true);
    };
  }, [container, isOpen, onClose]);
}
