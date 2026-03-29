import { useEffect, useRef, useCallback } from "react";
import {
  HubConnectionBuilder,
  HubConnection,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";
import { getAccessToken } from "../lib/api";

type EventHandler = (...args: unknown[]) => void;

/**
 * Manages a SignalR hub connection with automatic reconnection.
 * Returns a stable `on` function to subscribe to hub events.
 */
export function useSignalR(hubUrl: string) {
  const connectionRef = useRef<HubConnection | null>(null);
  const handlersRef = useRef<Map<string, Set<EventHandler>>>(new Map());

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => getAccessToken() ?? "",
      })
      .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
      .configureLogging(LogLevel.Warning)
      .build();

    connectionRef.current = connection;

    // Re-register all handlers on the new connection
    for (const [event, handlers] of handlersRef.current) {
      for (const handler of handlers) {
        connection.on(event, handler);
      }
    }

    connection.start().catch((err) => {
      console.warn("SignalR connection failed:", err);
    });

    return () => {
      connection.stop();
      connectionRef.current = null;
    };
  }, [hubUrl]);

  const on = useCallback((event: string, handler: EventHandler) => {
    if (!handlersRef.current.has(event)) {
      handlersRef.current.set(event, new Set());
    }
    handlersRef.current.get(event)!.add(handler);

    const conn = connectionRef.current;
    if (conn && conn.state === HubConnectionState.Connected) {
      conn.on(event, handler);
    }

    return () => {
      handlersRef.current.get(event)?.delete(handler);
      connectionRef.current?.off(event, handler);
    };
  }, []);

  return { on };
}
