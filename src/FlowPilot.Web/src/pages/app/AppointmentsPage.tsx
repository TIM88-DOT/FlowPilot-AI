import { useState } from "react";
import { useSearchParams } from "react-router-dom";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Plus, X, ChevronRight, ChevronLeft, Calendar, Clock, RefreshCw, Search } from "lucide-react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import api from "../../lib/api";
import type { ServiceDto } from "./SettingsPage";
import { useDebouncedValue } from "../../hooks/useDebouncedValue";

/* ------------------------------------------------------------------ */
/*  Types                                                              */
/* ------------------------------------------------------------------ */

interface Appointment {
  id: string;
  customerId: string;
  customerName: string;
  serviceName: string | null;
  startsAt: string;
  endsAt: string;
  status: string;
  staffUserId: string | null;
  externalId: string | null;
  notes: string | null;
  createdAt: string;
  updatedAt: string;
}

const statusFilters = ["All", "Scheduled", "Confirmed", "Completed", "Cancelled", "NoShow"];

const createSchema = z.object({
  customerId: z.string().min(1, "Required"),
  serviceName: z.string().optional(),
  startsAt: z.string().min(1, "Required"),
  durationMinutes: z.number().min(5).max(480),
  notes: z.string().optional(),
});

type CreateForm = z.infer<typeof createSchema>;

/* ------------------------------------------------------------------ */
/*  Main page                                                          */
/* ------------------------------------------------------------------ */

