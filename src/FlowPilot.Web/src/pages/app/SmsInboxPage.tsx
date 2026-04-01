import { useState, useRef, useEffect } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  Search,
  Send,
  ArrowLeft,
  MessageSquare,
  ChevronLeft,
  ChevronRight,
} from "lucide-react";
import { toast } from "sonner";
import api from "../../lib/api";
import { useSmsEvents } from "../../hooks/useSmsEvents";
import { useDebouncedValue } from "../../hooks/useDebouncedValue";

/* ------------------------------------------------------------------ */
/*  Types                                                              */
/* ------------------------------------------------------------------ */

interface ConversationSummary {
  customerId: string;
  customerFirstName: string;
  customerLastName: string | null;
  customerPhone: string;
  lastMessageBody: string;
  lastMessageAt: string;
  lastMessageDirection: string;
  totalMessages: number;
}

interface MessageItem {
  id: string;
  body: string;
  direction: string;
  status: string;
  createdAt: string;
  fromPhone: string | null;
  toPhone: string | null;
}

interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

/* ------------------------------------------------------------------ */
/*  Helpers                                                            */
/* ------------------------------------------------------------------ */

const inputCls =
  "w-full rounded-xl border border-border bg-warm-white px-3 py-2 text-[13px] text-ink placeholder:text-ink-faint focus:outline-none focus:ring-2 focus:ring-teal/30 focus:border-teal transition";

function formatTime(iso: string) {
  const d = new Date(iso);
  const now = new Date();
  const isToday = d.toDateString() === now.toDateString();

  if (isToday) return d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });

  const yesterday = new Date(now);
  yesterday.setDate(yesterday.getDate() - 1);
  if (d.toDateString() === yesterday.toDateString()) return "Yesterday";

  return d.toLocaleDateString([], { month: "short", day: "numeric" });
}

