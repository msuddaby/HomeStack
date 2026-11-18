import { createBrowserRouter, RouterProvider } from "react-router";
import { AuthGate, JoinPage } from "@/core/auth";
import { Layout } from "@/core/layout";
import { Empty } from "@/core/ui";
import { modules } from "@/modules";
import { HomePage } from "@/pages/HomePage";
import { AdminPage } from "@/pages/AdminPage";
import { SettingsPage } from "@/pages/SettingsPage";

const router = createBrowserRouter([
  // Invite links work signed out, so they sit outside the sign-in gate.
  { path: "join/:token", element: <JoinPage /> },
  {
    element: <AuthGate><Layout /></AuthGate>,
    children: [
      { index: true, element: <HomePage /> },
      ...modules.map((m) => ({ path: m.path, children: m.routes })),
      { path: "settings", element: <SettingsPage /> },
      { path: "admin", element: <AdminPage /> },
      { path: "*", element: <Empty title="Page not found" /> },
    ],
  },
]);

export function App() {
  return <RouterProvider router={router} />;
}
