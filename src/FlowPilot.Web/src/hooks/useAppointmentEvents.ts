import { useEffect } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useSignalR } from "./useSignalR";

/**
 * Subscribes to real-time appointment events via SignalR and invalidates
 * the relevant React Query caches so the UI stays up to date.
 */
export function useAppointmentEvents() {
  const queryClient = useQueryClient();
  const { on } = useSignalR("/hubs/appointments");

  useEffect(() => {
    const unsubStatus = on("AppointmentStatusChanged", () => {
      queryClient.invalidateQueries({ queryKey: ["appointments"] });
      queryClient.invalidateQueries({ queryKey: ["dashboard-stats"] });
    });

    const unsubCreated = on("AppointmentCreated", () => {
      queryClient.invalidateQueries({ queryKey: ["appointments"] });
      queryClient.invalidateQueries({ queryKey: ["dashboard-stats"] });
    });

    return () => {
      unsubStatus();
      unsubCreated();
    };
  }, [on, queryClient]);
}
