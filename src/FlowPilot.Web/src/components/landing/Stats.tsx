import { useFadeIn } from "../../hooks/useFadeIn";

const stats = [
  { value: "98%", label: "Delivery rate", sub: "SMS delivered successfully" },
  { value: "<2s", label: "AI response", sub: "From reply to action" },
  { value: "40%", label: "Fewer no-shows", sub: "Average reduction" },
  { value: "3x", label: "More reviews", sub: "In the first 60 days" },
];

export default function Stats() {
  const { ref, visible } = useFadeIn();

  return (
    <section
      ref={ref}
      className={`py-24 px-6 md:px-8 fade-in-section ${visible ? "is-visible" : ""}`}
    >
      <div className="max-w-[1200px] mx-auto">
        <div className="rounded-3xl border border-[rgba(0,0,0,0.05)] bg-white p-10 sm:p-14 shadow-[rgba(0,0,0,0.03)_0px_2px_4px]">
          <div className="grid grid-cols-2 lg:grid-cols-4 gap-8 lg:gap-4">
            {stats.map((stat, i) => (
              <div
                key={stat.label}
                className={`text-center ${
                  i < stats.length - 1
                    ? "lg:border-r lg:border-[rgba(0,0,0,0.05)]"
                    : ""
                }`}
              >
                <p
                  className="text-[clamp(2rem,4vw,3rem)] font-semibold text-[#0d0d0d] leading-none mb-2"
                  style={{ letterSpacing: "-0.8px" }}
                >
                  {stat.value}
                </p>
                <p className="text-[14px] text-[#0d0d0d] font-medium mb-1">
                  {stat.label}
                </p>
                <p className="text-[13px] text-[#888888]">{stat.sub}</p>
              </div>
            ))}
          </div>
        </div>
      </div>
    </section>
  );
}
