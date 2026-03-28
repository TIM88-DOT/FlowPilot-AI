import { ArrowRight } from "lucide-react";
import { useFadeIn } from "../../hooks/useFadeIn";

export default function CTA() {
  const { ref, visible } = useFadeIn();

  return (
    <section
      ref={ref}
      className={`py-16 px-6 fade-in-section ${visible ? "is-visible" : ""}`}
    >
      <div className="max-w-6xl mx-auto">
        <div className="rounded-2xl border border-border bg-warm-white px-8 sm:px-14 py-12 flex flex-col sm:flex-row items-center justify-between gap-6">
          <div>
            <h2 className="text-[clamp(1.4rem,3vw,1.8rem)] font-bold text-ink tracking-tight mb-2">
              Ready to put appointments on autopilot?
            </h2>
            <p className="text-[14px] text-ink-muted">
              Start your free 14-day trial. No credit card required.
            </p>
          </div>
          <a
            href="#contact"
            className="group shrink-0 inline-flex items-center gap-2 px-7 py-3 bg-teal hover:bg-teal-light text-white text-[14px] font-medium rounded-full transition-all hover:shadow-lg hover:shadow-teal/20"
          >
            Book a demo
            <ArrowRight className="w-4 h-4 opacity-70 group-hover:translate-x-0.5 transition-transform" />
          </a>
        </div>
      </div>
    </section>
  );
}
