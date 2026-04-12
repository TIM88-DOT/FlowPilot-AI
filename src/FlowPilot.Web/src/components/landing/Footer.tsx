const links = {
  Product: ["Features", "Pricing", "Changelog"],
  Company: ["About", "Blog", "Contact"],
  Legal: ["Privacy", "Terms", "GDPR"],
};

export default function Footer() {
  return (
    <footer id="contact" className="border-t border-[rgba(0,0,0,0.05)] py-12 px-6 md:px-8">
      <div className="max-w-[1200px] mx-auto">
        <div className="flex flex-col md:flex-row md:items-start justify-between gap-10 mb-10">
          {/* Brand */}
          <div>
            <div className="flex items-center gap-2 mb-3">
              <div className="w-5 h-5 rounded-full bg-brand" />
              <span className="text-[17px] text-[#0d0d0d] font-semibold tracking-[-0.2px]">
                Relora
              </span>
            </div>
            <p className="text-[14px] text-[#666666] leading-[1.5] max-w-[240px]">
              AI-native communication for appointment-based businesses.
            </p>
          </div>

          {/* Links */}
          <div className="flex gap-16">
            {Object.entries(links).map(([title, items]) => (
              <div key={title}>
                <p className="font-mono text-[12px] text-[#888888] tracking-[0.6px] uppercase mb-4 font-medium">
                  {title}
                </p>
                <ul className="space-y-2.5">
                  {items.map((item) => (
                    <li key={item}>
                      <a
                        href="#"
                        className="text-[14px] text-[#0d0d0d] hover:text-brand transition-colors"
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

        <div className="border-t border-[rgba(0,0,0,0.05)] pt-6 flex flex-col sm:flex-row items-center justify-between gap-3">
          <p className="text-[13px] text-[#888888]">
            &copy; {new Date().getFullYear()} Relora AI
          </p>
          <div className="flex gap-6">
            {["Twitter", "LinkedIn", "GitHub"].map((s) => (
              <a
                key={s}
                href="#"
                className="text-[13px] text-[#888888] hover:text-brand transition-colors"
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
