import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { App } from "./app/App";
import "./styles/theme.css";
import "./styles/layout.css";

/**
 * Retries are deliberately limited.
 *
 * A representative on a call needs to be told quickly that the system cannot be
 * reached. Retrying three times behind the scenes turns a five-second budget
 * into fifteen seconds of silence, which is worse than the failure itself.
 */
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: false,
      refetchOnWindowFocus: false,
      staleTime: 0,
    },
  },
});

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <App />
    </QueryClientProvider>
  </StrictMode>,
);
