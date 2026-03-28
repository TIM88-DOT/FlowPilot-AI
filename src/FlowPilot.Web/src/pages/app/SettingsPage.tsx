import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Plus, X, Trash2, GripVertical } from "lucide-react";
import api from "../../lib/api";

/* ------------------------------------------------------------------ */
/*  Settings page — tabbed layout                                      */
/* ------------------------------------------------------------------ */

const tabs = [
  { id: "business", label: "Business" },
  { id: "services", label: "Services" },
  { id: "hours", label: "Business Hours" },
  { id: "notifications", label: "Notifications" },
  { id: "reviews", label: "Reviews" },
  { id: "booking", label: "Booking" },
] as const;

type TabId = (typeof tabs)[number]["id"];

export default function SettingsPage() {
  const [activeTab, setActiveTab] = useState<TabId>("business");

  return (
    <div>
      <div className="mb-6">
        <h1 className="text-[20px] font-bold text-ink">Settings</h1>
        <p className="text-[13px] text-ink-muted mt-1">Configure your business</p>
      </div>

      {/* Tab nav */}
      <div className="flex gap-1 mb-6 overflow-x-auto border-b border-border">
        {tabs.map((tab) => (
          <button
            key={tab.id}
            onClick={() => setActiveTab(tab.id)}
            className={`px-4 py-2.5 text-[13px] font-medium transition-colors whitespace-nowrap border-b-2 -mb-px ${
              activeTab === tab.id
                ? "border-teal text-teal"
                : "border-transparent text-ink-muted hover:text-ink"
            }`}
          >
            {tab.label}
          </button>
        ))}
      </div>

      <div className="max-w-2xl">
        {activeTab === "business" && <BusinessSettings />}
        {activeTab === "services" && <ServicesSettings />}
        {activeTab === "hours" && <BusinessHoursSettings />}
        {activeTab === "notifications" && <NotificationSettings />}
        {activeTab === "reviews" && <ReviewSettings />}
        {activeTab === "booking" && <BookingSettings />}
      </div>
    </div>
  );
}

/* ------------------------------------------------------------------ */
/*  Business info                                                      */
/* ------------------------------------------------------------------ */

function BusinessSettings() {
  const [saved, setSaved] = useState(false);
  const [businessName, setBusinessName] = useState("");
  const [phone, setPhone] = useState("");
  const [email, setEmail] = useState("");
  const [address, setAddress] = useState("");
  const [timezone, setTimezone] = useState("Africa/Algiers");
  const [defaultLanguage, setDefaultLanguage] = useState("fr");
  const [currency, setCurrency] = useState("DZD");

  const handleSave = () => {
    // TODO: wire to backend tenant settings endpoint
    setSaved(true);
    setTimeout(() => setSaved(false), 2000);
  };

  return (
    <Card title="Business Information" description="General information about your business.">
      <div className="space-y-4">
        <div className="grid grid-cols-2 gap-4">
          <FieldBlock label="Business name">
            <input value={businessName} onChange={(e) => setBusinessName(e.target.value)} placeholder="Salon Belleza" className={inputCls} />
          </FieldBlock>
          <FieldBlock label="Phone">
            <input value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="+213551234567" className={inputCls} />
          </FieldBlock>
        </div>
        <FieldBlock label="Email">
          <input value={email} onChange={(e) => setEmail(e.target.value)} placeholder="contact@salonbelleza.com" className={inputCls} />
        </FieldBlock>
        <FieldBlock label="Address">
          <input value={address} onChange={(e) => setAddress(e.target.value)} placeholder="123 Rue Didouche Mourad, Algiers" className={inputCls} />
        </FieldBlock>
        <div className="grid grid-cols-3 gap-4">
          <FieldBlock label="Timezone">
            <select value={timezone} onChange={(e) => setTimezone(e.target.value)} className={inputCls}>
              <option value="Africa/Algiers">Africa/Algiers (UTC+1)</option>
              <option value="Africa/Tunis">Africa/Tunis (UTC+1)</option>
              <option value="Africa/Casablanca">Africa/Casablanca (UTC+1)</option>
              <option value="Europe/Paris">Europe/Paris (UTC+1/+2)</option>
              <option value="Europe/London">Europe/London (UTC+0/+1)</option>
              <option value="America/New_York">America/New_York (UTC-5)</option>
            </select>
          </FieldBlock>
          <FieldBlock label="Default language">
            <select value={defaultLanguage} onChange={(e) => setDefaultLanguage(e.target.value)} className={inputCls}>
              <option value="fr">French</option>
              <option value="ar">Arabic</option>
              <option value="en">English</option>
            </select>
          </FieldBlock>
          <FieldBlock label="Currency">
            <select value={currency} onChange={(e) => setCurrency(e.target.value)} className={inputCls}>
              <option value="DZD">DZD (Dinar)</option>
              <option value="EUR">EUR (Euro)</option>
              <option value="USD">USD (Dollar)</option>
              <option value="MAD">MAD (Dirham)</option>
              <option value="TND">TND (Dinar)</option>
              <option value="GBP">GBP (Pound)</option>
            </select>
          </FieldBlock>
        </div>
        <SaveButton onSave={handleSave} saved={saved} />
      </div>
    </Card>
  );
}

