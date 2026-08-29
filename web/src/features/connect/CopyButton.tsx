/**
 * A button that puts something on the clipboard and says it did.
 *
 * Every code block on the connect panel is there to be pasted somewhere else, so
 * selecting text by hand is the one interaction the panel exists to avoid.
 *
 * The confirmation matters as much as the copying. A button that does its job
 * silently is one people press twice, then a third time, wondering whether it
 * worked — and on a page whose whole point is "this is easy", that is the wrong
 * first impression.
 */

import { useEffect, useState } from "react";

/** How long the confirmation stays before the button offers itself again. */
const CONFIRMED_MS = 1600;

interface Props {
  /** What to put on the clipboard. */
  text: string;
  /** What the button says when idle. */
  label?: string;
}

export function CopyButton({ text, label = "Copy" }: Props): React.JSX.Element {
  const [state, setState] = useState<"idle" | "copied" | "failed">("idle");

  useEffect(() => {
    if (state === "idle") {
      return;
    }

    const settling = setTimeout(() => setState("idle"), CONFIRMED_MS);
    return () => clearTimeout(settling);
  }, [state]);

  async function copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(text);
      setState("copied");
    } catch {
      // Clipboard access is refused in some browsers and over plain HTTP. Saying
      // so is better than a button that appears to have worked: the person is
      // about to paste, and would paste whatever was there before.
      setState("failed");
    }
  }

  return (
    <button
      type="button"
      className="copy"
      onClick={copy}
      aria-label={state === "copied" ? "Copied" : label}
    >
      {state === "copied" ? "Copied" : state === "failed" ? "Copy failed" : label}
    </button>
  );
}
