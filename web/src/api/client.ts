/**
 * Talking to the API, and telling its failures apart.
 *
 * The distinction this file exists to preserve: an empty result and an
 * unreachable system look identical if both arrive as "nothing came back". They
 * are not the same answer, and a representative would tell a customer something
 * untrue on the strength of the confusion.
 */

import type {
  AvailabilityResponse,
  CommitmentsResponse,
  CustomerSearchResponse,
  RecordResponse,
  SearchResponse,
  SessionResponse,
  ActivityResponse,
} from "./types";

/*
 * Relative by default. The API serves the built front end in production and the
 * dev server proxies to it, so the browser always sees one origin -- which keeps
 * cookies straightforward and means no cross-origin configuration to get wrong.
 */
const API_BASE = import.meta.env.VITE_API_BASE ?? "/api/v1";

/** What went wrong, in a form the interface can switch on. */
export type FailureKind =
  /** The system could not reach the stock data. Never "no stock". */
  | "unreachable"
  /** The ERP declined the request. */
  | "refused"
  /** The key does not exist. */
  | "not-found"
  /** The request was malformed. */
  | "invalid"
  /** Anything else, including the network being down. */
  | "unknown";

/**
 * A failure carrying enough for the interface to render the right state.
 *
 * `kind` exists so no screen has to guess from an empty array whether it is
 * looking at "nothing matched" or "we could not ask".
 */
export class ApiFailure extends Error {
  readonly kind: FailureKind;
  readonly status: number;

  constructor(kind: FailureKind, status: number, message: string) {
    super(message);
    this.name = "ApiFailure";
    this.kind = kind;
    this.status = status;
  }

  /** Whether trying again could plausibly succeed. */
  get isWorthRetrying(): boolean {
    return this.kind === "unreachable" || this.kind === "unknown";
  }
}

interface ProblemDetails {
  type?: string;
  title?: string;
  detail?: string;
  status?: number;
}

/** Map a problem response onto the kind the interface switches on. */
function kindFrom(status: number, problem: ProblemDetails): FailureKind {
  if (problem.type === "erp-unreachable" || status === 504) return "unreachable";
  if (problem.type === "erp-refused" || status === 502) return "refused";
  if (problem.type === "not-found" || status === 404) return "not-found";
  if (status === 400) return "invalid";
  return "unknown";
}

async function request<T>(path: string, signal?: AbortSignal): Promise<T> {
  let response: Response;

  try {
    response = await fetch(`${API_BASE}${path}`, {
      credentials: "include",
      headers: { Accept: "application/json" },
      signal,
    });
  } catch (error) {
    // The network itself failed. Reported as unreachable, because from the
    // user's side it is the same situation and the same advice applies.
    throw new ApiFailure(
      "unreachable",
      0,
      error instanceof Error && error.name === "AbortError"
        ? "The request was cancelled."
        : "The system could not be reached.",
    );
  }

  if (response.ok) {
    return (await response.json()) as T;
  }

  let problem: ProblemDetails = {};
  try {
    problem = (await response.json()) as ProblemDetails;
  } catch {
    // A failure with no readable body is still a failure; the status carries
    // enough to choose a message.
  }

  throw new ApiFailure(
    kindFrom(response.status, problem),
    response.status,
    problem.detail ?? problem.title ?? "The request could not be completed.",
  );
}

export const api = {
  /** Find parts by number, description or manufacturer. */
  searchParts: (text: string, limit: number, signal?: AbortSignal) =>
    request<SearchResponse>(
      `/parts?q=${encodeURIComponent(text)}&limit=${limit}`,
      signal,
    ),

  /** Read a part's availability across every branch, with the customer's price. */
  availability: (partNumber: string, customerAccount: string | null, signal?: AbortSignal) =>
    request<AvailabilityResponse>(
      `/parts/${encodeURIComponent(partNumber)}/availability` +
        (customerAccount ? `?customerAccount=${encodeURIComponent(customerAccount)}` : ""),
      signal,
    ),

  /** Read what is holding a branch's committed stock. */
  commitments: (partNumber: string, branchCode: string, signal?: AbortSignal) =>
    request<CommitmentsResponse>(
      `/parts/${encodeURIComponent(partNumber)}/commitments?branchCode=${encodeURIComponent(branchCode)}`,
      signal,
    ),

  /** Read the stored record beside its parsed form. */
  record: (partNumber: string, signal?: AbortSignal) =>
    request<RecordResponse>(`/parts/${encodeURIComponent(partNumber)}/record`, signal),

  /** Find customers for the selector. */
  searchCustomers: (text: string, signal?: AbortSignal) =>
    request<CustomerSearchResponse>(`/customers?q=${encodeURIComponent(text)}`, signal),

  /** Who is signed in, and what they may do. */
  session: (signal?: AbortSignal) => request<SessionResponse>("/session", signal),

  /** Recent activity for the current user. */
  activity: (limit: number, signal?: AbortSignal) =>
    request<ActivityResponse>(`/activity?limit=${limit}`, signal),

  /** Choose the customer being served. */
  selectCustomer: async (customerAccount: string | null): Promise<SessionResponse> => {
    const response = await fetch(`${API_BASE}/session/customer`, {
      method: "PUT",
      credentials: "include",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify({ customerAccount }),
    });

    if (!response.ok) {
      throw new ApiFailure("unknown", response.status, "The customer could not be selected.");
    }

    return (await response.json()) as SessionResponse;
  },
};
