/**
 * Whether the waking screen recovers on its own.
 *
 * The deployment scales to zero, so the first visitor after a quiet period waits
 * about a minute: measured against the live deployment, roughly 45 seconds for
 * the container to start and another 15 for the catalogue to be read. For that
 * whole minute the page is showing the waking state, and the only thing that
 * moves it on is this poll.
 *
 * So the poll surviving a bad answer is not a detail. During a cold start the
 * platform's edge can answer before the container does, and what it returns then
 * is not this application's JSON. If one such answer stops the polling, the page
 * sits on the waking message permanently and only a reload fixes it -- which
 * looks exactly like the broken search the waking message exists to prevent.
 */

import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { createElement } from "react";

import { useReadiness } from "../src/api/readiness";

/** A client that does not retry or cache between tests. */
function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  });
  return createElement(QueryClientProvider, { client }, children);
}

/** The body the application sends once it can answer. */
const ready = {
  isReady: true,
  catalogueCount: 1247,
  erpEndpoint: "http://counter-mcp/",
  requestBudgetSeconds: 5,
  detail: "Search is answering.",
};

/** The body it sends while the catalogue is still being read. */
const notReady = { ...ready, isReady: false, catalogueCount: 0 };

describe("the waking poll", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("reports ready once the application answers", async () => {
    vi.mocked(fetch).mockResolvedValue(
      new Response(JSON.stringify(ready), { status: 200 }),
    );

    const { result } = renderHook(() => useReadiness(), { wrapper });

    await waitFor(() => expect(result.current.isReady).toBe(true));
    expect(result.current.catalogueCount).toBe(1247);
  });

  it("keeps waiting while the catalogue is still being read", async () => {
    vi.mocked(fetch).mockResolvedValue(
      new Response(JSON.stringify(notReady), { status: 503 }),
    );

    const { result } = renderHook(() => useReadiness(), { wrapper });

    // A 503 here is the informative case, not a failure: it carries the same
    // body and says the catalogue is not read yet.
    await waitFor(() => expect(vi.mocked(fetch)).toHaveBeenCalled());
    expect(result.current.isReady).toBe(false);
  });

  it("asks again after the edge answers instead of the application", async () => {
    // The cold-start case. Azure's edge returns its own HTML page when the
    // container behind it has not started, so `response.json()` throws. If that
    // one answer ends the polling, the page never leaves the waking state.
    vi.mocked(fetch)
      .mockResolvedValueOnce(
        new Response("<html><body>502 Bad Gateway</body></html>", {
          status: 502,
          headers: { "Content-Type": "text/html" },
        }),
      )
      .mockResolvedValue(new Response(JSON.stringify(ready), { status: 200 }));

    const { result } = renderHook(() => useReadiness(), { wrapper });

    await waitFor(() => expect(result.current.isReady).toBe(true), {
      timeout: 5000,
    });
  });

  it("asks again after the connection itself fails", async () => {
    // The other cold-start shape: the container is not accepting yet and the
    // request fails outright rather than returning anything.
    vi.mocked(fetch)
      .mockRejectedValueOnce(new TypeError("Failed to fetch"))
      .mockResolvedValue(new Response(JSON.stringify(ready), { status: 200 }));

    const { result } = renderHook(() => useReadiness(), { wrapper });

    await waitFor(() => expect(result.current.isReady).toBe(true), {
      timeout: 5000,
    });
  });

  it("stops asking once it is ready", async () => {
    vi.mocked(fetch).mockResolvedValue(
      new Response(JSON.stringify(ready), { status: 200 }),
    );

    const { result } = renderHook(() => useReadiness(), { wrapper });
    await waitFor(() => expect(result.current.isReady).toBe(true));

    const asked = vi.mocked(fetch).mock.calls.length;
    await new Promise((resolve) => setTimeout(resolve, 2000));

    // Billed by the second. A health check that runs for the life of the page
    // is a service that never scales back down.
    expect(vi.mocked(fetch).mock.calls.length).toBe(asked);
  });
});
