const links = {
  Product: ["Features", "Pricing", "Changelog"],
  Company: ["About", "Blog", "Contact"],
  Legal: ["Privacy", "Terms", "GDPR"],
};

export default function Footer() {
  return (
    <footer id="contact" className="border-t border-border py-10 px-6">
      <div className="max-w-6xl mx-auto">
        <div className="flex flex-col md:flex-row md:items-start justify-between gap-8 mb-8">
          {/* Brand */}
          <div>
            <div className="flex items-center gap-1.5 mb-2">
              <div className="w-5 h-5 rounded-full bg-teal" />
              <span className="text-[16px] text-ink font-bold">FlowPilot</span>
            </div>
            <p className="text-[12px] text-ink-faint leading-relaxed max-w-[220px]">
              AI-native communication for appointment-based businesses.
            </p>
          </div>

          {/* Links */}
          <div className="flex gap-16">
            {Object.entries(links).map(([title, items]) => (
              <div key={title}>
                <p className="text-[11px] text-ink-faint tracking-[0.08em] uppercase mb-3 font-medium">
                  {title}
                </p>
                <ul className="space-y-2">
                  {items.map((item) => (
                    <li key={item}>
                      <a
                        href="#"
                        className="text-[13px] text-ink-muted hover:text-ink transition-colors"
                      >
                        {item}
                      </a>
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </div>
        </div>

        <div className="border-t border-border pt-6 flex flex-col sm:flex-row items-center justify-between gap-3">
          <p className="text-[11px] text-ink-faint">
            &copy; {new Date().getFullYear()} FlowPilot AI
          </p>
          <div className="flex gap-5">
            {["Twitter", "LinkedIn", "GitHub"].map((s) => (
              <a
                key={s}
                href="#"
                className="text-[11px] text-ink-faint hover:text-ink-muted transition-colors"
              >
                {s}
              </a>
            ))}
          </div>
        </div>
      </div>
    </footer>
  );
}
