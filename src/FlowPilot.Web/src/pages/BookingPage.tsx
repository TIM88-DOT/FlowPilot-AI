import { useState } from "react";
import { useParams, Link } from "react-router-dom";
import { useQuery, useMutation } from "@tanstack/react-query";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Clock, MapPin, Phone, ChevronLeft, Check, Calendar, Loader2 } from "lucide-react";
import publicApi from "../lib/publicApi";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

interface PublicServiceDto {
  id: string;
  name: string;
  durationMinutes: number;
  price: number | null;
  currency: string | null;
}

interface DayHours {
  enabled: boolean;
  open: string;
  close: string;
}

interface BusinessHours {
  monday: DayHours;
  tuesday: DayHours;
  wednesday: DayHours;
  thursday: DayHours;
  friday: DayHours;
  saturday: DayHours;
  sunday: DayHours;
}

interface BusinessInfo {
  businessName: string;
  slug: string;
  businessPhone: string | null;
  businessEmail: string | null;
  address: string | null;
  timezone: string | null;
  currency: string;
  businessHours: BusinessHours | null;
  services: PublicServiceDto[];
}

interface TimeSlotDto {
  startTime: string;
  endTime: string;
}

interface BookingConfirmation {
  appointmentId: string;
  serviceName: string;
  startsAt: string;
  endsAt: string;
  businessName: string;
  businessPhone: string | null;
}

// ---------------------------------------------------------------------------
// Zod schema for customer info
// ---------------------------------------------------------------------------

const customerSchema = z.object({
  firstName: z.string().min(1, "First name is required"),
  lastName: z.string().optional(),
  phone: z.string().min(8, "Valid phone number required"),
  email: z.string().email("Invalid email").optional().or(z.literal("")),
  notes: z.string().optional(),
});

type CustomerForm = z.infer<typeof customerSchema>;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

type BookingStep = 1 | 2 | 3 | 4 | "success";

const STEP_LABELS = ["Service", "Date & Time", "Your Info", "Confirm"] as const;

function formatDate(dateStr: string): string {
  const d = new Date(dateStr);
  return d.toLocaleDateString("en-US", { weekday: "long", year: "numeric", month: "long", day: "numeric" });
}

function formatTime(timeStr: string): string {
  const [h, m] = timeStr.split(":");
  const hour = parseInt(h, 10);
  const ampm = hour >= 12 ? "PM" : "AM";
  const displayHour = hour % 12 || 12;
  return `${displayHour}:${m} ${ampm}`;
}

function formatPrice(price: number | null, currency: string | null): string {
  if (price === null || price === undefined) return "";
  const cur = currency || "DZD";
  return new Intl.NumberFormat("en-US", { style: "currency", currency: cur, minimumFractionDigits: 0 }).format(price);
}

function getMinDate(): string {
  const d = new Date();
  d.setDate(d.getDate() + 1);
  return d.toISOString().split("T")[0];
}

function getMaxDate(maxDays: number): string {
  const d = new Date();
  d.setDate(d.getDate() + maxDays);
  return d.toISOString().split("T")[0];
}

const DAY_KEYS: (keyof BusinessHours)[] = [
  "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday",
];

function getDayHours(hours: BusinessHours | null, dateStr: string): DayHours | null {
  if (!hours || !dateStr) return null;
  const dayIndex = new Date(dateStr + "T12:00:00").getDay(); // noon avoids timezone shift
  return hours[DAY_KEYS[dayIndex]];
}

function getOpenDaysSummary(hours: BusinessHours | null): string {
  if (!hours) return "";
  const labels = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
  const keys: (keyof BusinessHours)[] = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];
  const open = keys
    .map((k, i) => (hours[k].enabled ? labels[i] : null))
    .filter(Boolean);
  return open.length > 0 ? open.join(", ") : "No open days configured";
}

// ---------------------------------------------------------------------------
// Main Component
// ---------------------------------------------------------------------------

