import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Plus, X, ChevronRight, Trash2, Pencil } from "lucide-react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import api from "../../lib/api";

/* ------------------------------------------------------------------ */
/*  Types                                                              */
/* ------------------------------------------------------------------ */

interface LocaleVariant {
  id: string;
  locale: string;
  body: string;
  segmentCount: number;
}

interface Template {
  id: string;
  name: string;
  description: string | null;
  category: string;
  isSystem: boolean;
  localeVariants: LocaleVariant[];
  createdAt: string;
  updatedAt: string;
}

const SMS_SEGMENT_LENGTH = 160;
const locales = ["fr", "en"];

/* ------------------------------------------------------------------ */
/*  Main page                                                          */
/* ------------------------------------------------------------------ */

export default function TemplatesPage() {
  const queryClient = useQueryClient();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [showCreate, setShowCreate] = useState(false);

  const { data, isLoading } = useQuery<Template[]>({
    queryKey: ["templates"],
    queryFn: () => api.get("/templates").then((r) => r.data),
  });

  const templates = data ?? [];
  const selected = templates.find((t) => t.id === selectedId);

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-[20px] font-bold text-ink">Templates</h1>
          <p className="text-[13px] text-ink-muted mt-1">SMS templates with locale variants</p>
        </div>
        <button
          onClick={() => setShowCreate(true)}
          className="flex items-center gap-2 px-4 py-2 bg-teal hover:bg-teal-light text-white text-[13px] font-medium rounded-xl transition-colors"
        >
          <Plus className="w-4 h-4" />
          New template
        </button>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        {/* Template list */}
        <div className="lg:col-span-1 rounded-2xl border border-border bg-warm-white overflow-hidden">
          {isLoading ? (
            <p className="text-[13px] text-ink-faint py-12 text-center">Loading...</p>
          ) : templates.length === 0 ? (
            <p className="text-[13px] text-ink-faint py-12 text-center">No templates yet</p>
          ) : (
            <div className="divide-y divide-border">
              {templates.map((t) => (
                <button
                  key={t.id}
                  onClick={() => setSelectedId(t.id)}
                  className={`w-full text-left px-4 py-3 flex items-center justify-between hover:bg-cream-dark/30 transition-colors ${
                    selectedId === t.id ? "bg-cream-dark/50" : ""
                  }`}
                >
                  <div>
                    <div className="flex items-center gap-2">
                      <p className="text-[13px] font-medium text-ink">{t.name}</p>
                      {t.isSystem && (
                        <span className="text-[9px] font-semibold text-teal bg-teal-wash px-1.5 py-0.5 rounded-full border border-teal-border uppercase">
                          System
                        </span>
                      )}
                    </div>
                    <p className="text-[11px] text-ink-faint">
                      {t.category} · {t.localeVariants?.length ?? 0} locale(s)
                    </p>
                  </div>
                  <ChevronRight className="w-4 h-4 text-ink-faint" />
                </button>
              ))}
            </div>
          )}
        </div>

        {/* Detail / editor */}
        <div className="lg:col-span-2">
          {selected ? (
            <TemplateDetail
              template={selected}
              onUpdated={() =>
                queryClient.invalidateQueries({ queryKey: ["templates"] })
              }
              onDeleted={() => {
                setSelectedId(null);
                queryClient.invalidateQueries({ queryKey: ["templates"] });
              }}
            />
          ) : (
            <div className="rounded-2xl border border-border bg-warm-white p-8 text-center">
              <p className="text-[13px] text-ink-faint">
                Select a template to view and edit its locale variants
              </p>
            </div>
          )}
        </div>
      </div>

      {showCreate && (
        <CreateTemplateModal
          onClose={() => setShowCreate(false)}
          onCreated={(id) => {
            setShowCreate(false);
            setSelectedId(id);
            queryClient.invalidateQueries({ queryKey: ["templates"] });
          }}
        />
      )}
    </div>
  );
}

/* ------------------------------------------------------------------ */
/*  Template detail + variant editor                                   */
/* ------------------------------------------------------------------ */

