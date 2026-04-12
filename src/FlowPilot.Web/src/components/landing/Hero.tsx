import { ArrowRight } from "lucide-react";

export default function Hero() {
  return (
    <section className="relative min-h-[92vh] flex items-center px-6 pt-20 pb-16 overflow-hidden">
      {/* Subtle gradient */}
      <div
        className="absolute inset-0 pointer-events-none"
        style={{
          background: `
            radial-gradient(ellipse 50% 60% at 80% 30%, rgba(27,94,94,0.05) 0%, transparent 60%),
            radial-gradient(ellipse 40% 50% at 20% 70%, rgba(200,148,74,0.04) 0%, transparent 60%)
          `,
        }}
      />

      <div className="relative z-10 max-w-6xl mx-auto w-full grid grid-cols-1 lg:grid-cols-2 gap-12 lg:gap-16 items-center">
        {/* Left — Copy */}
        <div>
          <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-teal-wash border border-teal-border mb-8">
            <span className="w-1.5 h-1.5 rounded-full bg-teal animate-pulse" />
            <span className="text-[11px] font-medium text-teal tracking-wide uppercase">
              AI-native communication
            </span>
          </div>

          <h1 className="text-[clamp(2.4rem,5.5vw,4rem)] font-bold text-ink leading-[1.08] tracking-tight mb-5">
            Appointments that
            <br />
            manage <span className="text-teal">themselves.</span>
          </h1>

          <p className="text-[16px] text-ink-muted leading-[1.7] max-w-md mb-8">
            Smart reminders in your client's language. Instant reply understanding.
            Automatic review recovery — without lifting a finger.
          </p>

          <div className="flex items-center gap-3 mb-4">
            <a
              href="#contact"
              className="group flex items-center gap-2 px-6 py-3 bg-teal hover:bg-teal-light text-white text-[14px] font-medium rounded-full transition-all hover:shadow-lg hover:shadow-teal/20"
            >
              Book a demo
              <ArrowRight className="w-4 h-4 opacity-70 group-hover:translate-x-0.5 transition-transform" />
            </a>
            <a
              href="/login"
              className="flex items-center gap-2 px-6 py-3 text-[14px] text-ink-muted hover:text-ink font-medium rounded-full border border-border hover:border-border-strong transition-all"
            >
              Go to app
            </a>
          </div>

          <p className="text-[12px] text-ink-faint">
            Free 14-day trial &middot; No credit card
          </p>
        </div>

        {/* Right — Chat mockup */}
        <div className="relative">
          <div className="rounded-[20px] border border-border bg-warm-white p-5 shadow-sm">
            {/* Mini header */}
            <div className="flex items-center gap-2 mb-4 pb-3 border-b border-border">
              <div className="w-2 h-2 rounded-full bg-teal" />
              <span className="text-[12px] font-medium text-ink-muted">FlowPilot AI</span>
              <span className="ml-auto text-[10px] text-ink-faint">Live</span>
            </div>

            {/* Messages */}
            <div className="space-y-3">
              <div className="flex justify-end">
                <div className="bg-ink text-cream text-[13px] rounded-2xl rounded-br-sm px-4 py-2.5 max-w-[260px] leading-relaxed">
                  Bonjour Sarah, votre RDV est demain à 14h. Répondez OUI pour confirmer.
                </div>
              </div>
              <div className="flex justify-start">
                <div className="bg-white text-ink text-[13px] rounded-2xl rounded-bl-sm px-4 py-2.5 border border-border">
                  Oui merci!
                </div>
              </div>
              <div className="flex justify-center">
                <span className="inline-flex items-center gap-1.5 text-[11px] font-medium text-teal bg-teal-wash px-3 py-1 rounded-full border border-teal-border">
                  <span className="w-1.5 h-1.5 rounded-full bg-teal" />
                  Auto-confirmed · 94% confidence
                </span>
              </div>
              <div className="flex justify-end">
                <div className="bg-ink text-cream text-[13px] rounded-2xl rounded-br-sm px-4 py-2.5 max-w-[260px] leading-relaxed">
                  Parfait, à demain Sarah!
                </div>
              </div>
            </div>

            {/* Status bar */}
            <div className="mt-4 pt-3 border-t border-border flex items-center justify-between">
              <div className="flex items-center gap-4">
                <span className="text-[11px] text-ink-faint">FR · EN</span>
              </div>
              <span className="text-[11px] text-teal font-medium">3 replies handled today</span>
            </div>
          </div>

          {/* Floating badge */}
          <div className="absolute -bottom-3 -left-3 bg-white rounded-xl border border-border px-3 py-2 shadow-sm flex items-center gap-2">
            <div className="w-6 h-6 rounded-full bg-amber-wash border border-amber-border flex items-center justify-center">
              <span className="text-[10px]">⭐</span>
            </div>
            <div>
              <p className="text-[11px] font-semibold text-ink leading-none">Review sent</p>
              <p className="text-[10px] text-ink-faint">Auto follow-up</p>
            </div>
          </div>
        </div>
      </div>

      {/* Bottom border */}
      <div className="absolute bottom-0 left-0 right-0 h-px bg-gradient-to-r from-transparent via-border to-transparent" />
    </section>
  );
}