export default function BookingPage() {
  const { slug } = useParams<{ slug: string }>();
  const [step, setStep] = useState<BookingStep>(1);
  const [selectedService, setSelectedService] = useState<PublicServiceDto | null>(null);
  const [selectedDate, setSelectedDate] = useState("");
  const [selectedSlot, setSelectedSlot] = useState<TimeSlotDto | null>(null);
  const [confirmation, setConfirmation] = useState<BookingConfirmation | null>(null);

  const {
    register,
    handleSubmit,
    getValues,
    formState: { errors },
  } = useForm<CustomerForm>({
    resolver: zodResolver(customerSchema),
    defaultValues: { firstName: "", lastName: "", phone: "", email: "", notes: "" },
  });

  // --- Queries ---

  const { data: business, isLoading: loadingBusiness, error: businessError } = useQuery<BusinessInfo>({
    queryKey: ["public-booking", slug],
    queryFn: () => publicApi.get(`/book/${slug}`).then((r) => r.data),
    enabled: !!slug,
    staleTime: 0, // Always fetch fresh — settings may change between visits
    refetchOnWindowFocus: true,
  });

  const isSelectedDayOpen = (() => {
    const day = getDayHours(business?.businessHours ?? null, selectedDate);
    return !!day?.enabled;
  })();

  const { data: slots, isLoading: loadingSlots } = useQuery<TimeSlotDto[]>({
    queryKey: ["public-slots", slug, selectedDate, selectedService?.id],
    queryFn: () =>
      publicApi
        .get(`/book/${slug}/slots`, { params: { date: selectedDate, serviceId: selectedService!.id } })
        .then((r) => r.data),
    enabled: !!selectedDate && !!selectedService && isSelectedDayOpen,
    staleTime: 0,
  });

  const bookMutation = useMutation({
    mutationFn: (data: CustomerForm) =>
      publicApi
        .post(`/book/${slug}`, {
          firstName: data.firstName,
          lastName: data.lastName || null,
          phone: data.phone,
          email: data.email || null,
          serviceId: selectedService!.id,
          startsAt: `${selectedDate}T${selectedSlot!.startTime}:00`,
          notes: data.notes || null,
        })
        .then((r) => r.data),
    onSuccess: (data: BookingConfirmation) => {
      setConfirmation(data);
      setStep("success");
    },
    onError: (error: { response?: { status?: number } }) => {
      // Slot was taken by someone else — bounce back to pick a new time
      if (error.response?.status === 409) {
        setSelectedSlot(null);
        setStep(2);
      }
    },
  });

  // --- Loading & error states ---

  if (loadingBusiness) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-cream">
        <Loader2 className="w-6 h-6 text-teal animate-spin" />
      </div>
    );
  }

  if (businessError || !business) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-cream px-6">
        <div className="text-center">
          <h1 className="text-[20px] font-bold text-ink mb-2">Business not found</h1>
          <p className="text-[14px] text-ink-muted mb-6">The booking link may be incorrect or the business is no longer available.</p>
          <Link to="/" className="text-[14px] text-teal hover:text-teal-light font-medium">
            Go to homepage
          </Link>
        </div>
      </div>
    );
  }

  // --- Handlers ---

  function handleServiceSelect(service: PublicServiceDto) {
    setSelectedService(service);
    setSelectedDate("");
    setSelectedSlot(null);
    setStep(2);
  }

  function handleSlotSelect(slot: TimeSlotDto) {
    setSelectedSlot(slot);
    setStep(3);
  }

  function handleInfoSubmit(data: CustomerForm) {
    void data;
    setStep(4);
  }

  function handleConfirm() {
    bookMutation.mutate(getValues());
  }

  function goBack() {
    if (step === 2) setStep(1);
    else if (step === 3) setStep(2);
    else if (step === 4) setStep(3);
  }

  // --- Render ---

  return (
    <div className="min-h-screen bg-cream">
      {/* Header */}
      <div className="bg-warm-white border-b border-border">
        <div className="max-w-lg mx-auto px-6 py-5">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-full bg-teal flex items-center justify-center">
              <span className="text-white font-bold text-[16px]">{business.businessName[0]}</span>
            </div>
            <div>
              <h1 className="text-[17px] font-bold text-ink">{business.businessName}</h1>
              {business.address && (
                <p className="text-[12px] text-ink-muted flex items-center gap-1">
                  <MapPin className="w-3 h-3" />
                  {business.address}
                </p>
              )}
            </div>
          </div>
        </div>
      </div>

      {/* Step Indicator */}
      {step !== "success" && (
        <div className="max-w-lg mx-auto px-6 pt-6 pb-2">
          <div className="flex items-center gap-1">
            {STEP_LABELS.map((label, i) => {
              const stepNum = (i + 1) as 1 | 2 | 3 | 4;
              const isActive = step === stepNum;
              const isComplete = typeof step === "number" && step > stepNum;
              return (
                <div key={label} className="flex-1">
                  <div className="flex items-center gap-2 mb-1">
                    <div
                      className={`w-6 h-6 rounded-full flex items-center justify-center text-[11px] font-bold ${
                        isComplete
                          ? "bg-teal text-white"
                          : isActive
                            ? "border-2 border-teal text-teal"
                            : "border border-border text-ink-faint"
                      }`}
                    >
                      {isComplete ? <Check className="w-3.5 h-3.5" /> : stepNum}
                    </div>
                    {i < 3 && (
                      <div className={`flex-1 h-px ${isComplete ? "bg-teal" : "bg-border"}`} />
                    )}
                  </div>
                  <p className={`text-[10px] font-medium ${isActive ? "text-teal" : "text-ink-faint"}`}>
                    {label}
                  </p>
                </div>
              );
            })}
          </div>
        </div>
      )}

      <div className="max-w-lg mx-auto px-6 py-6">
        {/* Step 1: Service Selection */}
        {step === 1 && (
          <div>
            <h2 className="text-[18px] font-bold text-ink mb-1">Choose a service</h2>
            <p className="text-[13px] text-ink-muted mb-5">Select the service you'd like to book.</p>

            {business.services.length === 0 ? (
              <p className="text-[14px] text-ink-muted text-center py-8">No services available at the moment.</p>
            ) : (
              <div className="space-y-3">
                {business.services.map((service) => (
                  <button
                    key={service.id}
                    onClick={() => handleServiceSelect(service)}
                    className={`w-full text-left rounded-2xl border p-4 transition-colors ${
                      selectedService?.id === service.id
                        ? "border-teal bg-teal-wash"
                        : "border-border bg-warm-white hover:border-teal/40"
                    }`}
                  >
                    <div className="flex items-center justify-between">
                      <div>
                        <p className="text-[15px] font-semibold text-ink">{service.name}</p>
                        <p className="text-[13px] text-ink-muted flex items-center gap-1 mt-0.5">
                          <Clock className="w-3.5 h-3.5" />
                          {service.durationMinutes} min
                        </p>
                      </div>
                      {service.price !== null && (
                        <span className="text-[15px] font-bold text-teal">
                          {formatPrice(service.price, service.currency || business.currency)}
                        </span>
                      )}
                    </div>
                  </button>
                ))}
              </div>
            )}
          </div>
        )}

        {/* Step 2: Date & Time */}
        {step === 2 && (
          <div>
            <button onClick={goBack} className="flex items-center gap-1 text-[13px] text-ink-muted hover:text-ink mb-4 transition-colors">
              <ChevronLeft className="w-4 h-4" /> Back
            </button>
            <h2 className="text-[18px] font-bold text-ink mb-1">Pick a date & time</h2>
            <p className="text-[13px] text-ink-muted mb-3">
              {selectedService?.name} — {selectedService?.durationMinutes} min
            </p>

            {business.businessHours && (
              <p className="text-[12px] text-ink-faint mb-4">
                Open: {getOpenDaysSummary(business.businessHours)}
              </p>
            )}

            <div className="mb-5">
              <label className="block text-[13px] font-medium text-ink mb-1.5">
                <Calendar className="w-3.5 h-3.5 inline mr-1" />
                Date
              </label>
              <input
                type="date"
                value={selectedDate}
                onChange={(e) => {
                  setSelectedDate(e.target.value);
                  setSelectedSlot(null);
                }}
                min={getMinDate()}
                max={getMaxDate(60)}
                className="w-full px-4 py-2.5 rounded-xl border border-border bg-warm-white text-[14px] text-ink focus:outline-none focus:border-teal transition-colors"
              />
            </div>

            {selectedDate && (() => {
              const dayInfo = getDayHours(business.businessHours, selectedDate);
              const isClosed = !dayInfo || !dayInfo.enabled;

              if (isClosed) {
                return (
                  <div className="text-[13px] text-amber bg-amber/10 border border-amber/20 rounded-xl px-4 py-3 text-center">
                    We're closed on this day. Please pick another date.
                  </div>
                );
              }

              return (
                <div>
                  <div className="flex items-center justify-between mb-2">
                    <label className="text-[13px] font-medium text-ink">Available times</label>
                    <span className="text-[12px] text-ink-faint">{dayInfo.open} — {dayInfo.close}</span>
                  </div>
                  {loadingSlots ? (
                    <div className="flex items-center justify-center py-8">
                      <Loader2 className="w-5 h-5 text-teal animate-spin" />
                    </div>
                  ) : slots && slots.length > 0 ? (
                    <div className="grid grid-cols-3 gap-2">
                      {slots.map((slot) => (
                        <button
                          key={slot.startTime}
                          onClick={() => handleSlotSelect(slot)}
                          className={`px-3 py-2.5 rounded-xl border text-[13px] font-medium transition-colors ${
                            selectedSlot?.startTime === slot.startTime
                              ? "bg-teal text-white border-teal"
                              : "bg-warm-white text-ink border-border hover:border-teal"
                          }`}
                        >
                          {formatTime(slot.startTime)}
                        </button>
                      ))}
                    </div>
                  ) : (
                    <p className="text-[13px] text-ink-muted text-center py-6">No available times for this date.</p>
                  )}
                </div>
              );
            })()}
          </div>
        )}

        {/* Step 3: Customer Info */}
        {step === 3 && (
          <div>
            <button onClick={goBack} className="flex items-center gap-1 text-[13px] text-ink-muted hover:text-ink mb-4 transition-colors">
              <ChevronLeft className="w-4 h-4" /> Back
            </button>
            <h2 className="text-[18px] font-bold text-ink mb-1">Your information</h2>
            <p className="text-[13px] text-ink-muted mb-5">We'll send you a reminder before your appointment.</p>

            <form onSubmit={handleSubmit(handleInfoSubmit)} className="space-y-4">
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-[13px] font-medium text-ink mb-1.5">First name *</label>
                  <input
                    {...register("firstName")}
                    className="w-full px-4 py-2.5 rounded-xl border border-border bg-warm-white text-[14px] text-ink placeholder:text-ink-faint focus:outline-none focus:border-teal transition-colors"
                    placeholder="John"
                  />
                  {errors.firstName && <p className="text-[12px] text-red-500 mt-1">{errors.firstName.message}</p>}
                </div>
                <div>
                  <label className="block text-[13px] font-medium text-ink mb-1.5">Last name</label>
                  <input
                    {...register("lastName")}
                    className="w-full px-4 py-2.5 rounded-xl border border-border bg-warm-white text-[14px] text-ink placeholder:text-ink-faint focus:outline-none focus:border-teal transition-colors"
                    placeholder="Doe"
                  />
                </div>
              </div>

              <div>
                <label className="block text-[13px] font-medium text-ink mb-1.5">
                  <Phone className="w-3.5 h-3.5 inline mr-1" />
                  Phone *
                </label>
                <input
                  {...register("phone")}
                  type="tel"
                  className="w-full px-4 py-2.5 rounded-xl border border-border bg-warm-white text-[14px] text-ink placeholder:text-ink-faint focus:outline-none focus:border-teal transition-colors"
                  placeholder="+213 555 123 456"
                />
                {errors.phone && <p className="text-[12px] text-red-500 mt-1">{errors.phone.message}</p>}
              </div>

              <div>
                <label className="block text-[13px] font-medium text-ink mb-1.5">Email</label>
                <input
                  {...register("email")}
                  type="email"
                  className="w-full px-4 py-2.5 rounded-xl border border-border bg-warm-white text-[14px] text-ink placeholder:text-ink-faint focus:outline-none focus:border-teal transition-colors"
                  placeholder="john@example.com"
                />
                {errors.email && <p className="text-[12px] text-red-500 mt-1">{errors.email.message}</p>}
              </div>

              <div>
                <label className="block text-[13px] font-medium text-ink mb-1.5">Notes</label>
                <textarea
                  {...register("notes")}
                  rows={2}
                  className="w-full px-4 py-2.5 rounded-xl border border-border bg-warm-white text-[14px] text-ink placeholder:text-ink-faint focus:outline-none focus:border-teal transition-colors resize-none"
                  placeholder="Any special requests..."
                />
              </div>

              <button
                type="submit"
                className="w-full py-2.5 bg-teal hover:bg-teal-light text-white text-[14px] font-medium rounded-xl transition-colors"
              >
                Continue
              </button>
            </form>
          </div>
        )}

        {/* Step 4: Confirm */}
        {step === 4 && (
          <div>
            <button onClick={goBack} className="flex items-center gap-1 text-[13px] text-ink-muted hover:text-ink mb-4 transition-colors">
              <ChevronLeft className="w-4 h-4" /> Back
            </button>
            <h2 className="text-[18px] font-bold text-ink mb-5">Confirm your booking</h2>

            <div className="rounded-2xl border border-border bg-warm-white p-5 space-y-4 mb-6">
              <SummaryRow label="Service" value={selectedService?.name ?? ""} />
              <SummaryRow label="Date" value={selectedDate ? formatDate(selectedDate) : ""} />
              <SummaryRow label="Time" value={selectedSlot ? `${formatTime(selectedSlot.startTime)} — ${formatTime(selectedSlot.endTime)}` : ""} />
              <SummaryRow label="Duration" value={`${selectedService?.durationMinutes} min`} />
              {selectedService?.price !== null && selectedService?.price !== undefined && (
                <SummaryRow label="Price" value={formatPrice(selectedService.price, selectedService.currency || business.currency)} />
              )}
              <div className="border-t border-border pt-3">
                <SummaryRow label="Name" value={`${getValues("firstName")} ${getValues("lastName") || ""}`.trim()} />
                <SummaryRow label="Phone" value={getValues("phone")} />
                {getValues("email") && <SummaryRow label="Email" value={getValues("email") ?? ""} />}
                {getValues("notes") && <SummaryRow label="Notes" value={getValues("notes") ?? ""} />}
              </div>
            </div>

            {bookMutation.isError && (
              <div className="text-[13px] text-red-600 bg-red-50 border border-red-200 rounded-xl px-4 py-2.5 mb-4">
                {(bookMutation.error as { response?: { data?: { detail?: string } } })?.response?.data?.detail ||
                  "Something went wrong. Please try again."}
              </div>
            )}

            <button
              onClick={handleConfirm}
              disabled={bookMutation.isPending}
              className="w-full py-3 bg-teal hover:bg-teal-light text-white text-[15px] font-semibold rounded-xl transition-colors disabled:opacity-50 flex items-center justify-center gap-2"
            >
              {bookMutation.isPending ? (
                <>
                  <Loader2 className="w-4 h-4 animate-spin" />
                  Booking...
                </>
              ) : (
                "Confirm Booking"
              )}
            </button>
          </div>
        )}

        {/* Success Screen */}
        {step === "success" && confirmation && (
          <div className="text-center py-6">
            <div className="w-16 h-16 rounded-full bg-teal/10 flex items-center justify-center mx-auto mb-5">
              <Check className="w-8 h-8 text-teal" />
            </div>
            <h2 className="text-[22px] font-bold text-ink mb-2">Booking Confirmed!</h2>
            <p className="text-[14px] text-ink-muted mb-6">You'll receive a reminder SMS before your appointment.</p>

            <div className="rounded-2xl border border-border bg-warm-white p-5 text-left space-y-3 mb-6">
              <SummaryRow label="Service" value={confirmation.serviceName} />
              <SummaryRow label="Date" value={formatDate(confirmation.startsAt)} />
              <SummaryRow
                label="Time"
                value={`${new Date(confirmation.startsAt).toLocaleTimeString("en-US", { hour: "numeric", minute: "2-digit" })} — ${new Date(confirmation.endsAt).toLocaleTimeString("en-US", { hour: "numeric", minute: "2-digit" })}`}
              />
              <SummaryRow label="Business" value={confirmation.businessName} />
              {confirmation.businessPhone && <SummaryRow label="Contact" value={confirmation.businessPhone} />}
            </div>

            <Link
              to={`/book/${slug}`}
              onClick={() => window.location.reload()}
              className="text-[14px] text-teal hover:text-teal-light font-medium"
            >
              Book another appointment
            </Link>
          </div>
        )}
      </div>

      {/* Footer */}
      <div className="max-w-lg mx-auto px-6 pb-8">
        <p className="text-[11px] text-ink-faint text-center">
          Powered by <span className="font-medium">FlowPilot</span>
        </p>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Sub-components
// ---------------------------------------------------------------------------

function SummaryRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between items-start">
      <span className="text-[13px] text-ink-muted">{label}</span>
      <span className="text-[13px] font-medium text-ink text-right max-w-[60%]">{value}</span>
    </div>
  );
}