export default function AppointmentsPage() {
  const queryClient = useQueryClient();
  const [searchParams, setSearchParams] = useSearchParams();
  const [searchInput, setSearchInput] = useState(searchParams.get("search") ?? "");
  const debouncedSearch = useDebouncedValue(searchInput);
  const statusFilter = searchParams.get("status") ?? "All";
  const page = Number(searchParams.get("page") ?? "1");
  const [showCreate, setShowCreate] = useState(false);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const pageSize = 20;

  const setStatusFilter = (status: string) => {
    setSearchParams((prev) => {
      const params = new URLSearchParams(prev);
      if (status === "All") params.delete("status");
      else params.set("status", status);
      params.delete("page");
      return params;
    });
  };

  const setPage = (p: number | ((prev: number) => number)) => {
    const next = typeof p === "function" ? p(page) : p;
    setSearchParams((prev) => {
      const params = new URLSearchParams(prev);
      if (next <= 1) params.delete("page");
      else params.set("page", String(next));
      return params;
    });
  };

  const handleSearchChange = (value: string) => {
    setSearchInput(value);
    setSearchParams((prev) => {
      const params = new URLSearchParams(prev);
      if (value) params.set("search", value);
      else params.delete("search");
      params.delete("page");
      return params;
    });
  };

  const search = debouncedSearch;

  const { data, isLoading } = useQuery({
    queryKey: ["appointments", statusFilter, search, page],
    queryFn: () => {
      const params: Record<string, string | number> = { page, pageSize };
      if (statusFilter !== "All") params.status = statusFilter;
      if (search) params.search = search;
      return api.get("/appointments", { params }).then((r) => r.data);
    },
  });

  const appointments: Appointment[] = data?.items ?? [];
  const totalPages: number = data?.totalPages ?? 0;

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-[20px] font-bold text-ink">Appointments</h1>
          <p className="text-[13px] text-ink-muted mt-1">
            {data ? `${data.totalCount} total` : "Loading..."}
          </p>
        </div>
        <button
          onClick={() => setShowCreate(true)}
          className="flex items-center gap-2 px-4 py-2 bg-teal hover:bg-teal-light text-white text-[13px] font-medium rounded-xl transition-colors"
        >
          <Plus className="w-4 h-4" />
          New appointment
        </button>
      </div>

      {/* Search */}
      <div className="relative mb-4">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-ink-faint" />
        <input
          type="text"
          value={searchInput}
          onChange={(e) => handleSearchChange(e.target.value)}
          placeholder="Search by customer name or service..."
          className="w-full pl-10 pr-4 py-2.5 rounded-xl border border-border bg-warm-white text-[13px] text-ink placeholder:text-ink-faint focus:outline-none focus:border-teal transition-colors"
        />
      </div>

      {/* Status filter tabs */}
      <div className="flex gap-1 mb-4 overflow-x-auto">
        {statusFilters.map((s) => (
          <button
            key={s}
            onClick={() => setStatusFilter(s)}
            className={`px-3 py-1.5 rounded-lg text-[12px] font-medium transition-colors whitespace-nowrap ${
              statusFilter === s
                ? "bg-teal text-white"
                : "text-ink-muted hover:text-ink hover:bg-cream-dark/60"
            }`}
          >
            {s}
          </button>
        ))}
      </div>

      {/* List */}
      <div className="rounded-2xl border border-border bg-warm-white overflow-hidden">
        {isLoading ? (
          <p className="text-[13px] text-ink-faint py-12 text-center">Loading...</p>
        ) : appointments.length === 0 ? (
          <p className="text-[13px] text-ink-faint py-12 text-center">No appointments found</p>
        ) : (
          <div className="divide-y divide-border">
            {appointments.map((apt) => (
              <AppointmentRow
                key={apt.id}
                appointment={apt}
                onSelect={() => setSelectedId(apt.id)}
              />
            ))}
          </div>
        )}
      </div>

      {/* Pagination */}
      {totalPages > 1 && (
        <div className="flex items-center justify-between px-4 py-3 mt-2">
          <span className="text-[12px] text-ink-faint">
            Page {page} of {totalPages}
          </span>
          <div className="flex gap-1">
            <button
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              disabled={page === 1}
              className="p-1.5 rounded-lg hover:bg-cream-dark disabled:opacity-30 transition-colors"
            >
              <ChevronLeft className="w-4 h-4 text-ink-muted" />
            </button>
            <button
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
              disabled={page === totalPages}
              className="p-1.5 rounded-lg hover:bg-cream-dark disabled:opacity-30 transition-colors"
            >
              <ChevronRight className="w-4 h-4 text-ink-muted" />
            </button>
          </div>
        </div>
      )}

      {/* Modals */}
      {showCreate && (
        <CreateAppointmentModal
          onClose={() => setShowCreate(false)}
          onCreated={() => {
            setShowCreate(false);
            queryClient.invalidateQueries({ queryKey: ["appointments"] });
          }}
        />
      )}
      {selectedId && (
        <AppointmentDetailPanel
          appointmentId={selectedId}
          onClose={() => setSelectedId(null)}
        />
      )}
    </div>
  );
}

/* ------------------------------------------------------------------ */
/*  Appointment row                                                    */
/* ------------------------------------------------------------------ */

function AppointmentRow({
  appointment: apt,
  onSelect,
}: {
  appointment: Appointment;
  onSelect: () => void;
}) {
  const start = new Date(apt.startsAt);
  const end = new Date(apt.endsAt);
  const durationMin = Math.round((end.getTime() - start.getTime()) / 60000);

  return (
    <div
      onClick={onSelect}
      className="flex items-center justify-between px-4 py-3 hover:bg-cream-dark/30 transition-colors cursor-pointer"
    >
      <div className="flex items-center gap-4 min-w-0">
        <div className="min-w-0">
          <p className="text-[13px] font-medium text-ink truncate">{apt.customerName}</p>
          <p className="text-[11px] text-ink-faint">
            {apt.serviceName ?? "No service"} · {durationMin}min
          </p>
        </div>
      </div>

      <div className="flex items-center gap-3 shrink-0">
        <span className="text-[12px] text-ink-muted">
          {start.toLocaleDateString()} ·{" "}
          {start.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}
        </span>
        <StatusBadge status={apt.status} />
        <ChevronRight className="w-4 h-4 text-ink-faint" />
      </div>
    </div>
  );
}

