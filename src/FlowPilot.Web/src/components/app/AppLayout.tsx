import { NavLink, Outlet, useNavigate } from "react-router-dom";
import {
  LayoutDashboard,
  Users,
  CalendarDays,
  FileText,
  Settings,
  LogOut,
  Menu,
  X,
} from "lucide-react";
import { useState } from "react";
import { useAuth } from "../../contexts/AuthContext";

const navItems = [
  { to: "/app", icon: LayoutDashboard, label: "Dashboard", end: true },
  { to: "/app/customers", icon: Users, label: "Customers" },
  { to: "/app/appointments", icon: CalendarDays, label: "Appointments" },
  { to: "/app/templates", icon: FileText, label: "Templates" },
  { to: "/app/settings", icon: Settings, label: "Settings" },
];

export default function AppLayout() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const [sidebarOpen, setSidebarOpen] = useState(false);

  const handleLogout = async () => {
    await logout();
    navigate("/login");
  };

  const linkClass = ({ isActive }: { isActive: boolean }) =>
    `flex items-center gap-3 px-3 py-2 rounded-xl text-[13px] font-medium transition-colors ${
      isActive
        ? "bg-teal text-white"
        : "text-ink-muted hover:text-ink hover:bg-cream-dark/60"
    }`;

  const sidebar = (
    <div className="flex flex-col h-full">
      {/* Logo */}
      <div className="px-4 h-[60px] flex items-center gap-1.5 shrink-0">
        <div className="w-5 h-5 rounded-full bg-teal" />
        <span className="text-[16px] text-ink font-bold">FlowPilot</span>
      </div>

      {/* Nav */}
      <nav className="flex-1 px-3 py-2 space-y-1">
        {navItems.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            end={item.end}
            className={linkClass}
            onClick={() => setSidebarOpen(false)}
          >
            <item.icon className="w-[18px] h-[18px]" strokeWidth={1.8} />
            {item.label}
          </NavLink>
        ))}
      </nav>

      {/* User + logout */}
      <div className="px-3 py-4 border-t border-border">
        <div className="flex items-center gap-3 px-3 mb-3">
          <div className="w-8 h-8 rounded-full bg-teal-wash border border-teal-border flex items-center justify-center text-[12px] font-semibold text-teal">
            {user?.firstName?.charAt(0) ?? "U"}
          </div>
          <div className="min-w-0">
            <p className="text-[13px] font-medium text-ink truncate">{user?.firstName} {user?.lastName}</p>
            <p className="text-[11px] text-ink-faint truncate">{user?.email}</p>
          </div>
        </div>
        <button
          onClick={handleLogout}
          className="flex items-center gap-3 px-3 py-2 rounded-xl text-[13px] text-ink-muted hover:text-ink hover:bg-cream-dark/60 transition-colors w-full"
        >
          <LogOut className="w-[18px] h-[18px]" strokeWidth={1.8} />
          Sign out
        </button>
      </div>
    </div>
  );

  return (
    <div className="min-h-screen bg-cream flex">
      {/* Desktop sidebar */}
      <aside className="hidden lg:flex w-[240px] border-r border-border bg-warm-white flex-col shrink-0 sticky top-0 h-screen">
        {sidebar}
      </aside>

      {/* Mobile overlay */}
      {sidebarOpen && (
        <div className="fixed inset-0 z-50 lg:hidden">
          <div
            className="absolute inset-0 bg-ink/20 backdrop-blur-sm"
            onClick={() => setSidebarOpen(false)}
          />
          <aside className="absolute left-0 top-0 bottom-0 w-[260px] bg-warm-white border-r border-border">
            <button
              className="absolute top-4 right-4 p-1 text-ink-muted"
              onClick={() => setSidebarOpen(false)}
            >
              <X className="w-5 h-5" />
            </button>
            {sidebar}
          </aside>
        </div>
      )}

      {/* Main */}
      <div className="flex-1 flex flex-col min-w-0">
        {/* Topbar */}
        <header className="h-[60px] border-b border-border bg-warm-white/80 backdrop-blur-sm flex items-center px-6 shrink-0 sticky top-0 z-30">
          <button
            className="lg:hidden p-1.5 mr-3 text-ink-muted hover:text-ink"
            onClick={() => setSidebarOpen(true)}
          >
            <Menu className="w-5 h-5" />
          </button>
          <div className="flex-1" />
          <span className="text-[12px] text-ink-faint">{user?.role}</span>
        </header>

        {/* Page content */}
        <main className="flex-1 p-6">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
