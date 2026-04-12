import {
  Bot,
  MessageSquareText,
  Star,
  Globe,
  BarChart3,
  ShieldCheck,
} from "lucide-react";
import type { LucideIcon } from "lucide-react";
import { useFadeIn } from "../../hooks/useFadeIn";

interface Feature {
  icon: LucideIcon;
  title: string;
  description: string;
}

const features: Feature[] = [
  {
    icon: Bot,
    title: "Smart Reminders",
    description:
      "AI crafts and schedules the perfect reminder based on client history, no-show score, and preferred language.",
  },
  {
    icon: MessageSquareText,
    title: "Instant Replies",
    description:
      "Understands \"oui\" or \"yes\" and auto-confirms — or escalates when confidence is low.",
  },
  {
    icon: Star,
    title: "Review Recovery",
    description:
      "Sends personalized review requests after every completed visit with your Google or Facebook link.",
  },
  {
    icon: Globe,
    title: "Bilingual",
    description:
      "French and English out of the box. Every message adapts to each client's preferred language.",
  },
  {
    icon: BarChart3,
    title: "Live Dashboard",
    description:
      "Delivery rates, no-show trends, token usage, and agent run logs — all at a glance.",
  },
  {
    icon: ShieldCheck,
    title: "GDPR Ready",
    description:
      "Consent tracking, one-click anonymization, and an immutable audit trail baked in.",
  },
];

export default function Features() {
  const { ref, visible } = useFadeIn();

  return (
    <section
      id="features"
      ref={ref}
      className={`py-24 px-6 md:px-8 fade-in-section ${visible ? "is-visible" : ""}`}
    >
      <div className="max-w-[1200px] mx-auto">
        {/* Section header */}
        <div className="text-center mb-16">
          <p className="font-mono text-[12px] font-medium text-brand tracking-[0.6px] uppercase mb-4">
            Features
          </p>
          <h2
            className="text-[clamp(1.8rem,3.5vw,2.5rem)] font-semibold text-[#0d0d0d] leading-[1.1]"
            style={{ letterSpacing: "-0.8px" }}
          >
            Everything on autopilot.
          </h2>
          <p className="text-[18px] text-[#666666] leading-[1.5] max-w-lg mx-auto mt-4">
            Your business rules enforced in code. AI decides <em>how</em> to
            communicate, never <em>whether</em> it should.
          </p>
        </div>

        {/* 3x2 grid */}
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
          {features.map((f) => (
            <div
              key={f.title}
              className="group rounded-2xl border border-[rgba(0,0,0,0.05)] bg-white p-6 hover:border-[rgba(0,0,0,0.08)] transition-all duration-200 shadow-[rgba(0,0,0,0.03)_0px_2px_4px]"
            >
              <div className="w-10 h-10 rounded-xl bg-[#d4fae8] flex items-center justify-center mb-4 group-hover:bg-brand/10 transition-colors">
                <f.icon className="w-[18px] h-[18px] text-[#0fa76e]" strokeWidth={1.8} />
              </div>
              <h3
                className="text-[20px] font-semibold text-[#0d0d0d] mb-2"
                style={{ letterSpacing: "-0.2px" }}
              >
                {f.title}
              </h3>
              <p className="text-[14px] text-[#666666] leading-[1.5]">
                {f.description}
              </p>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
