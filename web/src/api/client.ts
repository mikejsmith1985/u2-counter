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
  AskResult,
  AskStatus,
  BrowseResponse,
  CommitmentsResponse,
  CustomerSearchResponse,
  CustomerSummary,
  PartSummary,
  RecordResponse,
  SearchResponse,
  SessionResponse,
  ActivityResponse,
  Persona,
} from "./types";

/*
 * Relative by default. The API serves the built front end in production and the
 * dev server proxies to it, so the browser always sees one origin -- which keeps
 * cookies straightforward and means no cross-origin configuration to get wrong.
 */
const API_BASE = import.meta.env.VITE_API_BASE ?? "/api/v1";

/** What went wrong, in a form the interface can switch on. */
export type FailureKind =
  /** A record could not be read without guessing at what it meant. */
  | "malformed-record"
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

  /**
   * Whether trying again could plausibly succeed.
   *
   * Read by the failure states to decide whether to offer a retry. A malformed
   * request and a malformed record both produce the same answer however many
   * times they are asked, and a button that changes nothing is worse than no
   * button — it makes the person press it twice before they believe you.
   */
  get isWorthRetrying(): boolean {
    return this.kind === "unreachable" || this.kind === "refused" || this.kind === "unknown";
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

  // Before the status, because a malformed record also arrives as 502 and
  // "the system declined that request" is the wrong thing to tell somebody
  // about a record the system could not make sense of.
  if (problem.type === "erp-malformed-record") return "malformed-record";

  if (problem.type === "erp-refused" || status === 502) return "refused";
  if (problem.type === "not-found" || status === 404) return "not-found";
  if (status === 400) return "invalid";
  return "unknown";
}

async function request<T>(
  path: string,
  signal?: AbortSignal,
  // Extra request options, for the one call that is not a GET. Routed through
  // here rather than given its own fetch so that a failed question reports
  // itself exactly like every other failed call -- one place decides what an
  // unreachable API looks like.
  init?: RequestInit,
): Promise<T> {
  let response: Response;

  try {
    response = await fetch(`${API_BASE}${path}`, {
      credentials: "include",
      ...init,
      headers: { Accept: "application/json", ...(init?.headers ?? {}) },
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

  /** Whether an assistant is configured, and which model answers. */
  askStatus: (signal?: AbortSignal) => request<AskStatus>("/ask/status", signal),

  /** Ask a question in words, and get the answer with every call it took. */
  ask: (question: string, signal?: AbortSignal) =>
    request<AskResult>("/ask", signal, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ question }),
    }),

  /** List the catalogue, for somebody who has not got a part number yet. */
  browseParts: (limit: number, signal?: AbortSignal) =>
    request<BrowseResponse<PartSummary>>(`/parts/browse?limit=${limit}`, signal),

  /** List the account file, for somebody who has not been told who they serve. */
  browseCustomers: (limit: number, signal?: AbortSignal) =>
    request<BrowseResponse<CustomerSummary>>(`/customers/browse?limit=${limit}`, signal),

  /** Find customers for the selector. */
  searchCustomers: (text: string, signal?: AbortSignal) =>
    request<CustomerSearchResponse>(`/customers?q=${encodeURIComponent(text)}`, signal),

  /** Who is signed in, and what they may do. */
  session: (signal?: AbortSignal) => request<SessionResponse>("/session", signal),

  /** Recent activity for the current user. */
  activity: (limit: number, signal?: AbortSignal) =>
    request<ActivityResponse>(`/activity?limit=${limit}`, signal),

  /** The demonstration identities on offer. */
  personas: (signal?: AbortSignal) => request<Persona[]>("/session/personas", signal),

  /**
   * Become one of the demonstration personas.
   *
   * Not a login. No credential is sent, because there is none: this changes
   * whose name the activity record carries and which branch leads the grid.
   */
  signIn: async (subject: string): Promise<SessionResponse> => {
    const response = await fetch(`${API_BASE}/session`, {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify({ subject }),
    });

    if (!response.ok) {
      throw new ApiFailure("unknown", response.status, "That identity could not be selected.");
    }

    return (await response.json()) as SessionResponse;
  },

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