/* ------------------------------------------------------------------ */
/*  Appointment detail slide-over                                      */
/* ------------------------------------------------------------------ */

function AppointmentDetailPanel({
  appointmentId,
  onClose,
}: {
  appointmentId: string;
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const [showReschedule, setShowReschedule] = useState(false);

  const { data: apt, isLoading } = useQuery<Appointment>({
    queryKey: ["appointment", appointmentId],
    queryFn: () => api.get(`/appointments/${appointmentId}`).then((r) => r.data),
  });

  const actionMutation = useMutation({
    mutationFn: (action: string) => api.post(`/appointments/${appointmentId}/${action}`),
    onSuccess: (_data, action) => {
      queryClient.invalidateQueries({ queryKey: ["appointment", appointmentId] });
      queryClient.invalidateQueries({ queryKey: ["appointments"] });
      toast.success(`Appointment ${action}ed successfully`);
    },
  });

  if (isLoading || !apt) {
    return (
      <div className="fixed inset-0 z-50">
        <div className="absolute inset-0 bg-ink/20 backdrop-blur-sm" onClick={onClose} />
        <div className="absolute right-0 top-0 bottom-0 w-full max-w-md bg-warm-white border-l border-border p-6 flex items-center justify-center">
          <div className="w-5 h-5 border-2 border-teal border-t-transparent rounded-full animate-spin" />
        </div>
      </div>
    );
  }

  const start = new Date(apt.startsAt);
  const end = new Date(apt.endsAt);
  const durationMin = Math.round((end.getTime() - start.getTime()) / 60000);

  const actions: Record<string, { label: string; variant: "confirm" | "complete" | "cancel" | "noshow" }[]> = {
    Scheduled: [
      { label: "confirm", variant: "confirm" },
      { label: "cancel", variant: "cancel" },
    ],
    Confirmed: [
      { label: "complete", variant: "complete" },
      { label: "cancel", variant: "cancel" },
    ],
  };

  const availableActions = actions[apt.status] ?? [];

  return (
    <div className="fixed inset-0 z-50">
      <div className="absolute inset-0 bg-ink/20 backdrop-blur-sm" onClick={onClose} />
      <div className="absolute right-0 top-0 bottom-0 w-full max-w-md bg-warm-white border-l border-border overflow-y-auto">
        {/* Header */}
        <div className="sticky top-0 bg-warm-white/90 backdrop-blur-sm border-b border-border px-6 py-4 flex items-center justify-between z-10">
          <div className="flex items-center gap-3">
            <h2 className="text-[16px] font-bold text-ink">{apt.customerName}</h2>
            <StatusBadge status={apt.status} />
          </div>
          <button onClick={onClose} className="p-1 text-ink-faint hover:text-ink">
            <X className="w-5 h-5" />
          </button>
        </div>

        <div className="p-6 space-y-6">
          {/* Time card */}
          <div className="rounded-xl bg-cream-dark/40 p-4 flex items-center gap-4">
            <div className="w-10 h-10 rounded-full bg-teal-wash border border-teal-border flex items-center justify-center">
              <Calendar className="w-[18px] h-[18px] text-teal" strokeWidth={1.8} />
            </div>
            <div>
              <p className="text-[14px] font-semibold text-ink">
                {start.toLocaleDateString(undefined, {
                  weekday: "long",
                  year: "numeric",
                  month: "long",
                  day: "numeric",
                })}
              </p>
              <p className="text-[13px] text-ink-muted flex items-center gap-1.5">
                <Clock className="w-3.5 h-3.5" />
                {start.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })} –{" "}
                {end.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })} ({durationMin}min)
              </p>
            </div>
          </div>

          {/* Info grid */}
          <div className="grid grid-cols-2 gap-4">
            <InfoField label="Service" value={apt.serviceName ?? "—"} />
            <InfoField label="Status" value={apt.status} />
            <InfoField label="Customer ID" value={apt.customerId} mono />
            {apt.staffUserId && <InfoField label="Staff ID" value={apt.staffUserId} mono />}
            {apt.externalId && <InfoField label="External ID" value={apt.externalId} mono />}
            {apt.notes && (
              <div className="col-span-2">
                <InfoField label="Notes" value={apt.notes} />
              </div>
            )}
            <InfoField label="Created" value={new Date(apt.createdAt).toLocaleString()} />
            <InfoField label="Updated" value={new Date(apt.updatedAt).toLocaleString()} />
          </div>

          {/* Actions */}
          <div className="flex flex-wrap gap-2">
            {availableActions.map((action) => {
              const variantCls: Record<string, string> = {
                confirm: "bg-blue-600 hover:bg-blue-700 text-white",
                complete: "bg-emerald-600 hover:bg-emerald-700 text-white",
                cancel: "text-red-600 hover:bg-red-50 border border-red-200",
                noshow: "text-amber hover:bg-amber-wash border border-amber-border",
              };
              return (
                <button
                  key={action.label}
                  onClick={() => actionMutation.mutate(action.label)}
                  disabled={actionMutation.isPending}
                  className={`flex items-center gap-1.5 px-4 py-2 rounded-xl text-[13px] font-medium transition-colors capitalize disabled:opacity-50 ${variantCls[action.variant]}`}
                >
                  {action.label}
                </button>
              );
            })}

            {(apt.status === "Scheduled" || apt.status === "Confirmed") && (
              <button
                onClick={() => setShowReschedule(true)}
                className="flex items-center gap-1.5 px-4 py-2 rounded-xl text-[13px] font-medium text-ink-muted hover:bg-cream-dark/60 border border-border transition-colors"
              >
                <RefreshCw className="w-3.5 h-3.5" />
                Reschedule
              </button>
            )}
          </div>

          {actionMutation.isError && (
            <p className="text-[12px] text-red-600">Action failed. Invalid status transition?</p>
          )}
        </div>

        {showReschedule && (
          <RescheduleModal
            appointment={apt}
            onClose={() => setShowReschedule(false)}
            onRescheduled={() => {
              setShowReschedule(false);
              queryClient.invalidateQueries({ queryKey: ["appointment", appointmentId] });
              queryClient.invalidateQueries({ queryKey: ["appointments"] });
            }}
          />
        )}
      </div>
    </div>
  );
}

