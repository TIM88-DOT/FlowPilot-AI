import { ArrowRight } from "lucide-react";
import { useFadeIn } from "../../hooks/useFadeIn";

export default function CTA() {
  const { ref, visible } = useFadeIn();

  return (
    <section
      ref={ref}
      className={`py-16 px-6 md:px-8 fade-in-section ${visible ? "is-visible" : ""}`}
    >
      <div className="max-w-[1200px] mx-auto">
        <div className="rounded-3xl border border-[rgba(0,0,0,0.05)] bg-white px-10 sm:px-16 py-14 text-center shadow-[rgba(0,0,0,0.03)_0px_2px_4px]">
          <h2
            className="text-[clamp(1.6rem,3vw,2.2rem)] font-semibold text-[#0d0d0d] leading-[1.15] mb-3"
            style={{ letterSpacing: "-0.8px" }}
          >
            Ready to put appointments on autopilot?
          </h2>
          <p className="text-[16px] text-[#666666] mb-8">
            Start your free 14-day trial. No credit card required.
          </p>
          <div className="flex items-center justify-center gap-3">
            <a
              href="/register"
              className="group inline-flex items-center gap-2 px-8 py-3 bg-[#0d0d0d] hover:opacity-90 text-white text-[15px] font-medium rounded-full shadow-[rgba(0,0,0,0.06)_0px_1px_2px] transition-opacity"
            >
              Get Started
              <ArrowRight className="w-4 h-4 opacity-70 group-hover:translate-x-0.5 transition-transform" />
            </a>
            <a
              href="#contact"
              className="inline-flex items-center px-8 py-3 text-[15px] text-[#0d0d0d] font-medium rounded-full border border-[rgba(0,0,0,0.08)] hover:opacity-90 transition-opacity"
            >
              Request Demo
            </a>
          </div>
        </div>
      </div>
    </section>
  );
}
