import { useFadeIn } from "../../hooks/useFadeIn";

const testimonials = [
  {
    quote:
      "Relora reduced our no-shows by 45% in the first month. The AI writes better reminders than we ever did — in both French and English.",
    name: "Emily R.",
    role: "Owner",
    business: "Belleza Salon, Toronto",
  },
  {
    quote:
      "Mes clients reçoivent un rappel au bon moment, dans leur langue. Je n'ai plus besoin d'appeler chacun manuellement. C'est magique.",
    name: "Karim B.",
    role: "Manager",
    business: "Studio Coupe, Montréal",
  },
  {
    quote:
      "The review recovery feature tripled our Google reviews in two months. Clients love the personalized follow-up after their visit.",
    name: "Dr. Lauren M.",
    role: "Director",
    business: "Riverside Clinic, Vancouver",
  },
];

export default function Testimonial() {
  const { ref, visible } = useFadeIn();

  return (
    <section
      ref={ref}
      className={`py-24 px-6 md:px-8 fade-in-section ${visible ? "is-visible" : ""}`}
    >
      <div className="max-w-[1200px] mx-auto">
        <div className="text-center mb-16">
          <p className="font-mono text-[12px] font-medium text-brand tracking-[0.6px] uppercase mb-4">
            Testimonials
          </p>
          <h2
            className="text-[clamp(1.8rem,4vw,2.5rem)] font-semibold text-[#0d0d0d] leading-[1.1]"
            style={{ letterSpacing: "-0.8px" }}
          >
            Loved by businesses like yours.
          </h2>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-3 gap-5">
          {testimonials.map((t) => (
            <div
              key={t.name}
              className="rounded-2xl border border-[rgba(0,0,0,0.05)] bg-white p-8 flex flex-col hover:border-[rgba(0,0,0,0.08)] transition-colors duration-300 shadow-[rgba(0,0,0,0.03)_0px_2px_4px]"
            >
              {/* Quote mark */}
              <span className="text-[48px] text-brand/20 leading-none mb-2 select-none">
                &ldquo;
              </span>

              <p className="text-[14px] text-[#333333] leading-[1.7] flex-1 mb-8">
                {t.quote}
              </p>

              <div className="flex items-center gap-3 pt-5 border-t border-[rgba(0,0,0,0.05)]">
                <div className="w-9 h-9 rounded-full bg-[#d4fae8] flex items-center justify-center text-[13px] font-semibold text-[#0fa76e]">
                  {t.name.charAt(0)}
                </div>
                <div>
                  <p className="text-[13px] font-semibold text-[#0d0d0d]">
                    {t.name}
                  </p>
                  <p className="text-[12px] text-[#888888]">
                    {t.role} &middot; {t.business}
                  </p>
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