/* ------------------------------------------------------------------ */
/*  Reschedule modal                                                   */
/* ------------------------------------------------------------------ */

const rescheduleSchema = z.object({
  startsAt: z.string().min(1, "Required"),
  durationMinutes: z.number().min(5).max(480),
});

type RescheduleForm = z.infer<typeof rescheduleSchema>;

function RescheduleModal({
  appointment,
  onClose,
  onRescheduled,
}: {
  appointment: Appointment;
  onClose: () => void;
  onRescheduled: () => void;
}) {
  const [error, setError] = useState("");
  const currentDuration = Math.round(
    (new Date(appointment.endsAt).getTime() - new Date(appointment.startsAt).getTime()) / 60000
  );

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<RescheduleForm>({
    resolver: zodResolver(rescheduleSchema),
    defaultValues: { durationMinutes: currentDuration },
  });

  const onSubmit = async (data: RescheduleForm) => {
    try {
      setError("");
      const startsAt = new Date(data.startsAt).toISOString();
      const endsAt = new Date(
        new Date(data.startsAt).getTime() + data.durationMinutes * 60000
      ).toISOString();
      await api.post(`/appointments/${appointment.id}/reschedule`, { startsAt, endsAt });
      toast.success("Appointment rescheduled");
      onRescheduled();
    } catch {
      setError("Failed to reschedule.");
    }
  };

  return (
    <ModalShell title="Reschedule appointment" onClose={onClose}>
      <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
        <ErrorBanner message={error} />
        <Field label="New date & time" error={errors.startsAt?.message}>
          <input type="datetime-local" {...register("startsAt")} className={inputCls} />
        </Field>
        <Field label="Duration (min)" error={errors.durationMinutes?.message}>
          <input
            type="number"
            {...register("durationMinutes", { valueAsNumber: true })}
            className={inputCls}
          />
        </Field>
        <ModalActions onClose={onClose} isSubmitting={isSubmitting} submitLabel="Reschedule" />
      </form>
    </ModalShell>
  );
}