/* ------------------------------------------------------------------ */
/*  Services                                                           */
/* ------------------------------------------------------------------ */

export interface ServiceDto {
  id: string;
  name: string;
  durationMinutes: number;
  price: number | null;
  currency: string | null;
  isActive: boolean;
  sortOrder: number;
  createdAt: string;
  updatedAt: string;
}

function ServicesSettings() {
  const queryClient = useQueryClient();
  const [newName, setNewName] = useState("");
  const [newDuration, setNewDuration] = useState(30);
  const [newPrice, setNewPrice] = useState("");
  const [error, setError] = useState("");

  const { data: services = [], isLoading } = useQuery<ServiceDto[]>({
    queryKey: ["services"],
    queryFn: () => api.get("/services").then((r) => r.data),
  });

  const createMutation = useMutation({
    mutationFn: (data: { name: string; durationMinutes: number; price?: number }) =>
      api.post("/services", data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["services"] });
      setNewName("");
      setNewDuration(30);
      setNewPrice("");
      setError("");
    },
    onError: () => setError("Failed to create service. Name may already exist."),
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, ...data }: { id: string; isActive?: boolean; name?: string; durationMinutes?: number; price?: number }) =>
      api.put(`/services/${id}`, data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["services"] }),
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => api.delete(`/services/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["services"] }),
  });

  const handleAdd = () => {
    if (!newName.trim()) return;
    const payload: { name: string; durationMinutes: number; price?: number } = {
      name: newName.trim(),
      durationMinutes: newDuration,
    };
    if (newPrice) payload.price = Number(newPrice);
    createMutation.mutate(payload);
  };

  return (
    <Card
      title="Services"
      description="Define the services you offer. These will appear in the appointment creation form."
    >
      <div className="space-y-3">
        {isLoading ? (
          <p className="text-[13px] text-ink-faint py-4 text-center">Loading services...</p>
        ) : services.length === 0 ? (
          <p className="text-[13px] text-ink-faint py-4 text-center">No services yet. Add one below.</p>
        ) : (
          services.map((s) => (
            <div
              key={s.id}
              className="flex items-center gap-3 p-3 rounded-xl border border-border bg-white"
            >
              <GripVertical className="w-4 h-4 text-ink-faint shrink-0 cursor-grab" />
              <p className="flex-1 min-w-0 text-[13px] text-ink font-medium truncate">{s.name}</p>
              <span className="text-[12px] text-ink-muted">{s.durationMinutes}min</span>
              {s.price !== null && (
                <span className="text-[12px] text-ink-muted">{s.price}{s.currency ? ` ${s.currency}` : ""}</span>
              )}
              <label className="flex items-center gap-1.5 shrink-0 cursor-pointer">
                <input
                  type="checkbox"
                  checked={s.isActive}
                  onChange={(e) => updateMutation.mutate({ id: s.id, isActive: e.target.checked })}
                  className="rounded accent-teal"
                />
                <span className="text-[11px] text-ink-faint">Active</span>
              </label>
              <button
                onClick={() => deleteMutation.mutate(s.id)}
                disabled={deleteMutation.isPending}
                className="p-1 text-ink-faint hover:text-red-600 transition-colors shrink-0"
              >
                <Trash2 className="w-4 h-4" />
              </button>
            </div>
          ))
        )}

        {/* Add new service inline */}
        {error && (
          <p className="text-[12px] text-red-600 bg-red-50 border border-red-200 rounded-lg px-3 py-2">{error}</p>
        )}
        <div className="flex items-center gap-3 p-3 rounded-xl border border-dashed border-teal-border bg-teal-wash/30">
          <input
            value={newName}
            onChange={(e) => setNewName(e.target.value)}
            placeholder="Service name"
            className="flex-1 min-w-0 px-3 py-2 rounded-lg border border-border bg-white text-[13px] text-ink placeholder:text-ink-faint focus:outline-none focus:border-teal transition-colors"
            onKeyDown={(e) => e.key === "Enter" && handleAdd()}
          />
          <div className="flex items-center gap-1">
            <input
              type="number"
              value={newDuration}
              onChange={(e) => setNewDuration(Number(e.target.value))}
              className="w-16 px-2 py-2 rounded-lg border border-border bg-white text-[13px] text-ink text-center focus:outline-none focus:border-teal transition-colors"
            />
            <span className="text-[11px] text-ink-faint">min</span>
          </div>
          <input
            value={newPrice}
            onChange={(e) => setNewPrice(e.target.value)}
            placeholder="Price"
            className="w-24 px-3 py-2 rounded-lg border border-border bg-white text-[13px] text-ink placeholder:text-ink-faint focus:outline-none focus:border-teal transition-colors"
            onKeyDown={(e) => e.key === "Enter" && handleAdd()}
          />
          <button
            onClick={handleAdd}
            disabled={createMutation.isPending || !newName.trim()}
            className="flex items-center gap-1.5 px-3 py-2 text-[13px] font-medium text-white bg-teal hover:bg-teal-light rounded-lg transition-colors disabled:opacity-50"
          >
            <Plus className="w-4 h-4" />
            Add
          </button>
        </div>
      </div>
    </Card>
  );
}

/* ------------------------------------------------------------------ */
/*  Business hours                                                     */
/* ------------------------------------------------------------------ */

const daysOfWeek = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

interface DayHours {
  enabled: boolean;
  open: string;
  close: string;
}

function BusinessHoursSettings() {
  const [hours, setHours] = useState<Record<string, DayHours>>(() => {
    const map: Record<string, DayHours> = {};
    daysOfWeek.forEach((day, i) => {
      map[day] = {
        enabled: i < 6, // Mon-Sat on, Sun off
        open: "09:00",
        close: i === 5 ? "13:00" : "18:00",
      };
    });
    return map;
  });

  const [saved, setSaved] = useState(false);
  const handleSave = () => {
    setSaved(true);
    setTimeout(() => setSaved(false), 2000);
  };

  const updateDay = (day: string, field: keyof DayHours, value: string | boolean) => {
    setHours((prev) => ({
      ...prev,
      [day]: { ...prev[day], [field]: value },
    }));
  };

  return (
    <Card title="Business Hours" description="Set your operating hours. Reminders will only be sent during these times.">
      <div className="space-y-2">
        {daysOfWeek.map((day) => {
          const h = hours[day];
          return (
            <div
              key={day}
              className={`flex items-center gap-4 px-4 py-3 rounded-xl border transition-colors ${
                h.enabled ? "border-border bg-white" : "border-border/50 bg-cream-dark/20"
              }`}
            >
              <label className="flex items-center gap-2.5 w-28 shrink-0 cursor-pointer">
                <input
                  type="checkbox"
                  checked={h.enabled}
                  onChange={(e) => updateDay(day, "enabled", e.target.checked)}
                  className="rounded accent-teal"
                />
                <span className={`text-[13px] font-medium ${h.enabled ? "text-ink" : "text-ink-faint"}`}>
                  {day.slice(0, 3)}
                </span>
              </label>
              {h.enabled ? (
                <div className="flex items-center gap-2">
                  <input
                    type="time"
                    value={h.open}
                    onChange={(e) => updateDay(day, "open", e.target.value)}
                    className="px-3 py-1.5 rounded-lg border border-border bg-warm-white text-[13px] text-ink focus:outline-none focus:border-teal transition-colors"
                  />
                  <span className="text-[12px] text-ink-faint">to</span>
                  <input
                    type="time"
                    value={h.close}
                    onChange={(e) => updateDay(day, "close", e.target.value)}
                    className="px-3 py-1.5 rounded-lg border border-border bg-warm-white text-[13px] text-ink focus:outline-none focus:border-teal transition-colors"
                  />
                </div>
              ) : (
                <span className="text-[12px] text-ink-faint">Closed</span>
              )}
            </div>
          );
        })}
        <SaveButton onSave={handleSave} saved={saved} />
      </div>
    </Card>
  );
}

/* ------------------------------------------------------------------ */
/*  Notification settings                                              */
/* ------------------------------------------------------------------ */

function NotificationSettings() {
  const [reminderTiming, setReminderTiming] = useState("24");
  const [secondReminder, setSecondReminder] = useState(true);
  const [secondReminderTiming, setSecondReminderTiming] = useState("2");
  const [confirmationEnabled, setConfirmationEnabled] = useState(true);
  const [noShowFollowUp, setNoShowFollowUp] = useState(true);
  const [smsSignature, setSmsSignature] = useState("");
  const [saved, setSaved] = useState(false);

  const handleSave = () => {
    setSaved(true);
    setTimeout(() => setSaved(false), 2000);
  };

  return (
    <Card title="Notifications" description="Configure when and how your customers receive messages.">
      <div className="space-y-5">
        <FieldBlock label="First reminder (hours before appointment)">
          <select value={reminderTiming} onChange={(e) => setReminderTiming(e.target.value)} className={inputCls}>
            <option value="48">48 hours</option>
            <option value="24">24 hours</option>
            <option value="12">12 hours</option>
            <option value="6">6 hours</option>
            <option value="3">3 hours</option>
            <option value="1">1 hour</option>
          </select>
        </FieldBlock>

        <Toggle
          label="Send a second reminder"
          description="Send a shorter follow-up closer to the appointment."
          checked={secondReminder}
          onChange={setSecondReminder}
        />

        {secondReminder && (
          <FieldBlock label="Second reminder (hours before)">
            <select value={secondReminderTiming} onChange={(e) => setSecondReminderTiming(e.target.value)} className={inputCls}>
              <option value="4">4 hours</option>
              <option value="3">3 hours</option>
              <option value="2">2 hours</option>
              <option value="1">1 hour</option>
              <option value="0.5">30 minutes</option>
            </select>
          </FieldBlock>
        )}

        <Toggle
          label="Auto-confirm replies"
          description='Automatically confirm appointments when the customer replies "yes" or equivalent.'
          checked={confirmationEnabled}
          onChange={setConfirmationEnabled}
        />

        <Toggle
          label="No-show follow-up"
          description="Send a message after a no-show to offer rebooking."
          checked={noShowFollowUp}
          onChange={setNoShowFollowUp}
        />

        <FieldBlock label="SMS signature (appended to all messages)">
          <input
            value={smsSignature}
            onChange={(e) => setSmsSignature(e.target.value)}
            placeholder="— Salon Belleza"
            className={inputCls}
          />
          <p className="text-[11px] text-ink-faint mt-1">
            Leave empty to let AI decide the sign-off.
          </p>
        </FieldBlock>

        <SaveButton onSave={handleSave} saved={saved} />
      </div>
    </Card>
  );
}

/* ------------------------------------------------------------------ */
/*  Review settings                                                    */
/* ------------------------------------------------------------------ */

function ReviewSettings() {
  const [googlePlaceId, setGooglePlaceId] = useState("");
  const [facebookUrl, setFacebookUrl] = useState("");
  const [reviewDelay, setReviewDelay] = useState("2");
  const [reviewCooldown, setReviewCooldown] = useState("30");
  const [autoSend, setAutoSend] = useState(true);
  const [saved, setSaved] = useState(false);

  const handleSave = () => {
    setSaved(true);
    setTimeout(() => setSaved(false), 2000);
  };

  return (
    <Card title="Review Platforms" description="Configure where and when FlowPilot sends review requests.">
      <div className="space-y-5">
        <FieldBlock label="Google Place ID">
          <input
            value={googlePlaceId}
            onChange={(e) => setGooglePlaceId(e.target.value)}
            placeholder="ChIJ..."
            className={inputCls}
          />
          {googlePlaceId && (
            <p className="text-[11px] text-teal mt-1">
              Preview: search.google.com/local/reviews?placeid={googlePlaceId}
            </p>
          )}
        </FieldBlock>

        <FieldBlock label="Facebook Page URL">
          <input
            value={facebookUrl}
            onChange={(e) => setFacebookUrl(e.target.value)}
            placeholder="https://facebook.com/your-page/reviews"
            className={inputCls}
          />
        </FieldBlock>

        <Toggle
          label="Auto-send review requests"
          description="Automatically send a review request after a completed appointment."
          checked={autoSend}
          onChange={setAutoSend}
        />

        <div className="grid grid-cols-2 gap-4">
          <FieldBlock label="Send review after (hours)">
            <select value={reviewDelay} onChange={(e) => setReviewDelay(e.target.value)} className={inputCls}>
              <option value="1">1 hour</option>
              <option value="2">2 hours</option>
              <option value="4">4 hours</option>
              <option value="24">24 hours</option>
            </select>
          </FieldBlock>
          <FieldBlock label="Cooldown between reviews (days)">
            <select value={reviewCooldown} onChange={(e) => setReviewCooldown(e.target.value)} className={inputCls}>
              <option value="14">14 days</option>
              <option value="30">30 days</option>
              <option value="60">60 days</option>
              <option value="90">90 days</option>
            </select>
          </FieldBlock>
        </div>

        <SaveButton onSave={handleSave} saved={saved} />
      </div>
    </Card>
  );
}

/* ------------------------------------------------------------------ */
/*  Booking settings                                                   */
/* ------------------------------------------------------------------ */

function BookingSettings() {
  const [bufferTime, setBufferTime] = useState("15");
  const [maxAdvance, setMaxAdvance] = useState("30");
  const [minAdvance, setMinAdvance] = useState("2");
  const [allowCancel, setAllowCancel] = useState(true);
  const [cancelBefore, setCancelBefore] = useState("24");
  const [allowReschedule, setAllowReschedule] = useState(true);
  const [rescheduleBefore, setRescheduleBefore] = useState("12");
  const [saved, setSaved] = useState(false);

  const handleSave = () => {
    setSaved(true);
    setTimeout(() => setSaved(false), 2000);
  };

  return (
    <Card title="Booking Rules" description="Control how appointments are booked and managed.">
      <div className="space-y-5">
        <div className="grid grid-cols-3 gap-4">
          <FieldBlock label="Buffer between appointments">
            <select value={bufferTime} onChange={(e) => setBufferTime(e.target.value)} className={inputCls}>
              <option value="0">None</option>
              <option value="5">5 min</option>
              <option value="10">10 min</option>
              <option value="15">15 min</option>
              <option value="30">30 min</option>
            </select>
          </FieldBlock>
          <FieldBlock label="Max advance booking (days)">
            <select value={maxAdvance} onChange={(e) => setMaxAdvance(e.target.value)} className={inputCls}>
              <option value="7">7 days</option>
              <option value="14">14 days</option>
              <option value="30">30 days</option>
              <option value="60">60 days</option>
              <option value="90">90 days</option>
            </select>
          </FieldBlock>
          <FieldBlock label="Min advance booking (hours)">
            <select value={minAdvance} onChange={(e) => setMinAdvance(e.target.value)} className={inputCls}>
              <option value="0">None</option>
              <option value="1">1 hour</option>
              <option value="2">2 hours</option>
              <option value="4">4 hours</option>
              <option value="24">24 hours</option>
            </select>
          </FieldBlock>
        </div>

        <div className="h-px bg-border" />

        <Toggle
          label="Allow customer cancellation"
          description="Let customers cancel via SMS reply."
          checked={allowCancel}
          onChange={setAllowCancel}
        />
        {allowCancel && (
          <FieldBlock label="Cancel allowed up to (hours before)">
            <select value={cancelBefore} onChange={(e) => setCancelBefore(e.target.value)} className={inputCls}>
              <option value="1">1 hour</option>
              <option value="2">2 hours</option>
              <option value="4">4 hours</option>
              <option value="12">12 hours</option>
              <option value="24">24 hours</option>
              <option value="48">48 hours</option>
            </select>
          </FieldBlock>
        )}

        <Toggle
          label="Allow customer reschedule"
          description="Let customers request a reschedule via SMS reply."
          checked={allowReschedule}
          onChange={setAllowReschedule}
        />
        {allowReschedule && (
          <FieldBlock label="Reschedule allowed up to (hours before)">
            <select value={rescheduleBefore} onChange={(e) => setRescheduleBefore(e.target.value)} className={inputCls}>
              <option value="1">1 hour</option>
              <option value="2">2 hours</option>
              <option value="4">4 hours</option>
              <option value="12">12 hours</option>
              <option value="24">24 hours</option>
            </select>
          </FieldBlock>
        )}

        <SaveButton onSave={handleSave} saved={saved} />
      </div>
    </Card>
  );
}

/* ------------------------------------------------------------------ */
/*  Shared UI                                                          */
/* ------------------------------------------------------------------ */

const inputCls =
  "w-full px-4 py-2.5 rounded-xl border border-border bg-white text-[13px] text-ink placeholder:text-ink-faint focus:outline-none focus:border-teal transition-colors";

function Card({
  title,
  description,
  children,
}: {
  title: string;
  description: string;
  children: React.ReactNode;
}) {
  return (
    <div className="rounded-2xl border border-border bg-warm-white p-6">
      <h2 className="text-[15px] font-semibold text-ink mb-1">{title}</h2>
      <p className="text-[12px] text-ink-faint mb-5">{description}</p>
      {children}
    </div>
  );
}

function FieldBlock({
  label,
  children,
}: {
  label: string;
  children: React.ReactNode;
}) {
  return (
    <div>
      <label className="block text-[13px] font-medium text-ink mb-1.5">{label}</label>
      {children}
    </div>
  );
}

function Toggle({
  label,
  description,
  checked,
  onChange,
}: {
  label: string;
  description: string;
  checked: boolean;
  onChange: (v: boolean) => void;
}) {
  return (
    <div className="flex items-start justify-between gap-4 py-1">
      <div>
        <p className="text-[13px] font-medium text-ink">{label}</p>
        <p className="text-[12px] text-ink-faint">{description}</p>
      </div>
      <button
        onClick={() => onChange(!checked)}
        className={`relative w-10 h-6 rounded-full shrink-0 transition-colors ${
          checked ? "bg-teal" : "bg-ink/10"
        }`}
      >
        <span
          className={`absolute top-0.5 left-0.5 w-5 h-5 bg-white rounded-full shadow transition-transform ${
            checked ? "translate-x-4" : ""
          }`}
        />
      </button>
    </div>
  );
}

function SaveButton({ onSave, saved }: { onSave: () => void; saved: boolean }) {
  return (
    <div className="flex items-center gap-3 pt-3">
      <button
        onClick={onSave}
        className="px-5 py-2.5 bg-teal hover:bg-teal-light text-white text-[13px] font-medium rounded-xl transition-colors"
      >
        Save changes
      </button>
      {saved && <span className="text-[12px] text-teal font-medium">Saved!</span>}
    </div>
  );
}
