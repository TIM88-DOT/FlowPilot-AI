import { useEffect, useRef, useCallback } from "react";
import {
  HubConnectionBuilder,
  HubConnection,
  LogLevel,
  type ILogger,
} from "@microsoft/signalr";
import { getAccessToken } from "../lib/api";

type EventHandler = (...args: unknown[]) => void;

/**
 * SignalR logger that demotes the expected "aborted during negotiation" noise
 * (fired on intentional navigation / React Strict Mode double-mount) to debug
 * level, while forwarding all genuine warnings and errors to the console so
 * real hub failures still surface.
 */
const NEGOTIATION_ABORT = /stopped during negotiation|Failed to start the connection/i;

const signalRLogger: ILogger = {
  log(logLevel, message) {
    if (logLevel < LogLevel.Warning) return;

    if (NEGOTIATION_ABORT.test(message)) {
      console.debug("[SignalR] connection aborted (navigation/strict-mode):", message);
      return;
    }

    if (logLevel >= LogLevel.Error) {
      console.error("[SignalR]", message);
    } else {
      console.warn("[SignalR]", message);
    }
  },
};

/**
 * Manages a SignalR hub connection with automatic reconnection.
 * Returns a stable `on` function to subscribe to hub events.
 */
export function useSignalR(hubUrl: string) {
  const connectionRef = useRef<HubConnection | null>(null);
  const handlersRef = useRef<Map<string, Set<EventHandler>>>(new Map());

  useEffect(() => {
    let stopped = false;

    const connection = new HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => getAccessToken() ?? "",
      })
      .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
      .configureLogging(signalRLogger)
      .build();

    connectionRef.current = connection;

    // Re-register all handlers on the new connection
    for (const [event, handlers] of handlersRef.current) {
      for (const handler of handlers) {
        connection.on(event, handler);
      }
    }

    connection.start().catch(() => {
      // Silenced — React Strict Mode double-mount aborts the first connection.
      // The second mount will connect successfully.
    });

    connection.onclose((err) => {
      if (!stopped && err) {
        console.warn("SignalR connection closed unexpectedly:", err);
      }
    });

    return () => {
      stopped = true;
      connection.stop();
      connectionRef.current = null;
    };
  }, [hubUrl]);

  const on = useCallback((event: string, handler: EventHandler) => {
    if (!handlersRef.current.has(event)) {
      handlersRef.current.set(event, new Set());
    }
    handlersRef.current.get(event)!.add(handler);

    // Register immediately — SignalR supports .on() before connection is started
    connectionRef.current?.on(event, handler);

    return () => {
      handlersRef.current.get(event)?.delete(handler);
      connectionRef.current?.off(event, handler);
    };
  }, []);

  return { on };
}
