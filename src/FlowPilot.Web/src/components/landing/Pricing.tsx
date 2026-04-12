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
      "Bilingual templates",
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

function PlanCard({
  name,
  price,
  period,
  description,
  features,
  cta,
  highlighted,
}: PlanProps) {
  return (
    <div
      className={`relative rounded-2xl p-7 flex flex-col transition-all duration-200 ${
        highlighted
          ? "bg-[#0d0d0d] text-white border border-[#0d0d0d]"
          : "bg-white border border-[rgba(0,0,0,0.05)] hover:border-[rgba(0,0,0,0.08)] shadow-[rgba(0,0,0,0.03)_0px_2px_4px]"
      }`}
    >
      {highlighted && (
        <span className="absolute -top-2.5 left-6 px-3 py-0.5 bg-brand text-[#0d0d0d] text-[11px] font-semibold rounded-full uppercase tracking-[0.6px]">
          Popular
        </span>
      )}

      <div className="mb-6">
        <h3
          className={`text-[15px] font-semibold mb-1 ${
            highlighted ? "text-white" : "text-[#0d0d0d]"
          }`}
        >
          {name}
        </h3>
        <p
          className={`text-[13px] mb-5 ${
            highlighted ? "text-white/50" : "text-[#666666]"
          }`}
        >
          {description}
        </p>
        <div className="flex items-baseline gap-0.5">
          <span
            className={`text-[2.5rem] font-semibold leading-none ${
              highlighted ? "text-white" : "text-[#0d0d0d]"
            }`}
            style={{ letterSpacing: "-0.8px" }}
          >
            {price}
          </span>
          {period && (
            <span
              className={`text-[14px] ${
                highlighted ? "text-white/40" : "text-[#888888]"
              }`}
            >
              {period}
            </span>
          )}
        </div>
      </div>

      <div
        className={`h-px mb-6 ${
          highlighted ? "bg-white/10" : "bg-[rgba(0,0,0,0.05)]"
        }`}
      />

      <ul className="space-y-3 mb-8 flex-1">
        {features.map((feature) => (
          <li key={feature} className="flex items-center gap-2.5">
            <Check
              className={`w-4 h-4 shrink-0 ${
                highlighted ? "text-brand" : "text-brand"
              }`}
              strokeWidth={2.5}
            />
            <span
              className={`text-[14px] ${
                highlighted ? "text-white/70" : "text-[#666666]"
              }`}
            >
              {feature}
            </span>
          </li>
        ))}
      </ul>

      <a
        href="#contact"
        className={`block text-center py-2.5 rounded-full text-[15px] font-medium transition-all ${
          highlighted
            ? "bg-white text-[#0d0d0d] hover:opacity-90"
            : "text-[#0d0d0d] border border-[rgba(0,0,0,0.08)] hover:border-[rgba(0,0,0,0.15)]"
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
      className={`py-24 px-6 md:px-8 fade-in-section ${visible ? "is-visible" : ""}`}
    >
      <div className="max-w-[960px] mx-auto">
        <div className="text-center mb-14">
          <p className="font-mono text-[12px] font-medium text-brand tracking-[0.6px] uppercase mb-4">
            Pricing
          </p>
          <h2
            className="text-[clamp(1.8rem,3.5vw,2.5rem)] font-semibold text-[#0d0d0d] leading-[1.1] mb-3"
            style={{ letterSpacing: "-0.8px" }}
          >
            Simple, transparent pricing.
          </h2>
          <p className="text-[16px] text-[#666666]">
            14-day free trial &middot; No credit card required
          </p>
        </div>

        <div className="relative">
          <div className="grid grid-cols-1 md:grid-cols-3 gap-5 items-start blur-[6px] select-none pointer-events-none">
            {plans.map((plan) => (
              <PlanCard key={plan.name} {...plan} />
            ))}
          </div>
          <div className="absolute inset-0 flex items-center justify-center">
            <span className="px-6 py-3 bg-[#0d0d0d] text-white text-[15px] font-medium rounded-full shadow-lg">
              Coming soon
            </span>
          </div>
        </div>
      </div>
    </section>
  );
}