function TemplateDetail({
  template,
  onUpdated,
  onDeleted,
}: {
  template: Template;
  onUpdated: () => void;
  onDeleted: () => void;
}) {
  const [activeLocale, setActiveLocale] = useState("fr");
  const [showEdit, setShowEdit] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);
  const [bodies, setBodies] = useState<Record<string, string>>(() => {
    const map: Record<string, string> = {};
    template.localeVariants?.forEach((v) => {
      map[v.locale] = v.body;
    });
    return map;
  });

  // Reset bodies when template changes
  const [prevId, setPrevId] = useState(template.id);
  if (template.id !== prevId) {
    setPrevId(template.id);
    const map: Record<string, string> = {};
    template.localeVariants?.forEach((v) => {
      map[v.locale] = v.body;
    });
    setBodies(map);
  }

  const saveMutation = useMutation({
    mutationFn: () =>
      api.put(`/templates/${template.id}/variants`, {
        locale: activeLocale,
        body: bodies[activeLocale] ?? "",
      }),
    onSuccess: () => {
      toast.success("Template variant saved");
      onUpdated();
    },
  });

  const deleteVariantMutation = useMutation({
    mutationFn: (locale: string) =>
      api.delete(`/templates/${template.id}/variants/${locale}`),
    onSuccess: () => {
      setBodies((prev) => {
        const next = { ...prev };
        delete next[activeLocale];
        return next;
      });
      toast.success("Variant deleted");
      onUpdated();
    },
  });

  const deleteMutation = useMutation({
    mutationFn: () => api.delete(`/templates/${template.id}`),
    onSuccess: () => {
      toast.success("Template deleted");
      onDeleted();
    },
  });

  const currentBody = bodies[activeLocale] ?? "";
  const segments = Math.ceil(currentBody.length / SMS_SEGMENT_LENGTH) || 0;
  const hasVariant = template.localeVariants?.some((v) => v.locale === activeLocale);

  return (
    <div className="rounded-2xl border border-border bg-warm-white p-5 space-y-4">
      {/* Header */}
      <div className="flex items-start justify-between">
        <div>
          <div className="flex items-center gap-2 mb-1">
            <h2 className="text-[15px] font-semibold text-ink">{template.name}</h2>
            {template.isSystem && (
              <span className="text-[9px] font-semibold text-teal bg-teal-wash px-1.5 py-0.5 rounded-full border border-teal-border uppercase">
                System
              </span>
            )}
          </div>
          {template.description && (
            <p className="text-[12px] text-ink-muted mb-1">{template.description}</p>
          )}
          <p className="text-[11px] text-ink-faint">{template.category}</p>
        </div>
        {!template.isSystem && (
          <div className="flex gap-1.5">
            <button
              onClick={() => setShowEdit(true)}
              className="p-1.5 rounded-lg text-ink-faint hover:text-ink hover:bg-cream-dark/60 transition-colors"
            >
              <Pencil className="w-4 h-4" />
            </button>
            <button
              onClick={() => setShowDeleteConfirm(true)}
              className="p-1.5 rounded-lg text-ink-faint hover:text-red-600 hover:bg-red-50 transition-colors"
            >
              <Trash2 className="w-4 h-4" />
            </button>
          </div>
        )}
      </div>

      {/* Locale tabs */}
      <div className="flex gap-1">
        {locales.map((loc) => {
          const exists = template.localeVariants?.some((v) => v.locale === loc);
          return (
            <button
              key={loc}
              onClick={() => setActiveLocale(loc)}
              className={`px-3 py-1.5 rounded-lg text-[12px] font-medium transition-colors uppercase ${
                activeLocale === loc
                  ? "bg-teal text-white"
                  : "text-ink-muted hover:text-ink hover:bg-cream-dark/60"
              }`}
            >
              {loc}
              {exists && <span className="ml-1 text-[9px] opacity-60">●</span>}
            </button>
          );
        })}
      </div>

      {/* Editor */}
      <textarea
        value={currentBody}
        onChange={(e) =>
          setBodies((prev) => ({ ...prev, [activeLocale]: e.target.value }))
        }
        rows={5}
        className="w-full px-4 py-3 rounded-xl border border-border bg-white text-[13px] text-ink placeholder:text-ink-faint focus:outline-none focus:border-teal transition-colors resize-none"
        placeholder={`Enter ${activeLocale.toUpperCase()} template body...`}
      />

      {/* Footer */}
      <div className="flex items-center justify-between">
        <span className="text-[11px] text-ink-faint">
          {currentBody.length} chars · {segments} segment{segments !== 1 ? "s" : ""}
        </span>
        <div className="flex gap-2">
          {hasVariant && !template.isSystem && (
            <button
              onClick={() => deleteVariantMutation.mutate(activeLocale)}
              disabled={deleteVariantMutation.isPending}
              className="px-3 py-1.5 text-[12px] font-medium text-red-600 hover:bg-red-50 border border-red-200 rounded-lg transition-colors disabled:opacity-50"
            >
              Delete variant
            </button>
          )}
          <button
            onClick={() => saveMutation.mutate()}
            disabled={saveMutation.isPending || !currentBody.trim()}
            className="px-4 py-1.5 bg-teal hover:bg-teal-light text-white text-[12px] font-medium rounded-lg transition-colors disabled:opacity-50"
          >
            {saveMutation.isPending ? "Saving..." : hasVariant ? "Update variant" : "Add variant"}
          </button>
        </div>
      </div>

      {saveMutation.isError && (
        <p className="text-[12px] text-red-600">Failed to save variant.</p>
      )}

      {/* Edit template modal */}
      {showEdit && (
        <EditTemplateModal
          template={template}
          onClose={() => setShowEdit(false)}
          onSaved={() => {
            setShowEdit(false);
            onUpdated();
          }}
        />
      )}

      {/* Delete confirmation */}
      {showDeleteConfirm && (
        <ConfirmModal
          title="Delete template"
          message={`Are you sure you want to delete "${template.name}"? This cannot be undone.`}
          confirmLabel="Delete"
          isLoading={deleteMutation.isPending}
          onConfirm={() => deleteMutation.mutate()}
          onClose={() => setShowDeleteConfirm(false)}
        />
      )}
    </div>
  );
}

