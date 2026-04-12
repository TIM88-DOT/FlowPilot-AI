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
      className={`py-16 border-y border-[rgba(0,0,0,0.05)] fade-in-section ${visible ? "is-visible" : ""}`}
    >
      <div className="max-w-[1200px] mx-auto px-6 md:px-8">
        <p className="font-mono text-[12px] font-medium text-[#888888] tracking-[0.6px] uppercase text-center mb-8">
          Loved by your favorite businesses
        </p>
        <div className="flex flex-wrap items-center justify-center gap-x-10 gap-y-4">
          {businesses.map((name, i) => (
            <span key={name} className="flex items-center gap-4">
              <span className="text-[15px] font-medium text-[#888888] select-none">
                {name}
              </span>
              {i < businesses.length - 1 && (
                <span className="hidden sm:block w-1 h-1 rounded-full bg-[rgba(0,0,0,0.1)]" />
              )}
            </span>
          ))}
        </div>
      </div>
    </section>
  );
}
