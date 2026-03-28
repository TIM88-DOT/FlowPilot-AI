import { useFadeIn } from "../../hooks/useFadeIn";

const testimonials = [
  {
    quote:
      "FlowPilot reduced our no-shows by 45% in the first month. The AI writes better reminders than we ever did — in French and Arabic.",
    name: "Fatima R.",
    role: "Owner",
    business: "Salon Belleza, Algiers",
  },
  {
    quote:
      "Mes clients recoivent un rappel au bon moment, dans leur langue. Je n'ai plus besoin d'appeler chacun manuellement. C'est magique.",
    name: "Karim B.",
    role: "Manager",
    business: "BarberShop DZ, Oran",
  },
  {
    quote:
      "The review recovery feature tripled our Google reviews in two months. Clients love the personalized follow-up after their visit.",
    name: "Dr. Leila M.",
    role: "Director",
    business: "Clinique Riadh, Constantine",
  },
];

export default function Testimonial() {
  const { ref, visible } = useFadeIn();

  return (
    <section
      ref={ref}
      className={`py-28 px-6 fade-in-section ${visible ? "is-visible" : ""}`}
    >
      <div className="max-w-5xl mx-auto">
        <div className="text-center mb-16">
          <p className="text-[12px] text-teal font-semibold tracking-[0.12em] uppercase mb-4">
            Testimonials
          </p>
          <h2 className="font-serif text-[clamp(1.8rem,4vw,2.5rem)] font-medium text-ink tracking-tight">
            Loved by businesses like yours.
          </h2>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-3 gap-5">
          {testimonials.map((t) => (
            <div
              key={t.name}
              className="rounded-[16px] border border-border bg-warm-white p-7 flex flex-col hover:border-border-strong transition-colors duration-300"
            >
              {/* Quote mark */}
              <span className="font-serif text-[48px] text-teal/20 leading-none mb-2">&ldquo;</span>

              <p className="text-[14px] text-ink leading-[1.7] flex-1 mb-8">
                {t.quote}
              </p>

              <div className="flex items-center gap-3 pt-5 border-t border-border">
                <div className="w-9 h-9 rounded-full bg-teal-wash border border-teal-border flex items-center justify-center text-[13px] font-semibold text-teal">
                  {t.name.charAt(0)}
                </div>
                <div>
                  <p className="text-[13px] font-semibold text-ink">{t.name}</p>
                  <p className="text-[11px] text-ink-faint">{t.role} &middot; {t.business}</p>
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
