/**
 * Whether the system can answer yet.
 *
 * This exists because of how the application is deployed. It scales to zero when
 * nobody is using it, so the first person to arrive after a quiet period wakes a
 * container: the page paints immediately, and search cannot answer for a few
 * seconds while the catalogue is read.
 *
 * Those seconds are the whole reason for this file. Left unhandled they look
 * exactly like a broken search — someone types, nothing matches, and they draw
 * the obvious conclusion. Saying "still loading, a few seconds" costs nothing and
 * is the difference between a system that is starting and a system that is
 * broken.
 */

import { useQuery } from "@tanstack/react-query";

/** How often to re-ask while waiting. */
const POLL_MS = 750;

/** What the health endpoint reports. */
export interface Readiness {
  /** Whether search can answer. */
  isReady: boolean;
  /** How many parts are searchable. */
  catalogueCount: number;
  /** Where this instance believes the ERP is. */
  erpEndpoint: string;
  /** How long it will wait for one answer. */
  requestBudgetSeconds: number;
  /** Why, in a sentence. */
  detail: string;
}

/**
 * Watch until the system is ready, then stop asking.
 *
 * @returns Whether search can answer, and how many parts it holds.
 *
 * @remarks
 * Polling stops the moment it succeeds. A health check that keeps running
 * forever is a request every second for the life of the page, which on a service
 * billed by the second is a service that never scales back down.
 *
 * The endpoint answers `503` until the catalogue is built, so an error here is
 * the expected state rather than a problem — which is why nothing is reported
 * from the failure except "not ready yet".
 */
export function useReadiness(): { isReady: boolean; catalogueCount: number } {
  const { data } = useQuery({
    queryKey: ["readiness"],
    queryFn: async (): Promise<Readiness> => {
      const response = await fetch("/health", { headers: { Accept: "application/json" } });

      // A 503 carries the same body as a 200 here, and it is the informative
      // case: it says the catalogue is still being read.
      return (await response.json()) as Readiness;
    },
    refetchInterval: (query) => (query.state.data?.isReady ? false : POLL_MS),
    retry: false,
    // Treated as fresh for as long as it says ready, so navigating within the
    // page does not re-ask a question already answered.
    staleTime: Infinity,
  });

  return {
    isReady: data?.isReady ?? false,
    catalogueCount: data?.catalogueCount ?? 0,
  };
}
