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
 * SignalR logger that demotes the expected, self-healing negotiation noise to debug level, while
 * forwarding all genuine warnings and errors to the console so real hub failures still surface.
 *
 * Expected/transient cases:
 *  - "stopped during negotiation" / "Failed to start the connection" — intentional navigation or
 *    React Strict Mode double-mount aborts the first connection.
 *  - "Failed to complete negotiation ... 401" — the negotiate request raced the in-flight
 *    /auth/refresh (token not set yet for those ~ms). withAutomaticReconnect retries with a valid
 *    token, so this is noise, not a real failure.
 */
const NEGOTIATION_ABORT = /stopped during negotiation|Failed to (start the connection|complete negotiation)/i;

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
 *
 * Pass `enabled: false` to defer connecting until prerequisites are met (e.g. a valid access
 * token exists). Connecting without a token makes the negotiate request fail with a 401, which
 * surfaces as console noise on unauthenticated or not-yet-bootstrapped contexts.
 */
export function useSignalR(hubUrl: string, enabled = true) {
  const connectionRef = useRef<HubConnection | null>(null);
  const handlersRef = useRef<Map<string, Set<EventHandler>>>(new Map());

  useEffect(() => {
    if (!enabled) return;

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
  }, [hubUrl, enabled]);

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