/* ------------------------------------------------------------------ */
/*  Create appointment modal                                           */
/* ------------------------------------------------------------------ */

function CreateAppointmentModal({
  onClose,
  onCreated,
}: {
  onClose: () => void;
  onCreated: () => void;
}) {
  const [error, setError] = useState("");

  const { data: servicesData } = useQuery<ServiceDto[]>({
    queryKey: ["services"],
    queryFn: () => api.get("/services").then((r) => r.data),
  });
  const activeServices = (servicesData ?? []).filter((s) => s.isActive);

  const { data: customerData } = useQuery({
    queryKey: ["customers", "all-for-select"],
    queryFn: () =>
      api.get("/customers", { params: { pageSize: 200 } }).then((r) => r.data),
  });

  const customers: { id: string; firstName: string; lastName: string | null }[] =
    customerData?.items ?? [];

  const {
    register,
    handleSubmit,
    setValue,
    formState: { errors, isSubmitting },
  } = useForm<CreateForm>({
    resolver: zodResolver(createSchema),
    defaultValues: { durationMinutes: 30 },
  });

  const handleServiceChange = (serviceName: string) => {
    const match = activeServices.find((s) => s.name === serviceName);
    if (match) {
      setValue("durationMinutes", match.durationMinutes);
    }
  };

  const onSubmit = async (data: CreateForm) => {
    try {
      setError("");
      const startsAt = new Date(data.startsAt).toISOString();
      const endsAt = new Date(
        new Date(data.startsAt).getTime() + data.durationMinutes * 60000
      ).toISOString();
      await api.post("/appointments", {
        customerId: data.customerId,
        startsAt,
        endsAt,
        serviceName: data.serviceName || null,
        notes: data.notes || null,
      });
      toast.success("Appointment created");
      onCreated();
    } catch {
      setError("Failed to create appointment.");
    }
  };

  return (
    <ModalShell title="New appointment" onClose={onClose}>
      <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
        <ErrorBanner message={error} />
        <Field label="Customer" error={errors.customerId?.message}>
          <select {...register("customerId")} className={inputCls}>
            <option value="">Select a customer...</option>
            {customers.map((c) => (
              <option key={c.id} value={c.id}>
                {c.firstName} {c.lastName ?? ""}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Service">
          {activeServices.length > 0 ? (
            <>
              <input
                {...register("serviceName", {
                  onChange: (e) => handleServiceChange(e.target.value),
                })}
                list="service-options"
                placeholder="Select or type a custom service..."
                className={inputCls}
              />
              <datalist id="service-options">
                {activeServices.map((s) => (
                  <option key={s.id} value={s.name}>
                    {s.name} — {s.durationMinutes}min{s.price ? ` · ${s.price}` : ""}
                  </option>
                ))}
              </datalist>
            </>
          ) : (
            <input
              {...register("serviceName")}
              placeholder="e.g. Haircut, Consultation..."
              className={inputCls}
            />
          )}
          {activeServices.length === 0 && (
            <p className="text-[11px] text-ink-faint mt-1">
              Tip: Add services in Settings → Services to get a dropdown here.
            </p>
          )}
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Date & time" error={errors.startsAt?.message}>
            <input type="datetime-local" {...register("startsAt")} className={inputCls} />
          </Field>
          <Field label="Duration (min)" error={errors.durationMinutes?.message}>
            <input
              type="number"
              {...register("durationMinutes", { valueAsNumber: true })}
              className={inputCls}
            />
          </Field>
        </div>
        <Field label="Notes (optional)">
          <input {...register("notes")} placeholder="Any notes..." className={inputCls} />
        </Field>
        <ModalActions onClose={onClose} isSubmitting={isSubmitting} submitLabel="Create" />
      </form>
    </ModalShell>
  );
}

/* ------------------------------------------------------------------ */
/*  Shared UI                                                          */
/* ------------------------------------------------------------------ */

function StatusBadge({ status }: { status: string }) {
  const styles: Record<string, string> = {
    Scheduled: "bg-amber-wash text-amber border-amber-border",
    Confirmed: "bg-teal-wash text-teal border-teal-border",
    Completed: "bg-teal-wash text-teal border-teal-border",
    Cancelled: "bg-red-50 text-red-600 border-red-200",
    NoShow: "bg-red-50 text-red-600 border-red-200",
  };
  return (
    <span
      className={`text-[10px] font-medium px-2 py-0.5 rounded-full border ${
        styles[status] ?? "bg-cream-dark text-ink-muted border-border"
      }`}
    >
      {status}
    </span>
  );
}

function InfoField({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div>
      <p className="text-[11px] text-ink-faint mb-1">{label}</p>
      <p className={`text-[13px] text-ink font-medium ${mono ? "font-mono text-[11px] break-all" : ""}`}>
        {value}
      </p>
    </div>
  );
}

const inputCls =
  "w-full px-4 py-2.5 rounded-xl border border-border bg-white text-[13px] text-ink placeholder:text-ink-faint focus:outline-none focus:border-teal transition-colors";

function ModalShell({
  title,
  onClose,
  children,
}: {
  title: string;
  onClose: () => void;
  children: React.ReactNode;
}) {
  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center p-6">
      <div className="absolute inset-0 bg-ink/20 backdrop-blur-sm" onClick={onClose} />
      <div className="relative bg-warm-white rounded-2xl border border-border p-6 w-full max-w-md shadow-xl">
        <div className="flex items-center justify-between mb-5">
          <h2 className="text-[16px] font-bold text-ink">{title}</h2>
          <button onClick={onClose} className="p-1 text-ink-faint hover:text-ink">
            <X className="w-5 h-5" />
          </button>
        </div>
        {children}
      </div>
    </div>
  );
}

function Field({
  label,
  error,
  children,
}: {
  label: string;
  error?: string;
  children: React.ReactNode;
}) {
  return (
    <div>
      <label className="block text-[13px] font-medium text-ink mb-1.5">{label}</label>
      {children}
      {error && <p className="text-[12px] text-red-500 mt-1">{error}</p>}
    </div>
  );
}

function ErrorBanner({ message }: { message: string }) {
  if (!message) return null;
  return (
    <div className="text-[13px] text-red-600 bg-red-50 border border-red-200 rounded-xl px-4 py-2.5">
      {message}
    </div>
  );
}

function ModalActions({
  onClose,
  isSubmitting,
  submitLabel,
  onSubmit,
}: {
  onClose: () => void;
  isSubmitting: boolean;
  submitLabel: string;
  onSubmit?: () => void;
}) {
  return (
    <div className="flex gap-3 pt-2">
      <button
        type="button"
        onClick={onClose}
        className="flex-1 py-2.5 text-[13px] font-medium text-ink-muted border border-border rounded-xl hover:bg-cream-dark/60 transition-colors"
      >
        Cancel
      </button>
      <button
        type={onSubmit ? "button" : "submit"}
        onClick={onSubmit}
        disabled={isSubmitting}
        className="flex-1 py-2.5 bg-teal hover:bg-teal-light text-white text-[13px] font-medium rounded-xl transition-colors disabled:opacity-50"
      >
        {isSubmitting ? "..." : submitLabel}
      </button>
    </div>
  );
}
