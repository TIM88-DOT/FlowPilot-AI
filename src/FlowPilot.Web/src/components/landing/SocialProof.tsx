import { useFadeIn } from "../../hooks/useFadeIn";

const businesses = [
  "Belleza Salon",
  "Riverside Clinic",
  "Spa Jasmin",
  "North Barber Co.",
  "Centre Dentaire",
  "Studio Beauté",
];

export default function SocialProof() {
  const { ref, visible } = useFadeIn();

  return (
    <section
      ref={ref}
      className={`py-12 bg-cream-dark/50 fade-in-section ${visible ? "is-visible" : ""}`}
    >
      <div className="max-w-5xl mx-auto px-6">
        <div className="flex flex-wrap items-center justify-center gap-x-8 gap-y-3">
          {businesses.map((name, i) => (
            <span key={name} className="flex items-center gap-3">
              <span className="text-[14px] font-medium text-ink/25 tracking-wide select-none">
                {name}
              </span>
              {i < businesses.length - 1 && (
                <span className="hidden sm:block w-1 h-1 rounded-full bg-ink/10" />
              )}
            </span>
          ))}
        </div>
      </div>
    </section>
  );
}
