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
    description:
      "Import clients via CSV or connect your booking system. Zero manual entry.",
  },
  {
    number: "02",
    icon: BrainCircuit,
    title: "Automate",
    description:
      "AI sends reminders, understands replies, and handles confirmations instantly.",
  },
  {
    number: "03",
    icon: TrendingUp,
    title: "Grow",
    description:
      "Fewer no-shows, more reviews. Track everything from your dashboard.",
  },
];

export default function HowItWorks() {
  const { ref, visible } = useFadeIn();

  return (
    <section
      id="how-it-works"
      ref={ref}
      className={`py-24 px-6 md:px-8 fade-in-section ${visible ? "is-visible" : ""}`}
    >
      <div className="max-w-[1200px] mx-auto">
        <div className="rounded-3xl bg-[#0d0d0d] p-10 sm:p-16">
          <div className="text-center mb-12">
            <p className="font-mono text-[12px] font-medium text-brand tracking-[0.6px] uppercase mb-4">
              How It Works
            </p>
            <h2
              className="text-[clamp(1.8rem,3.5vw,2.5rem)] font-semibold text-[#ededed] leading-[1.1]"
              style={{ letterSpacing: "-0.8px" }}
            >
              Three steps. That's it.
            </h2>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-3 gap-8">
            {steps.map((step) => (
              <div key={step.number}>
                <div className="flex items-center gap-3 mb-5">
                  <div className="w-11 h-11 rounded-full bg-white/[0.06] border border-white/10 flex items-center justify-center">
                    <step.icon
                      className="w-[18px] h-[18px] text-brand"
                      strokeWidth={1.8}
                    />
                  </div>
                  <span className="font-mono text-[12px] font-medium text-white/30 tracking-[0.6px] uppercase">
                    {step.number}
                  </span>
                </div>

                <h3
                  className="text-[20px] font-semibold text-[#ededed] mb-2"
                  style={{ letterSpacing: "-0.2px" }}
                >
                  {step.title}
                </h3>
                <p className="text-[14px] text-white/50 leading-[1.5]">
                  {step.description}
                </p>
              </div>
            ))}
          </div>
        </div>
      </div>
    </section>
  );
}
