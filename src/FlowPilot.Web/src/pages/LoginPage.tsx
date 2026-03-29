import { useState } from "react";
import { useNavigate, Link } from "react-router-dom";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useAuth } from "../hooks/useAuth";

const loginSchema = z.object({
  email: z.string().email("Invalid email"),
  password: z.string().min(6, "Password must be at least 6 characters"),
});

type LoginForm = z.infer<typeof loginSchema>;

export default function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const [error, setError] = useState("");

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<LoginForm>({
    resolver: zodResolver(loginSchema),
  });

  const onSubmit = async (data: LoginForm) => {
    try {
      setError("");
      await login(data.email, data.password);
      navigate("/app");
    } catch {
      setError("Invalid email or password");
    }
  };

  return (
    <div className="min-h-screen flex items-center justify-center bg-cream px-6">
      <div className="w-full max-w-sm">
        <div className="text-center mb-8">
          <Link to="/" className="inline-flex items-center gap-1.5 mb-6">
            <div className="w-6 h-6 rounded-full bg-teal" />
            <span className="text-[19px] text-ink font-bold tracking-tight">FlowPilot</span>
          </Link>
          <h1 className="text-[22px] font-bold text-ink mb-1">Welcome back</h1>
          <p className="text-[14px] text-ink-muted">Sign in to your account</p>
        </div>

        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
          {error && (
            <div className="text-[13px] text-red-600 bg-red-50 border border-red-200 rounded-xl px-4 py-2.5">
              {error}
            </div>
          )}

          <div>
            <label className="block text-[13px] font-medium text-ink mb-1.5">Email</label>
            <input
              type="email"
              {...register("email")}
              className="w-full px-4 py-2.5 rounded-xl border border-border bg-warm-white text-[14px] text-ink placeholder:text-ink-faint focus:outline-none focus:border-teal transition-colors"
              placeholder="you@business.com"
            />
            {errors.email && (
              <p className="text-[12px] text-red-500 mt-1">{errors.email.message}</p>
            )}
          </div>

          <div>
            <label className="block text-[13px] font-medium text-ink mb-1.5">Password</label>
            <input
              type="password"
              {...register("password")}
              className="w-full px-4 py-2.5 rounded-xl border border-border bg-warm-white text-[14px] text-ink placeholder:text-ink-faint focus:outline-none focus:border-teal transition-colors"
              placeholder="••••••••"
            />
            {errors.password && (
              <p className="text-[12px] text-red-500 mt-1">{errors.password.message}</p>
            )}
          </div>

          <button
            type="submit"
            disabled={isSubmitting}
            className="w-full py-2.5 bg-teal hover:bg-teal-light text-white text-[14px] font-medium rounded-xl transition-colors disabled:opacity-50"
          >
            {isSubmitting ? "Signing in..." : "Sign in"}
          </button>
        </form>

        <p className="text-center text-[13px] text-ink-muted mt-6">
          Don't have an account?{" "}
          <Link to="/register" className="text-teal font-medium hover:underline">
            Sign up
          </Link>
        </p>
      </div>
    </div>
  );
}