function formatFullTime(iso: string) {
  const d = new Date(iso);
  return d.toLocaleString([], {
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

/* ------------------------------------------------------------------ */
/*  Component                                                          */
/* ------------------------------------------------------------------ */

export default function SmsInboxPage() {
  const queryClient = useQueryClient();
  useSmsEvents();

  const [searchInput, setSearchInput] = useState("");
  const debouncedSearch = useDebouncedValue(searchInput, 300);
  const [page, setPage] = useState(1);
  const [selectedCustomerId, setSelectedCustomerId] = useState<string | null>(null);
  const [msgPage, setMsgPage] = useState(1);
  const [draft, setDraft] = useState("");
  const messagesEndRef = useRef<HTMLDivElement>(null);

  // Reset page when search changes
  useEffect(() => { setPage(1); }, [debouncedSearch]);

  /* ---- Queries ---- */

  const { data: conversations, isLoading: loadingConversations } = useQuery<PagedResult<ConversationSummary>>({
    queryKey: ["conversations", debouncedSearch, page],
    queryFn: async () => {
      const params = new URLSearchParams();
      if (debouncedSearch) params.set("search", debouncedSearch);
      params.set("page", String(page));
      params.set("pageSize", "25");
      const { data } = await api.get(`/messaging/conversations?${params}`);
      return data;
    },
  });

  const { data: messages, isLoading: loadingMessages } = useQuery<PagedResult<MessageItem>>({
    queryKey: ["messages", selectedCustomerId, msgPage],
    queryFn: async () => {
      const { data } = await api.get(
        `/messaging/conversations/${selectedCustomerId}/messages?page=${msgPage}&pageSize=50`
      );
      return data;
    },
    enabled: !!selectedCustomerId,
  });

  // Auto-scroll to bottom when messages load
  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  /* ---- Send mutation ---- */

  const sendMutation = useMutation({
    mutationFn: async () => {
      const { data } = await api.post(
        `/messaging/conversations/${selectedCustomerId}/send`,
        { body: draft }
      );
      return data;
    },
    onSuccess: () => {
      setDraft("");
      queryClient.invalidateQueries({ queryKey: ["messages", selectedCustomerId] });
      queryClient.invalidateQueries({ queryKey: ["conversations"] });
    },
    onError: () => {
      toast.error("Failed to send message.");
    },
  });

  /* ---- Derived ---- */

  const selectedConversation = conversations?.items.find(
    (c) => c.customerId === selectedCustomerId
  );

  /* ---- Render ---- */

  // Mobile: show thread if a conversation is selected
  const showThread = !!selectedCustomerId;

  return (
    <div className="h-[calc(100vh-120px)] flex flex-col">
      {/* Header */}
      <div className="flex items-center justify-between mb-4">
        <div>
          <h1 className="text-[22px] font-semibold text-ink">SMS Inbox</h1>
          <p className="text-[13px] text-ink-muted mt-0.5">
            Customer conversations &amp; manual messaging
          </p>
        </div>
      </div>

      {/* Main split panel */}
      <div className="flex-1 flex border border-border rounded-2xl bg-warm-white overflow-hidden min-h-0">
        {/* ---- Left: Conversation list ---- */}
        <div
          className={`w-full lg:w-[340px] lg:border-r lg:border-border flex flex-col shrink-0 ${
            showThread ? "hidden lg:flex" : "flex"
          }`}
        >
          {/* Search */}
          <div className="p-3 border-b border-border">
            <div className="relative">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-ink-faint" />
              <input
                type="text"
                placeholder="Search by name or phone..."
                value={searchInput}
                onChange={(e) => setSearchInput(e.target.value)}
                className={`${inputCls} pl-9`}
              />
            </div>
          </div>

          {/* List */}
          <div className="flex-1 overflow-y-auto">
            {loadingConversations ? (
              <div className="p-6 text-center text-[13px] text-ink-faint">Loading...</div>
            ) : !conversations?.items.length ? (
              <div className="p-6 text-center">
                <MessageSquare className="w-10 h-10 text-ink-faint/40 mx-auto mb-2" />
                <p className="text-[13px] text-ink-faint">No conversations yet</p>
              </div>
            ) : (
              conversations.items.map((conv) => (
                <button
                  key={conv.customerId}
                  onClick={() => {
                    setSelectedCustomerId(conv.customerId);
                    setMsgPage(1);
                  }}
                  className={`w-full text-left px-4 py-3 border-b border-border/50 hover:bg-cream-dark/40 transition-colors ${
                    selectedCustomerId === conv.customerId ? "bg-teal-wash" : ""
                  }`}
                >
                  <div className="flex items-center justify-between mb-0.5">
                    <span className="text-[13px] font-medium text-ink truncate">
                      {conv.customerFirstName}
                      {conv.customerLastName ? ` ${conv.customerLastName}` : ""}
                    </span>
                    <span className="text-[11px] text-ink-faint shrink-0 ml-2">
                      {formatTime(conv.lastMessageAt)}
                    </span>
                  </div>
                  <div className="flex items-center gap-1.5">
                    {conv.lastMessageDirection === "Outbound" && (
                      <span className="text-[11px] text-ink-faint">You:</span>
                    )}
                    <p className="text-[12px] text-ink-muted truncate">
                      {conv.lastMessageBody}
                    </p>
                  </div>
                  <p className="text-[11px] text-ink-faint mt-0.5">{conv.customerPhone}</p>
                </button>
              ))
            )}
          </div>

          {/* Pagination */}
          {conversations && conversations.totalPages > 1 && (
            <div className="flex items-center justify-between px-4 py-2 border-t border-border text-[12px] text-ink-muted">
              <button
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                disabled={page <= 1}
                className="p-1 disabled:opacity-30"
              >
                <ChevronLeft className="w-4 h-4" />
              </button>
              <span>
                {page} / {conversations.totalPages}
              </span>
              <button
                onClick={() => setPage((p) => Math.min(conversations.totalPages, p + 1))}
                disabled={page >= conversations.totalPages}
                className="p-1 disabled:opacity-30"
              >
                <ChevronRight className="w-4 h-4" />
              </button>
            </div>
          )}
        </div>

        {/* ---- Right: Message thread ---- */}
        <div
          className={`flex-1 flex flex-col min-w-0 ${
            showThread ? "flex" : "hidden lg:flex"
          }`}
        >
          {!selectedCustomerId ? (
            /* Empty state */
            <div className="flex-1 flex items-center justify-center">
              <div className="text-center">
                <MessageSquare className="w-12 h-12 text-ink-faint/30 mx-auto mb-3" />
                <p className="text-[14px] text-ink-muted">
                  Select a conversation to view messages
                </p>
              </div>
            </div>
          ) : (
            <>
              {/* Thread header */}
              <div className="flex items-center gap-3 px-4 py-3 border-b border-border shrink-0">
                <button
                  className="lg:hidden p-1 text-ink-muted hover:text-ink"
                  onClick={() => setSelectedCustomerId(null)}
                >
                  <ArrowLeft className="w-5 h-5" />
                </button>
                <div className="min-w-0">
                  <p className="text-[14px] font-medium text-ink truncate">
                    {selectedConversation?.customerFirstName}
                    {selectedConversation?.customerLastName
                      ? ` ${selectedConversation.customerLastName}`
                      : ""}
                  </p>
                  <p className="text-[12px] text-ink-faint">
                    {selectedConversation?.customerPhone}
                  </p>
                </div>
              </div>

              {/* Messages */}
              <div className="flex-1 overflow-y-auto px-4 py-4 space-y-3">
                {loadingMessages ? (
                  <div className="text-center text-[13px] text-ink-faint py-8">
                    Loading messages...
                  </div>
                ) : !messages?.items.length ? (
                  <div className="text-center text-[13px] text-ink-faint py-8">
                    No messages yet
                  </div>
                ) : (
                  <>
                    {/* Older messages pagination */}
                    {messages.totalPages > 1 && msgPage < messages.totalPages && (
                      <button
                        onClick={() => setMsgPage((p) => p + 1)}
                        className="mx-auto block text-[12px] text-teal hover:underline mb-2"
                      >
                        Load older messages
                      </button>
                    )}

                    {/* Messages rendered in reverse (newest at bottom) */}
                    {[...messages.items].reverse().map((msg) => {
                      const isOutbound = msg.direction === "Outbound";
                      return (
                        <div
                          key={msg.id}
                          className={`flex ${isOutbound ? "justify-end" : "justify-start"}`}
                        >
                          <div
                            className={`max-w-[75%] rounded-2xl px-3.5 py-2 ${
                              isOutbound
                                ? "bg-teal text-white rounded-br-md"
                                : "bg-cream-dark text-ink rounded-bl-md"
                            }`}
                          >
                            <p className="text-[13px] whitespace-pre-wrap break-words">
                              {msg.body}
                            </p>
                            <div
                              className={`flex items-center gap-1.5 mt-1 ${
                                isOutbound ? "justify-end" : "justify-start"
                              }`}
                            >
                              <span
                                className={`text-[10px] ${
                                  isOutbound ? "text-white/60" : "text-ink-faint"
                                }`}
                              >
                                {formatFullTime(msg.createdAt)}
                              </span>
                              {isOutbound && (
                                <span
                                  className={`text-[10px] ${
                                    msg.status === "Failed"
                                      ? "text-red-300"
                                      : "text-white/60"
                                  }`}
                                >
                                  {msg.status === "Delivered"
                                    ? "✓✓"
                                    : msg.status === "Sent"
                                    ? "✓"
                                    : msg.status === "Failed"
                                    ? "✗"
                                    : ""}
                                </span>
                              )}
                            </div>
                          </div>
                        </div>
                      );
                    })}
                    <div ref={messagesEndRef} />
                  </>
                )}
              </div>

              {/* Compose */}
              <div className="border-t border-border px-4 py-3 shrink-0">
                <form
                  onSubmit={(e) => {
                    e.preventDefault();
                    if (draft.trim() && !sendMutation.isPending) {
                      sendMutation.mutate();
                    }
                  }}
                  className="flex items-end gap-2"
                >
                  <textarea
                    value={draft}
                    onChange={(e) => setDraft(e.target.value)}
                    placeholder="Type a message..."
                    rows={1}
                    className={`${inputCls} resize-none min-h-[38px] max-h-[100px]`}
                    onKeyDown={(e) => {
                      if (e.key === "Enter" && !e.shiftKey) {
                        e.preventDefault();
                        if (draft.trim() && !sendMutation.isPending) {
                          sendMutation.mutate();
                        }
                      }
                    }}
                  />
                  <button
                    type="submit"
                    disabled={!draft.trim() || sendMutation.isPending}
                    className="flex items-center justify-center w-[38px] h-[38px] rounded-xl bg-teal text-white hover:bg-teal/90 disabled:opacity-40 transition-colors shrink-0"
                  >
                    <Send className="w-4 h-4" />
                  </button>
                </form>
                {draft.length > 0 && (
                  <p className="text-[11px] text-ink-faint mt-1 text-right">
                    {draft.length} chars · ~{Math.ceil(draft.length / 160)} segment
                    {Math.ceil(draft.length / 160) > 1 ? "s" : ""}
                  </p>
                )}
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
