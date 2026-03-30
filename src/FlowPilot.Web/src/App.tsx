import { lazy, Suspense } from "react";
import { BrowserRouter, Routes, Route } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "sonner";
import { AuthProvider } from "./contexts/AuthContext";
import ErrorBoundary from "./components/app/ErrorBoundary";
import ProtectedRoute from "./components/app/ProtectedRoute";
import AppLayout from "./components/app/AppLayout";

const LandingPage = lazy(() => import("./pages/LandingPage"));
const LoginPage = lazy(() => import("./pages/LoginPage"));
const RegisterPage = lazy(() => import("./pages/RegisterPage"));
const DashboardPage = lazy(() => import("./pages/app/DashboardPage"));
const CustomersPage = lazy(() => import("./pages/app/CustomersPage"));
const AppointmentsPage = lazy(() => import("./pages/app/AppointmentsPage"));
const TemplatesPage = lazy(() => import("./pages/app/TemplatesPage"));
const SettingsPage = lazy(() => import("./pages/app/SettingsPage"));

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      refetchOnWindowFocus: false,
      staleTime: 30_000,
    },
  },
});

function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <BrowserRouter>
          <ErrorBoundary>
            <Suspense fallback={<div className="flex h-screen items-center justify-center text-muted">Loading…</div>}>
            <Routes>
              {/* Public */}
              <Route path="/" element={<LandingPage />} />
              <Route path="/login" element={<LoginPage />} />
              <Route path="/register" element={<RegisterPage />} />

              {/* Protected app */}
              <Route element={<ProtectedRoute />}>
                <Route element={<AppLayout />}>
                  <Route path="/app" element={<DashboardPage />} />
                  <Route path="/app/customers" element={<CustomersPage />} />
                  <Route path="/app/appointments" element={<AppointmentsPage />} />
                  <Route path="/app/templates" element={<TemplatesPage />} />
                  <Route path="/app/settings" element={<SettingsPage />} />
                </Route>
              </Route>
            </Routes>
            </Suspense>
          </ErrorBoundary>
        </BrowserRouter>
        <Toaster
          position="top-right"
          toastOptions={{
            className: "!bg-warm-white !border-border !text-ink !text-[13px] !rounded-xl !shadow-md",
            duration: 4000,
          }}
          richColors
        />
      </AuthProvider>
    </QueryClientProvider>
  );
}

export default App;
