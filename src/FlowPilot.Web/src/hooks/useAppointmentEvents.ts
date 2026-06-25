import { useEffect, useSyncExternalStore } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useSignalR } from "./useSignalR";
import { getAccessToken, subscribeAccessToken } from "../lib/api";

/** Reactively tracks whether an in-memory access token is currently set. */
function useHasAccessToken(): boolean {
  return useSyncExternalStore(
    subscribeAccessToken,
    () => getAccessToken() !== null,
  );
}

/**
 * Subscribes to real-time appointment events via SignalR and invalidates
 * the relevant React Query caches so the UI stays up to date.
 */
export function useAppointmentEvents() {
  const queryClient = useQueryClient();
  // Only open the hub once a valid token exists — avoids a failed negotiate (401) during the
  // brief window after an optimistic render but before /auth/refresh has set the token.
  const hasToken = useHasAccessToken();
  const { on } = useSignalR("/hubs/appointments", hasToken);

  useEffect(() => {
    const unsubStatus = on("AppointmentStatusChanged", () => {
      queryClient.invalidateQueries({ queryKey: ["appointments"] });
      queryClient.invalidateQueries({ queryKey: ["dashboard-stats"] });
    });

    const unsubCreated = on("AppointmentCreated", () => {
      queryClient.invalidateQueries({ queryKey: ["appointments"] });
      queryClient.invalidateQueries({ queryKey: ["dashboard-stats"] });
    });

    const unsubAtRisk = on("AppointmentAtRisk", () => {
      queryClient.invalidateQueries({ queryKey: ["appointments"] });
      queryClient.invalidateQueries({ queryKey: ["dashboard-stats"] });
    });

    const unsubMissed = on("AppointmentMissed", () => {
      queryClient.invalidateQueries({ queryKey: ["appointments"] });
      queryClient.invalidateQueries({ queryKey: ["dashboard-stats"] });
    });

    return () => {
      unsubStatus();
      unsubCreated();
      unsubAtRisk();
      unsubMissed();
    };
  }, [on, queryClient]);
}
