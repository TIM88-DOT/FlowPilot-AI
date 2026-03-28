import { useState, useEffect } from "react";

export default function Navbar() {
  const [scrolled, setScrolled] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 20);
    window.addEventListener("scroll", onScroll);
    return () => window.removeEventListener("scroll", onScroll);
  }, []);

  return (
    <nav
      className={`fixed top-0 left-0 right-0 z-50 transition-all duration-500 ${
        scrolled
          ? "bg-cream/80 backdrop-blur-xl border-b border-border"
          : "bg-transparent"
      }`}
    >
      <div className="max-w-6xl mx-auto px-6 h-[60px] flex items-center justify-between">
        {/* Logo */}
        <a href="#" className="flex items-center gap-1.5">
          <div className="w-6 h-6 rounded-full bg-teal" />
          <span className="text-[19px] text-ink font-bold tracking-tight">
            FlowPilot
          </span>
        </a>

        {/* Desktop nav */}
        <div className="hidden md:flex items-center gap-8">
          <a href="#features" className="text-[13px] text-ink-muted hover:text-ink transition-colors">
            Features
          </a>
          <a href="#how-it-works" className="text-[13px] text-ink-muted hover:text-ink transition-colors">
            How It Works
          </a>
          <a href="#pricing" className="text-[13px] text-ink-muted hover:text-ink transition-colors">
            Pricing
          </a>
        </div>

        {/* CTA */}
        <div className="hidden md:flex items-center gap-5">
          <a href="/login" className="text-[13px] text-ink-muted hover:text-ink transition-colors">
            Log in
          </a>
          <a
            href="#contact"
            className="text-[13px] text-white bg-teal hover:bg-teal-light px-5 py-2 rounded-full transition-colors"
          >
            Book a demo
          </a>
        </div>

        {/* Mobile */}
        <button
          className="md:hidden p-2 text-ink"
          onClick={() => setMobileOpen(!mobileOpen)}
          aria-label="Toggle menu"
        >
          <div className="w-5 flex flex-col gap-[5px]">
            <span className={`block h-[1.5px] bg-ink transition-all duration-300 ${mobileOpen ? "rotate-45 translate-y-[7px]" : ""}`} />
            <span className={`block h-[1.5px] bg-ink transition-all duration-300 ${mobileOpen ? "opacity-0" : ""}`} />
            <span className={`block h-[1.5px] bg-ink transition-all duration-300 ${mobileOpen ? "-rotate-45 -translate-y-[7px]" : ""}`} />
          </div>
        </button>
      </div>

      {mobileOpen && (
        <div className="md:hidden bg-cream/95 backdrop-blur-xl border-t border-border px-6 py-6 flex flex-col gap-4">
          <a href="#features" className="text-sm text-ink-muted" onClick={() => setMobileOpen(false)}>Features</a>
          <a href="#how-it-works" className="text-sm text-ink-muted" onClick={() => setMobileOpen(false)}>How It Works</a>
          <a href="#pricing" className="text-sm text-ink-muted" onClick={() => setMobileOpen(false)}>Pricing</a>
          <div className="flex gap-4 pt-3 border-t border-border">
            <a href="/login" className="text-sm text-ink-muted">Log in</a>
            <a href="#contact" className="text-sm text-white bg-teal px-5 py-2 rounded-full">Book a demo</a>
          </div>
        </div>
      )}
    </nav>
  );
}
