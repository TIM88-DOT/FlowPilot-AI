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
      className={`fixed top-0 left-0 right-0 z-50 transition-all duration-300 ${
        scrolled
          ? "bg-white/80 backdrop-blur-xl border-b border-[rgba(0,0,0,0.05)]"
          : "bg-transparent"
      }`}
    >
      <div className="max-w-[1200px] mx-auto px-6 md:px-8 h-[60px] flex items-center justify-between">
        {/* Logo */}
        <a href="#" className="flex items-center gap-2">
          <div className="w-6 h-6 rounded-full bg-brand" />
          <span className="text-[17px] font-semibold text-[#0d0d0d] tracking-[-0.2px]">
            Relora
          </span>
        </a>

        {/* Desktop nav */}
        <div className="hidden md:flex items-center gap-8">
          {["Features", "How It Works", "Pricing"].map((label) => (
            <a
              key={label}
              href={`#${label.toLowerCase().replace(/\s+/g, "-")}`}
              className="text-[15px] font-medium text-[#0d0d0d] hover:text-brand transition-colors"
            >
              {label}
            </a>
          ))}
        </div>

        {/* CTA */}
        <div className="hidden md:flex items-center gap-4">
          <a
            href="/login"
            className="text-[15px] font-medium text-[#0d0d0d] hover:text-brand transition-colors"
          >
            Log in
          </a>
          <a
            href="/register"
            className="text-[15px] font-medium text-white bg-[#0d0d0d] hover:opacity-90 px-6 py-2 rounded-full shadow-[rgba(0,0,0,0.06)_0px_1px_2px] transition-opacity"
          >
            Get Started
          </a>
        </div>

        {/* Mobile hamburger */}
        <button
          className="md:hidden p-2 rounded-lg"
          onClick={() => setMobileOpen(!mobileOpen)}
          aria-label="Toggle menu"
        >
          <div className="w-5 flex flex-col gap-[5px]">
            <span
              className={`block h-[1.5px] bg-[#0d0d0d] transition-all duration-300 ${
                mobileOpen ? "rotate-45 translate-y-[7px]" : ""
              }`}
            />
            <span
              className={`block h-[1.5px] bg-[#0d0d0d] transition-all duration-300 ${
                mobileOpen ? "opacity-0" : ""
              }`}
            />
            <span
              className={`block h-[1.5px] bg-[#0d0d0d] transition-all duration-300 ${
                mobileOpen ? "-rotate-45 -translate-y-[7px]" : ""
              }`}
            />
          </div>
        </button>
      </div>

      {/* Mobile menu */}
      {mobileOpen && (
        <div className="md:hidden bg-white/95 backdrop-blur-xl border-t border-[rgba(0,0,0,0.05)] px-6 py-6 flex flex-col gap-4">
          {["Features", "How It Works", "Pricing"].map((label) => (
            <a
              key={label}
              href={`#${label.toLowerCase().replace(/\s+/g, "-")}`}
              className="text-[15px] font-medium text-[#0d0d0d]"
              onClick={() => setMobileOpen(false)}
            >
              {label}
            </a>
          ))}
          <div className="flex gap-4 pt-4 border-t border-[rgba(0,0,0,0.05)]">
            <a href="/login" className="text-[15px] font-medium text-[#666666]">
              Log in
            </a>
            <a
              href="/register"
              className="text-[15px] font-medium text-white bg-[#0d0d0d] px-6 py-2 rounded-full"
            >
              Get Started
            </a>
          </div>
        </div>
      )}
    </nav>
  );
}
