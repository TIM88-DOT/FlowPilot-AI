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
      'Understands "oui", "نعم", or "yes" and auto-confirms — or escalates when confidence is low.',
  },
  {
    icon: Star,
    title: "Review Recovery",
    description:
      "Sends personalized review requests after every completed visit with your Google or Facebook link.",
  },
  {
    icon: Globe,
    title: "Multilingual",
    description:
      "French, Arabic, English out of the box. Every message adapts to each client's language.",
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
      className={`py-20 px-6 fade-in-section ${visible ? "is-visible" : ""}`}
    >
      <div className="max-w-6xl mx-auto">
        {/* Header row */}
        <div className="flex flex-col md:flex-row md:items-end md:justify-between gap-4 mb-12">
          <div>
            <p className="text-[11px] text-teal font-semibold tracking-[0.12em] uppercase mb-3">
              Features
            </p>
            <h2 className="text-[clamp(1.6rem,3.5vw,2.2rem)] font-bold text-ink tracking-tight leading-[1.15]">
              Everything on autopilot.
            </h2>
          </div>
          <p className="text-[14px] text-ink-muted max-w-sm leading-relaxed">
            Your business rules enforced in code. AI decides <em>how</em> to communicate, never <em>whether</em> it should.
          </p>
        </div>

        {/* 3x2 grid — compact cards */}
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
          {features.map((f) => (
            <div
              key={f.title}
              className="group rounded-2xl border border-border bg-warm-white p-5 hover:border-border-strong hover:bg-white transition-all duration-200"
            >
              <div className="flex items-start gap-4">
                <div className="w-9 h-9 rounded-xl bg-teal-wash border border-teal-border flex items-center justify-center shrink-0 group-hover:bg-teal/10 transition-colors">
                  <f.icon className="w-[17px] h-[17px] text-teal" strokeWidth={1.8} />
                </div>
                <div className="min-w-0">
                  <h3 className="text-[14px] font-semibold text-ink mb-1">{f.title}</h3>
                  <p className="text-[13px] text-ink-muted leading-relaxed">{f.description}</p>
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
