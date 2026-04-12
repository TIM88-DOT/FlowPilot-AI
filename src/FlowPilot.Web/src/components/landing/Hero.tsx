import { ArrowRight } from "lucide-react";

export default function Hero() {
  return (
    <section className="relative min-h-[92vh] flex items-center px-6 md:px-8 pt-24 pb-20 overflow-hidden">
      {/* Atmospheric green-white gradient wash */}
      <div
        className="absolute inset-0 pointer-events-none"
        style={{
          background: `
            radial-gradient(ellipse 60% 50% at 50% 0%, rgba(24,226,153,0.08) 0%, transparent 70%),
            radial-gradient(ellipse 40% 40% at 80% 20%, rgba(24,226,153,0.05) 0%, transparent 60%),
            radial-gradient(ellipse 30% 30% at 20% 60%, rgba(24,226,153,0.03) 0%, transparent 50%)
          `,
        }}
      />

      <div className="relative z-10 max-w-[1200px] mx-auto w-full grid grid-cols-1 lg:grid-cols-2 gap-12 lg:gap-20 items-center">
        {/* Left — Copy */}
        <div>
          <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-[#d4fae8] mb-8">
            <span className="w-1.5 h-1.5 rounded-full bg-brand animate-pulse" />
            <span className="font-mono text-[12px] font-medium text-[#0fa76e] tracking-[0.6px] uppercase">
              AI-native communication
            </span>
          </div>

          <h1
            className="text-[clamp(2.5rem,5.5vw,4rem)] font-semibold text-[#0d0d0d] leading-[1.15] mb-6"
            style={{ letterSpacing: "-1.28px" }}
          >
            Appointments that
            <br />
            manage{" "}
            <span className="text-brand">themselves.</span>
          </h1>

          <p className="text-[18px] text-[#333333] leading-[1.5] max-w-md mb-10">
            Smart reminders in your client's language. Instant reply
            understanding. Automatic review recovery — without lifting a finger.
          </p>

          <div className="flex items-center gap-3 mb-4">
            <a
              href="/register"
              className="group inline-flex items-center gap-2 px-6 py-2.5 bg-[#0d0d0d] hover:opacity-90 text-white text-[15px] font-medium rounded-full shadow-[rgba(0,0,0,0.06)_0px_1px_2px] transition-opacity"
            >
              Get Started
              <ArrowRight className="w-4 h-4 opacity-70 group-hover:translate-x-0.5 transition-transform" />
            </a>
            <a
              href="#contact"
              className="inline-flex items-center px-6 py-2.5 text-[15px] text-[#0d0d0d] font-medium rounded-full border border-[rgba(0,0,0,0.08)] bg-white hover:opacity-90 transition-opacity"
            >
              Request Demo
            </a>
          </div>

          <p className="text-[14px] text-[#888888]">
            Free 14-day trial &middot; No credit card
          </p>
        </div>

        {/* Right — Chat mockup */}
        <div className="relative">
          <div className="rounded-[24px] border border-[rgba(0,0,0,0.05)] bg-white p-6 shadow-[rgba(0,0,0,0.03)_0px_2px_4px]">
            {/* Mini header */}
            <div className="flex items-center gap-2 mb-5 pb-3 border-b border-[rgba(0,0,0,0.05)]">
              <div className="w-2 h-2 rounded-full bg-brand" />
              <span className="text-[13px] font-medium text-[#0d0d0d]">Relora AI</span>
              <span className="ml-auto font-mono text-[10px] font-medium text-[#0fa76e] tracking-[0.6px] uppercase">
                Live
              </span>
            </div>

            {/* Messages */}
            <div className="space-y-3">
              <div className="flex justify-end">
                <div className="bg-[#0d0d0d] text-white text-[14px] rounded-2xl rounded-br-sm px-4 py-2.5 max-w-[280px] leading-[1.5]">
                  Bonjour Sarah, votre RDV est demain à 14h. Répondez OUI pour
                  confirmer.
                </div>
              </div>
              <div className="flex justify-start">
                <div className="bg-[#fafafa] text-[#0d0d0d] text-[14px] rounded-2xl rounded-bl-sm px-4 py-2.5 border border-[rgba(0,0,0,0.05)]">
                  Oui merci!
                </div>
              </div>
              <div className="flex justify-center">
                <span className="inline-flex items-center gap-1.5 font-mono text-[12px] font-medium text-[#0fa76e] bg-[#d4fae8] px-3 py-1 rounded-full tracking-[0.6px] uppercase">
                  <span className="w-1.5 h-1.5 rounded-full bg-brand" />
                  Auto-confirmed · 94% confidence
                </span>
              </div>
              <div className="flex justify-end">
                <div className="bg-[#0d0d0d] text-white text-[14px] rounded-2xl rounded-br-sm px-4 py-2.5 max-w-[280px] leading-[1.5]">
                  Parfait, à demain Sarah!
                </div>
              </div>
            </div>

            {/* Status bar */}
            <div className="mt-5 pt-3 border-t border-[rgba(0,0,0,0.05)] flex items-center justify-between">
              <span className="font-mono text-[10px] text-[#888888] tracking-[0.6px] uppercase">
                FR · EN
              </span>
              <span className="text-[13px] text-brand font-medium">
                3 replies handled today
              </span>
            </div>
          </div>

          {/* Floating badge */}
          <div className="absolute -bottom-3 -left-3 bg-white rounded-2xl border border-[rgba(0,0,0,0.05)] px-4 py-2.5 shadow-[rgba(0,0,0,0.03)_0px_2px_4px] flex items-center gap-2.5">
            <div className="w-7 h-7 rounded-full bg-[#d4fae8] flex items-center justify-center">
              <span className="text-[11px]">⭐</span>
            </div>
            <div>
              <p className="text-[13px] font-semibold text-[#0d0d0d] leading-none">
                Review sent
              </p>
              <p className="text-[11px] text-[#888888] mt-0.5">Auto follow-up</p>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
