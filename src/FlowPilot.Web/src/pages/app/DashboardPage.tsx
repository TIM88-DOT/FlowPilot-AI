import { useQuery } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import {
  CalendarDays,
  MessageSquareText,
  UserX,
  Star,
  AlertTriangle,
} from "lucide-react";
import api from "../../lib/api";

interface DashboardStats {
  noShowRatePercent: number;
  totalAppointmentsLast30Days: number;
  missedAppointmentsLast30Days: number;
  reviewsSentThisMonth: number;
  smsSentThisMonth: number;
  atRiskCount: number;
}

interface KPI {
  label: string;
  value: string;
  sub: string;
  icon: typeof CalendarDays;
  emphasis?: "danger";
  href?: string;
}

export default function DashboardPage() {
  const navigate = useNavigate();

  const appointments = useQuery({
    queryKey: ["appointments", "today"],
    queryFn: () =>
      api
        .get("/appointments", {
          params: {
            from: new Date().toISOString().split("T")[0],
            to: new Date().toISOString().split("T")[0],
          },
        })
        .then((r) => r.data),
  });

  const customers = useQuery({
    queryKey: ["customers", "summary"],
    queryFn: () =>
      api.get("/customers", { params: { pageSize: 1 } }).then((r) => r.data),
  });

  const stats = useQuery<DashboardStats>({
    queryKey: ["dashboard-stats"],
    queryFn: () => api.get("/stats/dashboard").then((r) => r.data),
  });

  const todayCount = appointments.data?.items?.length ?? 0;
  const totalCustomers = customers.data?.totalCount ?? 0;

  const atRiskCount = stats.data?.atRiskCount ?? 0;

  const kpis: KPI[] = [
    {
      label: "Today's appointments",
      value: String(todayCount),
      sub: "Scheduled for today",
      icon: CalendarDays,
      href: "/app/appointments",
    },
    {
      label: "At-risk",
      value: stats.data ? String(atRiskCount) : "...",
      sub: atRiskCount > 0 ? "Unconfirmed — call the customer" : "No unconfirmed appointments",
      icon: AlertTriangle,
      emphasis: atRiskCount > 0 ? "danger" : undefined,
      href: "/app/appointments?status=AtRisk",
    },
    {
      label: "No-show rate",
      value: stats.data ? `${stats.data.noShowRatePercent}%` : "...",
      sub: `Last 30 days (${stats.data?.missedAppointmentsLast30Days ?? 0}/${stats.data?.totalAppointmentsLast30Days ?? 0})`,
      icon: UserX,
      href: "/app/appointments?status=NoShow",
    },
    {
      label: "Total customers",
      value: String(totalCustomers),
      sub: "Active in your database",
      icon: MessageSquareText,
      href: "/app/customers",
    },
    {
      label: "Reviews sent",
      value: stats.data ? String(stats.data.reviewsSentThisMonth) : "...",
      sub: `This month (${stats.data?.smsSentThisMonth ?? 0} total SMS)`,
      icon: Star,
    },
  ];

  return (
    <div>
      <div className="mb-6">
        <h1 className="text-[20px] font-bold text-ink">Dashboard</h1>
        <p className="text-[13px] text-ink-muted mt-1">Overview of your business</p>
      </div>

      {/* KPI grid */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-4 mb-8">
        {kpis.map((kpi) => {
          const isDanger = kpi.emphasis === "danger";
          const clickable = Boolean(kpi.href);
          const baseCls = `rounded-2xl border p-5 transition-colors text-left w-full ${
            isDanger
              ? "border-red-300 bg-red-50/70 hover:border-red-400"
              : "border-border bg-warm-white hover:border-border-strong"
          } ${clickable ? "cursor-pointer hover:shadow-sm" : ""}`;

          const content = (
            <>
              <div className="flex items-center justify-between mb-3">
                <span
                  className={`text-[12px] font-medium ${
                    isDanger ? "text-red-700" : "text-ink-muted"
                  }`}
                >
                  {kpi.label}
                </span>
                <kpi.icon
                  className={`w-4 h-4 ${isDanger ? "text-red-500" : "text-ink-faint"}`}
                  strokeWidth={1.8}
                />
              </div>
              <p
                className={`text-[28px] font-bold leading-none mb-1 ${
                  isDanger ? "text-red-700" : "text-ink"
                }`}
              >
                {kpi.value}
              </p>
              <p className={`text-[11px] ${isDanger ? "text-red-600" : "text-ink-faint"}`}>
                {kpi.sub}
              </p>
            </>
          );

          if (kpi.href) {
            return (
              <button key={kpi.label} type="button" onClick={() => navigate(kpi.href!)} className={baseCls}>
                {content}
              </button>
            );
          }

          return (
            <div key={kpi.label} className={baseCls}>
              {content}
            </div>
          );
        })}
      </div>

      {/* Recent appointments */}
      <div className="rounded-2xl border border-border bg-warm-white p-5">
        <h2 className="text-[15px] font-semibold text-ink mb-4">Today's appointments</h2>
        {appointments.isLoading ? (
          <p className="text-[13px] text-ink-faint py-8 text-center">Loading...</p>
        ) : todayCount === 0 ? (
          <p className="text-[13px] text-ink-faint py-8 text-center">No appointments today</p>
        ) : (
          <div className="space-y-2">
            {appointments.data.items.map(
              (apt: { id: string; customerName: string; serviceName: string; startsAt: string; status: string }) => (
                <div
                  key={apt.id}
                  className="flex items-center justify-between py-2.5 px-3 rounded-xl hover:bg-cream-dark/40 transition-colors"
                >
                  <div>
                    <p className="text-[13px] font-medium text-ink">{apt.customerName}</p>
                    <p className="text-[11px] text-ink-faint">{apt.serviceName}</p>
                  </div>
                  <div className="text-right">
                    <p className="text-[12px] text-ink-muted">
                      {new Date(apt.startsAt).toLocaleTimeString([], {
                        hour: "2-digit",
                        minute: "2-digit",
                      })}
                    </p>
                    <StatusBadge status={apt.status} />
                  </div>
                </div>
              )
            )}
          </div>
        )}
      </div>
    </div>
  );
}

function StatusBadge({ status }: { status: string }) {
  const styles: Record<string, string> = {
    Scheduled: "bg-amber-wash text-amber border-amber-border",
    Confirmed: "bg-teal-wash text-teal border-teal-border",
    Completed: "bg-teal-wash text-teal border-teal-border",
    Cancelled: "bg-red-50 text-red-600 border-red-200",
    NoShow: "bg-red-50 text-red-600 border-red-200",
  };

  return (
    <span
      className={`inline-block text-[10px] font-medium px-2 py-0.5 rounded-full border ${
        styles[status] ?? "bg-cream-dark text-ink-muted border-border"
      }`}
    >
      {status}
    </span>
  );
}
