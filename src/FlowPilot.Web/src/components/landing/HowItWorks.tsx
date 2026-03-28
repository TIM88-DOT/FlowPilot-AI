import { Upload, BrainCircuit, TrendingUp } from "lucide-react";
import type { LucideIcon } from "lucide-react";
import { useFadeIn } from "../../hooks/useFadeIn";

interface Step {
  number: string;
  icon: LucideIcon;
  title: string;
  description: string;
}

const steps: Step[] = [
  {
    number: "01",
    icon: Upload,
    title: "Connect",
    description: "Import clients via CSV or connect your booking system. Zero manual entry.",
  },
  {
    number: "02",
    icon: BrainCircuit,
    title: "Automate",
    description: "AI sends reminders, understands replies, and handles confirmations instantly.",
  },
  {
    number: "03",
    icon: TrendingUp,
    title: "Grow",
    description: "Fewer no-shows, more reviews. Track everything from your dashboard.",
  },
];

export default function HowItWorks() {
  const { ref, visible } = useFadeIn();

  return (
    <section
      id="how-it-works"
      ref={ref}
      className={`py-20 px-6 fade-in-section ${visible ? "is-visible" : ""}`}
    >
      <div className="max-w-6xl mx-auto">
        <div className="rounded-2xl bg-ink p-8 sm:p-12">
          <div className="flex flex-col md:flex-row md:items-end md:justify-between gap-4 mb-10">
            <div>
              <p className="text-[11px] text-teal-light font-semibold tracking-[0.12em] uppercase mb-3">
                How It Works
              </p>
              <h2 className="text-[clamp(1.6rem,3.5vw,2.2rem)] font-bold text-cream tracking-tight">
                Three steps. That's it.
              </h2>
            </div>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
            {steps.map((step, i) => (
              <div key={step.number} className="relative">
                {/* Connector line */}
                {i < steps.length - 1 && (
                  <div className="hidden md:block absolute top-5 left-full w-6 h-px bg-cream/10 z-0" />
                )}

                <div className="flex items-center gap-3 mb-4">
                  <div className="w-10 h-10 rounded-full bg-cream/[0.06] border border-cream/10 flex items-center justify-center">
                    <step.icon className="w-[17px] h-[17px] text-teal-light" strokeWidth={1.8} />
                  </div>
                  <span className="text-[11px] font-medium text-cream/30 tracking-wider">{step.number}</span>
                </div>

                <h3 className="text-[16px] font-semibold text-cream mb-2">{step.title}</h3>
                <p className="text-[13px] text-cream/50 leading-relaxed">{step.description}</p>
              </div>
            ))}
          </div>
        </div>
      </div>
    </section>
  );
}
