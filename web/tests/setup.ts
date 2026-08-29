/**
 * What every front-end unit test needs before it runs.
 *
 * jsdom implements the document but not the browser around it, so anything a
 * component asks the browser for has to be supplied here. Each of these is
 * provided because a component legitimately uses it, not to silence an error:
 * a stub added to make a failure go away hides the behaviour it stood for.
 */

import { afterEach, vi } from "vitest";
import { cleanup } from "@testing-library/react";

// React Testing Library leaves its container in the document. Two tests sharing
// one would find each other's elements, and the second would pass for the wrong
// reason.
afterEach(cleanup);

// The clipboard is behind a permission prompt no test can answer, so writes are
// recorded rather than performed. What was copied is the thing being asserted.
Object.defineProperty(navigator, "clipboard", {
  configurable: true,
  value: { writeText: vi.fn(() => Promise.resolve()) },
});

// jsdom has no layout, so nothing can be scrolled into view. Components call
// this to keep a highlighted result visible while arrowing through a list.
Element.prototype.scrollIntoView = vi.fn();

// Media queries are how the interface reads the viewer's theme preference.
// Unset here, which is the default state: no explicit choice either way.
Object.defineProperty(window, "matchMedia", {
  configurable: true,
  value: (query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  }),
});
