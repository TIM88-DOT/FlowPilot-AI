import { useState } from "react";
import { useSearchParams } from "react-router-dom";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  Search,
  Plus,
  ChevronLeft,
  ChevronRight,
  X,
  Pencil,
  Trash2,
  Upload,
  History,
} from "lucide-react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import api from "../../lib/api";
import { useDebouncedValue } from "../../hooks/useDebouncedValue";

/* ------------------------------------------------------------------ */
/*  Types                                                              */
/* ------------------------------------------------------------------ */

interface Customer {
  id: string;
  firstName: string;
  lastName: string | null;
  phone: string;
  email: string | null;
  preferredLanguage: string;
  consentStatus: string;
  noShowScore: number;
  tags: string | null;
  createdAt: string;
  updatedAt: string;
}

interface ConsentRecord {
  id: string;
  status: string;
  source: string;
  notes: string | null;
  createdAt: string;
}

interface PagedResult {
  items: Customer[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

/* ------------------------------------------------------------------ */
/*  Main page                                                          */
/* ------------------------------------------------------------------ */

export default function CustomersPage() {
  const queryClient = useQueryClient();
  const [searchParams, setSearchParams] = useSearchParams();
  const [searchInput, setSearchInput] = useState(searchParams.get("search") ?? "");
  const debouncedSearch = useDebouncedValue(searchInput);
  const page = Number(searchParams.get("page") ?? "1");
  const [showCreate, setShowCreate] = useState(false);
  const [showImport, setShowImport] = useState(false);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const pageSize = 20;

  const setPage = (p: number | ((prev: number) => number)) => {
    const next = typeof p === "function" ? p(page) : p;
    setSearchParams((prev) => {
      const params = new URLSearchParams(prev);
      if (next <= 1) params.delete("page");
      else params.set("page", String(next));
      return params;
    });
  };

  // Sync debounced search to URL
  const search = debouncedSearch;
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

  const { data, isLoading } = useQuery<PagedResult>({
    queryKey: ["customers", search, page],
    queryFn: () =>
      api.get("/customers", { params: { search: search || undefined, page, pageSize } }).then((r) => r.data),
  });

  const totalPages = data?.totalPages ?? 0;

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-[20px] font-bold text-ink">Customers</h1>
          <p className="text-[13px] text-ink-muted mt-1">
            {data ? `${data.totalCount} total` : "Loading..."}
          </p>
        </div>
        <div className="flex gap-2">
          <button
            onClick={() => setShowImport(true)}
            className="flex items-center gap-2 px-4 py-2 text-ink-muted border border-border hover:border-border-strong text-[13px] font-medium rounded-xl transition-colors"
          >
            <Upload className="w-4 h-4" />
            Import CSV
          </button>
          <button
            onClick={() => setShowCreate(true)}
            className="flex items-center gap-2 px-4 py-2 bg-teal hover:bg-teal-light text-white text-[13px] font-medium rounded-xl transition-colors"
          >
            <Plus className="w-4 h-4" />
            Add customer
          </button>
        </div>
      </div>

      {/* Search */}
      <div className="relative mb-4">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-ink-faint" />
        <input
          type="text"
          value={searchInput}
          onChange={(e) => handleSearchChange(e.target.value)}
          placeholder="Search by name, phone, or email..."
          className="w-full pl-10 pr-4 py-2.5 rounded-xl border border-border bg-warm-white text-[13px] text-ink placeholder:text-ink-faint focus:outline-none focus:border-teal transition-colors"
        />
      </div>

      {/* Table */}
      <div className="rounded-2xl border border-border bg-warm-white overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-left">
            <thead>
              <tr className="border-b border-border">
                <th className="px-4 py-3 text-[11px] font-semibold text-ink-faint uppercase tracking-wider">
                  Name
                </th>
                <th className="px-4 py-3 text-[11px] font-semibold text-ink-faint uppercase tracking-wider">
                  Phone
                </th>
                <th className="px-4 py-3 text-[11px] font-semibold text-ink-faint uppercase tracking-wider">
                  Language
                </th>
                <th className="px-4 py-3 text-[11px] font-semibold text-ink-faint uppercase tracking-wider">
                  Consent
                </th>
                <th className="px-4 py-3 text-[11px] font-semibold text-ink-faint uppercase tracking-wider">
                  No-show
                </th>
                <th className="px-4 py-3 text-[11px] font-semibold text-ink-faint uppercase tracking-wider">
                  Tags
                </th>
                <th className="px-4 py-3" />
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                <tr>
                  <td colSpan={7} className="px-4 py-12 text-center text-[13px] text-ink-faint">
                    Loading...
                  </td>
                </tr>
              ) : !data?.items.length ? (
                <tr>
                  <td colSpan={7} className="px-4 py-12 text-center text-[13px] text-ink-faint">
                    No customers found
                  </td>
                </tr>
              ) : (
                data.items.map((c) => (
                  <tr
                    key={c.id}
                    className="border-b border-border last:border-0 hover:bg-cream-dark/30 transition-colors cursor-pointer"
                    onClick={() => setSelectedId(c.id)}
                  >
                    <td className="px-4 py-3">
                      <p className="text-[13px] font-medium text-ink">
                        {c.firstName} {c.lastName ?? ""}
                      </p>
                      {c.email && <p className="text-[11px] text-ink-faint">{c.email}</p>}
                    </td>
                    <td className="px-4 py-3 text-[13px] text-ink-muted">{c.phone}</td>
                    <td className="px-4 py-3 text-[13px] text-ink-muted uppercase">
                      {c.preferredLanguage}
                    </td>
                    <td className="px-4 py-3">
                      <ConsentBadge status={c.consentStatus} />
                    </td>
                    <td className="px-4 py-3">
                      <NoShowScore score={c.noShowScore} />
                    </td>
                    <td className="px-4 py-3">
                      {c.tags && (
                        <div className="flex gap-1 flex-wrap">
                          {c.tags.split(",").map((tag) => (
                            <span
                              key={tag}
                              className="text-[10px] bg-cream-dark text-ink-muted px-2 py-0.5 rounded-full"
                            >
                              {tag.trim()}
                            </span>
                          ))}
                        </div>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      <ChevronRight className="w-4 h-4 text-ink-faint" />
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {totalPages > 1 && (
          <div className="flex items-center justify-between px-4 py-3 border-t border-border">
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
      </div>

      {/* Modals */}
      {showCreate && (
        <CreateCustomerModal
          onClose={() => setShowCreate(false)}
          onCreated={() => {
            setShowCreate(false);
            queryClient.invalidateQueries({ queryKey: ["customers"] });
          }}
        />
      )}
      {showImport && (
        <ImportCsvModal
          onClose={() => setShowImport(false)}
          onImported={() => {
            setShowImport(false);
            queryClient.invalidateQueries({ queryKey: ["customers"] });
          }}
        />
      )}
      {selectedId && (
        <CustomerDetailPanel
          customerId={selectedId}
          onClose={() => setSelectedId(null)}
        />
      )}
    </div>
  );
}

/* ------------------------------------------------------------------ */
/*  Shared badges                                                      */
/* ------------------------------------------------------------------ */

function ConsentBadge({ status }: { status: string }) {
  const granted = status === "Granted" || status === "OptedIn";
  return (
    <span
      className={`text-[10px] font-medium px-2 py-0.5 rounded-full border ${
        granted
          ? "bg-teal-wash text-teal border-teal-border"
          : "bg-red-50 text-red-600 border-red-200"
      }`}
    >
      {status}
    </span>
  );
}

function NoShowScore({ score }: { score: number }) {
  const color = score >= 0.5 ? "text-red-600" : score >= 0.2 ? "text-amber" : "text-teal";
  return <span className={`text-[13px] font-medium ${color}`}>{Math.round(score * 100)}%</span>;
}

/* ------------------------------------------------------------------ */
/*  Customer detail slide-over                                         */
/* ------------------------------------------------------------------ */

function CustomerDetailPanel({
  customerId,
  onClose,
}: {
  customerId: string;
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const [showEdit, setShowEdit] = useState(false);
  const [showHistory, setShowHistory] = useState(false);
  const [showDelete, setShowDelete] = useState(false);

  const { data: customer, isLoading } = useQuery<Customer>({
    queryKey: ["customer", customerId],
    queryFn: () => api.get(`/customers/${customerId}`).then((r) => r.data),
  });

  const deleteMutation = useMutation({
    mutationFn: () => api.delete(`/customers/${customerId}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["customers"] });
      toast.success("Customer deleted");
      onClose();
    },
  });

  if (isLoading || !customer) {
    return (
      <div className="fixed inset-0 z-50">
        <div className="absolute inset-0 bg-ink/20 backdrop-blur-sm" onClick={onClose} />
        <div className="absolute right-0 top-0 bottom-0 w-full max-w-md bg-warm-white border-l border-border p-6 flex items-center justify-center">
          <div className="w-5 h-5 border-2 border-teal border-t-transparent rounded-full animate-spin" />
        </div>
      </div>
    );
  }

  return (
    <div className="fixed inset-0 z-50">
      <div className="absolute inset-0 bg-ink/20 backdrop-blur-sm" onClick={onClose} />
      <div className="absolute right-0 top-0 bottom-0 w-full max-w-md bg-warm-white border-l border-border overflow-y-auto">
        {/* Header */}
        <div className="sticky top-0 bg-warm-white/90 backdrop-blur-sm border-b border-border px-6 py-4 flex items-center justify-between z-10">
          <h2 className="text-[16px] font-bold text-ink">
            {customer.firstName} {customer.lastName ?? ""}
          </h2>
          <button onClick={onClose} className="p-1 text-ink-faint hover:text-ink">
            <X className="w-5 h-5" />
          </button>
        </div>

        <div className="p-6 space-y-6">
          {/* Info grid */}
          <div className="grid grid-cols-2 gap-4">
            <InfoField label="Phone" value={customer.phone} />
            <InfoField label="Email" value={customer.email ?? "—"} />
            <InfoField label="Language" value={customer.preferredLanguage.toUpperCase()} />
            <div>
              <p className="text-[11px] text-ink-faint mb-1">Consent</p>
              <ConsentBadge status={customer.consentStatus} />
            </div>
            <div>
              <p className="text-[11px] text-ink-faint mb-1">No-show score</p>
              <NoShowScore score={customer.noShowScore} />
            </div>
            <InfoField label="Tags" value={customer.tags ?? "—"} />
            <InfoField label="Created" value={new Date(customer.createdAt).toLocaleDateString()} />
            <InfoField label="Updated" value={new Date(customer.updatedAt).toLocaleDateString()} />
          </div>

          {/* Actions */}
          <div className="flex flex-wrap gap-2">
            <ActionBtn icon={Pencil} label="Edit" onClick={() => setShowEdit(true)} />
            <ActionBtn icon={History} label="Consent history" onClick={() => setShowHistory(true)} />
            <ActionBtn icon={Trash2} label="Delete (GDPR)" onClick={() => setShowDelete(true)} variant="danger" />
          </div>
        </div>

        {/* Sub-modals */}
        {showEdit && (
          <EditCustomerModal
            customer={customer}
            onClose={() => setShowEdit(false)}
            onSaved={() => {
              setShowEdit(false);
              queryClient.invalidateQueries({ queryKey: ["customer", customerId] });
              queryClient.invalidateQueries({ queryKey: ["customers"] });
            }}
          />
        )}
        {showHistory && (
          <ConsentHistoryModal customerId={customerId} onClose={() => setShowHistory(false)} />
        )}
        {showDelete && (
          <ConfirmModal
            title="Delete customer (GDPR)"
            message="This will anonymize all PII fields and soft-delete the customer. This action cannot be undone."
            confirmLabel="Delete permanently"
            isLoading={deleteMutation.isPending}
            onConfirm={() => deleteMutation.mutate()}
            onClose={() => setShowDelete(false)}
          />
        )}
      </div>
    </div>
  );
}

function InfoField({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <p className="text-[11px] text-ink-faint mb-1">{label}</p>
      <p className="text-[13px] text-ink font-medium">{value}</p>
    </div>
  );
}

function ActionBtn({
  icon: Icon,
  label,
  onClick,
  variant = "default",
}: {
  icon: typeof Pencil;
  label: string;
  onClick: () => void;
  variant?: "default" | "danger";
}) {
  return (
    <button
      onClick={onClick}
      className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[12px] font-medium transition-colors ${
        variant === "danger"
          ? "text-red-600 hover:bg-red-50 border border-red-200"
          : "text-ink-muted hover:text-ink hover:bg-cream-dark/60 border border-border"
      }`}
    >
      <Icon className="w-3.5 h-3.5" />
      {label}
    </button>
  );
}

/* ------------------------------------------------------------------ */
/*  Create customer modal                                              */
/* ------------------------------------------------------------------ */

const createSchema = z.object({
  firstName: z.string().min(1, "First name is required"),
  lastName: z.string().optional(),
  phone: z.string().min(6, "Phone is required (E.164 format)"),
  email: z.string().email("Invalid email").optional().or(z.literal("")),
  preferredLanguage: z.string().min(1),
  tags: z.string().optional(),
});

type CreateCustomerForm = z.infer<typeof createSchema>;

function CreateCustomerModal({ onClose, onCreated }: { onClose: () => void; onCreated: () => void }) {
  const [error, setError] = useState("");
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<CreateCustomerForm>({
    resolver: zodResolver(createSchema),
    defaultValues: { preferredLanguage: "fr" },
  });

  const onSubmit = async (data: CreateCustomerForm) => {
    try {
      setError("");
      await api.post("/customers", {
        phone: data.phone,
        firstName: data.firstName,
        lastName: data.lastName || null,
        email: data.email || null,
        preferredLanguage: data.preferredLanguage,
        tags: data.tags || null,
      });
      toast.success("Customer created");
      onCreated();
    } catch {
      setError("Failed to create customer. Check the phone format (E.164).");
    }
  };

  return (
    <ModalShell title="Add customer" onClose={onClose}>
      <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
        <ErrorBanner message={error} />
        <div className="grid grid-cols-2 gap-3">
          <Field label="First name" error={errors.firstName?.message}>
            <input {...register("firstName")} placeholder="Sarah" className={inputCls} />
          </Field>
          <Field label="Last name">
            <input {...register("lastName")} placeholder="Benali" className={inputCls} />
          </Field>
        </div>
        <Field label="Phone (E.164)" error={errors.phone?.message}>
          <input {...register("phone")} placeholder="+213551234567" className={inputCls} />
        </Field>
        <Field label="Email (optional)" error={errors.email?.message}>
          <input {...register("email")} placeholder="sarah@example.com" className={inputCls} />
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Language">
            <select {...register("preferredLanguage")} className={inputCls}>
              <option value="fr">French</option>
              <option value="ar">Arabic</option>
              <option value="en">English</option>
            </select>
          </Field>
          <Field label="Tags (comma-separated)">
            <input {...register("tags")} placeholder="vip, new" className={inputCls} />
          </Field>
        </div>
        <ModalActions onClose={onClose} isSubmitting={isSubmitting} submitLabel="Add customer" />
      </form>
    </ModalShell>
  );
}

/* ------------------------------------------------------------------ */
/*  Edit customer modal                                                */
/* ------------------------------------------------------------------ */

const editSchema = z.object({
  firstName: z.string().min(1).optional(),
  lastName: z.string().optional(),
  phone: z.string().min(6).optional(),
  email: z.string().email().optional().or(z.literal("")),
  preferredLanguage: z.string().optional(),
  tags: z.string().optional(),
});

type EditCustomerForm = z.infer<typeof editSchema>;

function EditCustomerModal({
  customer,
  onClose,
  onSaved,
}: {
  customer: Customer;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [error, setError] = useState("");
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<EditCustomerForm>({
    resolver: zodResolver(editSchema),
    defaultValues: {
      firstName: customer.firstName,
      lastName: customer.lastName ?? "",
      phone: customer.phone,
      email: customer.email ?? "",
      preferredLanguage: customer.preferredLanguage,
      tags: customer.tags ?? "",
    },
  });

  const onSubmit = async (data: EditCustomerForm) => {
    try {
      setError("");
      await api.put(`/customers/${customer.id}`, {
        firstName: data.firstName || null,
        lastName: data.lastName || null,
        phone: data.phone || null,
        email: data.email || null,
        preferredLanguage: data.preferredLanguage || null,
        tags: data.tags || null,
      });
      toast.success("Customer updated");
      onSaved();
    } catch {
      setError("Failed to update customer.");
    }
  };

  return (
    <ModalShell title="Edit customer" onClose={onClose}>
      <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
        <ErrorBanner message={error} />
        <div className="grid grid-cols-2 gap-3">
          <Field label="First name" error={errors.firstName?.message}>
            <input {...register("firstName")} className={inputCls} />
          </Field>
          <Field label="Last name">
            <input {...register("lastName")} className={inputCls} />
          </Field>
        </div>
        <Field label="Phone" error={errors.phone?.message}>
          <input {...register("phone")} className={inputCls} />
        </Field>
        <Field label="Email" error={errors.email?.message}>
          <input {...register("email")} className={inputCls} />
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Language">
            <select {...register("preferredLanguage")} className={inputCls}>
              <option value="fr">French</option>
              <option value="ar">Arabic</option>
              <option value="en">English</option>
            </select>
          </Field>
          <Field label="Tags">
            <input {...register("tags")} className={inputCls} />
          </Field>
        </div>
        <ModalActions onClose={onClose} isSubmitting={isSubmitting} submitLabel="Save changes" />
      </form>
    </ModalShell>
  );
}

/* ------------------------------------------------------------------ */
/*  Consent history modal (read-only)                                  */
/* ------------------------------------------------------------------ */

function ConsentHistoryModal({ customerId, onClose }: { customerId: string; onClose: () => void }) {
  const { data, isLoading } = useQuery<ConsentRecord[]>({
    queryKey: ["customer", customerId, "history"],
    queryFn: () => api.get(`/customers/${customerId}/history`).then((r) => r.data),
  });

  return (
    <ModalShell title="Consent history" onClose={onClose}>
      {isLoading ? (
        <p className="text-[13px] text-ink-faint py-8 text-center">Loading...</p>
      ) : !data?.length ? (
        <p className="text-[13px] text-ink-faint py-8 text-center">No consent records</p>
      ) : (
        <div className="space-y-2 max-h-[400px] overflow-y-auto">
          {data.map((r) => (
            <div key={r.id} className="flex items-start justify-between py-2 px-3 rounded-xl bg-cream-dark/30">
              <div>
                <div className="flex items-center gap-2">
                  <ConsentBadge status={r.status} />
                  <span className="text-[11px] text-ink-faint">{r.source}</span>
                </div>
                {r.notes && <p className="text-[11px] text-ink-muted mt-1">{r.notes}</p>}
              </div>
              <span className="text-[11px] text-ink-faint shrink-0">
                {new Date(r.createdAt).toLocaleString()}
              </span>
            </div>
          ))}
        </div>
      )}
    </ModalShell>
  );
}

/* ------------------------------------------------------------------ */
/*  CSV import modal                                                   */
/* ------------------------------------------------------------------ */

function ImportCsvModal({ onClose, onImported }: { onClose: () => void; onImported: () => void }) {
  const [file, setFile] = useState<File | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState("");
  const [result, setResult] = useState<{
    totalRows: number;
    imported: number;
    skipped: number;
    errors: { row: number; error: string }[];
  } | null>(null);

  const handleUpload = async () => {
    if (!file) return;
    setIsSubmitting(true);
    setError("");
    try {
      const form = new FormData();
      form.append("file", file);
      const { data } = await api.post("/customers/import", form, {
        headers: { "Content-Type": "multipart/form-data" },
      });
      setResult(data);
      toast.success(`Imported ${data.imported} customers`);
    } catch {
      setError("Failed to import CSV.");
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <ModalShell title="Import customers from CSV" onClose={onClose}>
      <ErrorBanner message={error} />
      {result ? (
        <div className="space-y-3">
          <div className="grid grid-cols-3 gap-3">
            <div className="text-center p-3 rounded-xl bg-cream-dark/50">
              <p className="text-[18px] font-bold text-ink">{result.totalRows}</p>
              <p className="text-[11px] text-ink-faint">Total rows</p>
            </div>
            <div className="text-center p-3 rounded-xl bg-teal-wash">
              <p className="text-[18px] font-bold text-teal">{result.imported}</p>
              <p className="text-[11px] text-ink-faint">Imported</p>
            </div>
            <div className="text-center p-3 rounded-xl bg-amber-wash">
              <p className="text-[18px] font-bold text-amber">{result.skipped}</p>
              <p className="text-[11px] text-ink-faint">Skipped</p>
            </div>
          </div>
          {result.errors.length > 0 && (
            <div className="max-h-[200px] overflow-y-auto space-y-1">
              {result.errors.map((e, i) => (
                <p key={i} className="text-[11px] text-red-600">
                  Row {e.row}: {e.error}
                </p>
              ))}
            </div>
          )}
          <button
            onClick={onImported}
            className="w-full py-2.5 bg-teal hover:bg-teal-light text-white text-[13px] font-medium rounded-xl transition-colors"
          >
            Done
          </button>
        </div>
      ) : (
        <div className="space-y-4">
          <p className="text-[13px] text-ink-muted">
            Upload a CSV with columns: <code className="text-[12px] bg-cream-dark px-1 rounded">Phone, FirstName, LastName, Email, PreferredLanguage</code>
          </p>
          <input
            type="file"
            accept=".csv"
            onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            className="block w-full text-[13px] text-ink-muted file:mr-3 file:py-2 file:px-4 file:rounded-xl file:border file:border-border file:text-[13px] file:font-medium file:bg-warm-white file:text-ink hover:file:bg-cream-dark transition-colors"
          />
          <ModalActions
            onClose={onClose}
            isSubmitting={isSubmitting}
            submitLabel="Upload & import"
            onSubmit={handleUpload}
            disabled={!file}
          />
        </div>
      )}
    </ModalShell>
  );
}

/* ------------------------------------------------------------------ */
/*  Shared UI helpers                                                  */
/* ------------------------------------------------------------------ */

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

function ConfirmModal({
  title,
  message,
  confirmLabel,
  isLoading,
  onConfirm,
  onClose,
}: {
  title: string;
  message: string;
  confirmLabel: string;
  isLoading: boolean;
  onConfirm: () => void;
  onClose: () => void;
}) {
  return (
    <ModalShell title={title} onClose={onClose}>
      <p className="text-[13px] text-ink-muted mb-5">{message}</p>
      <ModalActions
        onClose={onClose}
        isSubmitting={isLoading}
        submitLabel={confirmLabel}
        onSubmit={onConfirm}
        variant="danger"
      />
    </ModalShell>
  );
}

function ModalActions({
  onClose,
  isSubmitting,
  submitLabel,
  onSubmit,
  variant = "default",
  disabled = false,
}: {
  onClose: () => void;
  isSubmitting: boolean;
  submitLabel: string;
  onSubmit?: () => void;
  variant?: "default" | "danger";
  disabled?: boolean;
}) {
  const btnCls =
    variant === "danger"
      ? "bg-red-600 hover:bg-red-700 text-white"
      : "bg-teal hover:bg-teal-light text-white";
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
        disabled={isSubmitting || disabled}
        className={`flex-1 py-2.5 text-[13px] font-medium rounded-xl transition-colors disabled:opacity-50 ${btnCls}`}
      >
        {isSubmitting ? "..." : submitLabel}
      </button>
    </div>
  );
}
