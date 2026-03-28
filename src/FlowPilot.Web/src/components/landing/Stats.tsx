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
      className={`py-24 px-6 fade-in-section ${visible ? "is-visible" : ""}`}
    >
      <div className="max-w-5xl mx-auto">
        <div className="rounded-[20px] bg-ink p-10 sm:p-14">
          <div className="grid grid-cols-2 lg:grid-cols-4 gap-8 lg:gap-4">
            {stats.map((stat, i) => (
              <div
                key={stat.label}
                className={`text-center ${
                  i < stats.length - 1 ? "lg:border-r lg:border-cream/[0.08]" : ""
                }`}
              >
                <p className="font-serif text-[clamp(2rem,4vw,3rem)] font-medium text-cream leading-none mb-2">
                  {stat.value}
                </p>
                <p className="text-[13px] text-cream/60 font-medium mb-1">
                  {stat.label}
                </p>
                <p className="text-[11px] text-cream/30">
                  {stat.sub}
                </p>
              </div>
            ))}
          </div>
        </div>
      </div>
    </section>
  );
}