/* ------------------------------------------------------------------ */
/*  Create template modal                                              */
/* ------------------------------------------------------------------ */

const createSchema = z.object({
  name: z.string().min(1, "Required"),
  description: z.string().optional(),
  category: z.string().min(1, "Required"),
  initialBody: z.string().optional(),
  initialLocale: z.string().min(1),
});

type CreateTemplateForm = z.infer<typeof createSchema>;

function CreateTemplateModal({
  onClose,
  onCreated,
}: {
  onClose: () => void;
  onCreated: (id: string) => void;
}) {
  const [error, setError] = useState("");

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<CreateTemplateForm>({
    resolver: zodResolver(createSchema),
    defaultValues: { initialLocale: "fr" },
  });

  const onSubmit = async (data: CreateTemplateForm) => {
    try {
      setError("");
      const localeVariants =
        data.initialBody?.trim()
          ? [{ locale: data.initialLocale, body: data.initialBody }]
          : [];
      const res = await api.post("/templates", {
        name: data.name,
        description: data.description || null,
        category: data.category,
        localeVariants,
      });
      toast.success("Template created");
      onCreated(res.data.id);
    } catch {
      setError("Failed to create template.");
    }
  };

  return (
    <ModalShell title="New template" onClose={onClose}>
      <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
        <ErrorBanner message={error} />
        <Field label="Name" error={errors.name?.message}>
          <input
            {...register("name")}
            placeholder="e.g. Appointment Reminder"
            className={inputCls}
          />
        </Field>
        <Field label="Description (optional)">
          <input
            {...register("description")}
            placeholder="What this template is for"
            className={inputCls}
          />
        </Field>
        <Field label="Category" error={errors.category?.message}>
          <select {...register("category")} className={inputCls}>
            <option value="">Select...</option>
            <option value="Reminder">Reminder</option>
            <option value="Confirmation">Confirmation</option>
            <option value="ReviewRequest">Review Request</option>
            <option value="FollowUp">Follow Up</option>
            <option value="Custom">Custom</option>
          </select>
        </Field>
        <div className="grid grid-cols-4 gap-3">
          <div className="col-span-1">
            <Field label="Locale">
              <select {...register("initialLocale")} className={inputCls}>
                <option value="fr">FR</option>
                <option value="en">EN</option>
              </select>
            </Field>
          </div>
          <div className="col-span-3">
            <Field label="Initial body (optional)">
              <input
                {...register("initialBody")}
                placeholder="Template body text..."
                className={inputCls}
              />
            </Field>
          </div>
        </div>
        <ModalActions onClose={onClose} isSubmitting={isSubmitting} submitLabel="Create" />
      </form>
    </ModalShell>
  );
}

/* ------------------------------------------------------------------ */
/*  Edit template modal                                                */
/* ------------------------------------------------------------------ */

const editSchema = z.object({
  name: z.string().min(1).optional(),
  description: z.string().optional(),
  category: z.string().optional(),
});

type EditTemplateForm = z.infer<typeof editSchema>;

function EditTemplateModal({
  template,
  onClose,
  onSaved,
}: {
  template: Template;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [error, setError] = useState("");
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<EditTemplateForm>({
    resolver: zodResolver(editSchema),
    defaultValues: {
      name: template.name,
      description: template.description ?? "",
      category: template.category,
    },
  });

  const onSubmit = async (data: EditTemplateForm) => {
    try {
      setError("");
      await api.put(`/templates/${template.id}`, {
        name: data.name || null,
        description: data.description || null,
        category: data.category || null,
      });
      toast.success("Template updated");
      onSaved();
    } catch {
      setError("Failed to update template.");
    }
  };

  return (
    <ModalShell title="Edit template" onClose={onClose}>
      <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
        <ErrorBanner message={error} />
        <Field label="Name" error={errors.name?.message}>
          <input {...register("name")} className={inputCls} />
        </Field>
        <Field label="Description">
          <input {...register("description")} className={inputCls} />
        </Field>
        <Field label="Category">
          <select {...register("category")} className={inputCls}>
            <option value="Reminder">Reminder</option>
            <option value="Confirmation">Confirmation</option>
            <option value="ReviewRequest">Review Request</option>
            <option value="FollowUp">Follow Up</option>
            <option value="Custom">Custom</option>
          </select>
        </Field>
        <ModalActions onClose={onClose} isSubmitting={isSubmitting} submitLabel="Save changes" />
      </form>
    </ModalShell>
  );
}

/* ------------------------------------------------------------------ */
/*  Shared UI                                                          */
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
      />
    </ModalShell>
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
