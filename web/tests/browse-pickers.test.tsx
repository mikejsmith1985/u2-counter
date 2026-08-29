/**
 * A picker shows what is there before anything is typed.
 *
 * Found by using the application rather than by testing it. Opened cold, there
 * was a search box for parts and a search box for customers, and no way to
 * answer the question anyone actually arrives with: what is in here?
 *
 * A counter representative learns their catalogue over months and types a part
 * number from memory. Nobody meeting the system for the first time can, and that
 * includes everyone it gets demonstrated to. A search box facing a stranger with
 * no list behind it is a locked door with no handle.
 */

import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { createElement } from "react";

import { CustomerSelector } from "../src/features/pricing/CustomerSelector";

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  });
  return createElement(QueryClientProvider, { client }, children);
}

/** Two accounts, enough to prove a list rendered. */
const accounts = [
  { accountNumber: "C-1001", name: "Ace Electric", addressLine: "12 Mill St", priceClass: "TIER1" },
  { accountNumber: "C-1002", name: "Борец Supply", addressLine: "9 Kiln Rd", priceClass: "TIER2" },
];

/** Answer whichever endpoint the component asks for. */
function respond(url: string) {
  if (url.includes("/customers/browse")) {
    return Promise.resolve(
      new Response(
        JSON.stringify({ results: accounts, totalCount: 150, envelope: { isComplete: true } }),
        { status: 200 },
      ),
    );
  }

  if (url.includes("/customers?q=")) {
    return Promise.resolve(
      new Response(
        JSON.stringify({ results: [accounts[0]], envelope: { isComplete: true } }),
        { status: 200 },
      ),
    );
  }

  return Promise.reject(new Error(`unexpected request: ${url}`));
}

describe("the customer picker", () => {
  beforeEach(() => {
    vi.stubGlobal(
      "fetch",
      vi.fn((input: RequestInfo | URL) => respond(String(input))),
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("lists customers as soon as it is opened, with nothing typed", async () => {
    render(<CustomerSelector selectedAccount={null} onSelect={() => {}} />, { wrapper });

    await userEvent.click(screen.getByRole("combobox"));

    await waitFor(() => expect(screen.getByText("Ace Electric")).toBeTruthy());
  });

  it("says how many accounts there are altogether", async () => {
    // Two rows with no total tells somebody the company has two customers.
    render(<CustomerSelector selectedAccount={null} onSelect={() => {}} />, { wrapper });

    await userEvent.click(screen.getByRole("combobox"));

    await waitFor(() => expect(screen.getByText(/of 150 accounts/i)).toBeTruthy());
  });

  it("narrows to matches once something is typed", async () => {
    render(<CustomerSelector selectedAccount={null} onSelect={() => {}} />, { wrapper });

    const box = screen.getByRole("combobox");
    await userEvent.click(box);
    await waitFor(() => expect(screen.getByText("Борец Supply")).toBeTruthy());

    await userEvent.type(box, "Ace");

    await waitFor(() => expect(screen.queryByText("Борец Supply")).toBeNull());
    await waitFor(() => expect(screen.getByText("Ace Electric")).toBeTruthy());
  });

  it("chooses the customer that was clicked", async () => {
    const chosen = vi.fn();
    render(<CustomerSelector selectedAccount={null} onSelect={chosen} />, { wrapper });

    await userEvent.click(screen.getByRole("combobox"));
    await waitFor(() => expect(screen.getByText("Ace Electric")).toBeTruthy());
    await userEvent.click(screen.getByText("Ace Electric"));

    expect(chosen).toHaveBeenCalledWith("C-1001", "Ace Electric");
  });

  it("can be driven from the keyboard alone", async () => {
    // A representative choosing a customer is holding a telephone. Browsing
    // must not be the one step that needs a mouse.
    const chosen = vi.fn();
    render(<CustomerSelector selectedAccount={null} onSelect={chosen} />, { wrapper });

    await userEvent.click(screen.getByRole("combobox"));
    await waitFor(() => expect(screen.getByText("Ace Electric")).toBeTruthy());

    await userEvent.keyboard("{ArrowDown}{Enter}");

    expect(chosen).toHaveBeenCalledWith("C-1002", "Борец Supply");
  });

  it("does not offer the count as something to select", async () => {
    // Arrowing onto "2 of 150 accounts" and pressing Enter must do nothing. A
    // count that looks like a row invites exactly that.
    render(<CustomerSelector selectedAccount={null} onSelect={() => {}} />, { wrapper });

    await userEvent.click(screen.getByRole("combobox"));
    await waitFor(() => expect(screen.getByText(/of 150 accounts/i)).toBeTruthy());

    expect(screen.getAllByRole("option")).toHaveLength(accounts.length);
  });
});
