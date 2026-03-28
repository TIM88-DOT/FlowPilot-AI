import { Check } from "lucide-react";
import { useFadeIn } from "../../hooks/useFadeIn";

interface PlanProps {
  name: string;
  price: string;
  period?: string;
  description: string;
  features: string[];
  cta: string;
  highlighted?: boolean;
}

const plans: PlanProps[] = [
  {
    name: "Starter",
    price: "$29",
    period: "/mo",
    description: "Solo practitioners getting started.",
    features: [
      "200 SMS / month",
      "1 staff user",
      "Smart reminders",
      "Appointment management",
    ],
    cta: "Start free trial",
  },
  {
    name: "Pro",
    price: "$79",
    period: "/mo",
    description: "The full AI suite for growing businesses.",
    features: [
      "1,000 SMS / month",
      "5 staff users",
      "Reply AI + auto-confirm",
      "Review recovery",
      "Full analytics",
      "Multilingual templates",
    ],
    cta: "Start free trial",
    highlighted: true,
  },
  {
    name: "Enterprise",
    price: "Custom",
    description: "Multi-location with custom needs.",
    features: [
      "Unlimited SMS & users",
      "Everything in Pro",
      "API access + webhooks",
      "Custom integrations",
      "Dedicated account manager",
    ],
    cta: "Contact sales",
  },
];

function PlanCard({ name, price, period, description, features, cta, highlighted }: PlanProps) {
  return (
    <div
      className={`relative rounded-2xl p-6 flex flex-col transition-all duration-200 ${
        highlighted
          ? "bg-teal text-white ring-1 ring-teal"
          : "bg-warm-white border border-border hover:border-border-strong"
      }`}
    >
      {highlighted && (
        <span className="absolute -top-2.5 left-5 px-2.5 py-0.5 bg-amber text-white text-[10px] font-semibold rounded-full uppercase tracking-wider">
          Popular
        </span>
      )}

      <div className="mb-5">
        <h3 className={`text-[14px] font-semibold mb-0.5 ${highlighted ? "text-white" : "text-ink"}`}>
          {name}
        </h3>
        <p className={`text-[12px] mb-4 ${highlighted ? "text-white/60" : "text-ink-muted"}`}>
          {description}
        </p>
        <div className="flex items-baseline gap-0.5">
          <span className={`text-[2.2rem] font-bold leading-none ${highlighted ? "text-white" : "text-ink"}`}>
            {price}
          </span>
          {period && (
            <span className={`text-[12px] ${highlighted ? "text-white/40" : "text-ink-faint"}`}>
              {period}
            </span>
          )}
        </div>
      </div>

      <div className={`h-px mb-5 ${highlighted ? "bg-white/15" : "bg-border"}`} />

      <ul className="space-y-2.5 mb-6 flex-1">
        {features.map((feature) => (
          <li key={feature} className="flex items-center gap-2">
            <Check className={`w-3.5 h-3.5 shrink-0 ${highlighted ? "text-white/60" : "text-teal"}`} strokeWidth={2.5} />
            <span className={`text-[13px] ${highlighted ? "text-white/80" : "text-ink-muted"}`}>
              {feature}
            </span>
          </li>
        ))}
      </ul>

      <a
        href="#contact"
        className={`block text-center py-2.5 rounded-full text-[13px] font-medium transition-all ${
          highlighted
            ? "bg-white text-teal hover:bg-cream"
            : "text-ink border border-border hover:border-border-strong"
        }`}
      >
        {cta}
      </a>
    </div>
  );
}

export default function Pricing() {
  const { ref, visible } = useFadeIn();

  return (
    <section
      id="pricing"
      ref={ref}
      className={`py-20 px-6 fade-in-section ${visible ? "is-visible" : ""}`}
    >
      <div className="max-w-4xl mx-auto">
        <div className="text-center mb-12">
          <p className="text-[11px] text-teal font-semibold tracking-[0.12em] uppercase mb-3">
            Pricing
          </p>
          <h2 className="text-[clamp(1.6rem,3.5vw,2.2rem)] font-bold text-ink tracking-tight mb-2">
            Simple, transparent pricing.
          </h2>
          <p className="text-[14px] text-ink-muted">
            14-day free trial &middot; No credit card required
          </p>
        </div>

        <div className="relative">
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4 items-start blur-[6px] select-none pointer-events-none">
            {plans.map((plan) => (
              <PlanCard key={plan.name} {...plan} />
            ))}
          </div>
          <div className="absolute inset-0 flex items-center justify-center">
            <span className="px-5 py-2.5 bg-ink text-cream text-[14px] font-medium rounded-full shadow-lg">
              Coming soon
            </span>
          </div>
        </div>
      </div>
    </section>
  );
}
