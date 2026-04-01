import { useEffect } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useSignalR } from "./useSignalR";

/**
 * Subscribes to real-time SMS events via SignalR and invalidates
 * the relevant React Query caches so the inbox UI stays up to date.
 */
export function useSmsEvents() {
  const queryClient = useQueryClient();
  const { on } = useSignalR("/hubs/sms");

  useEffect(() => {
    const unsubInbound = on("NewInboundSms", () => {
      queryClient.invalidateQueries({ queryKey: ["conversations"] });
      queryClient.invalidateQueries({ queryKey: ["messages"] });
    });

    return () => {
      unsubInbound();
    };
  }, [on, queryClient]);
}
