import { createBrowserRouter, RouterProvider } from "react-router";
import { AuthGate } from "@/core/auth";
import { Layout } from "@/core/layout";
import { Empty } from "@/core/ui";
import { modules } from "@/modules";
import { HomePage } from "@/pages/HomePage";
import { SettingsPage } from "@/pages/SettingsPage";

const router = createBrowserRouter([
  {
    element: <AuthGate><Layout /></AuthGate>,
    children: [
      { index: true, element: <HomePage /> },
      ...modules.map((m) => ({ path: m.path, children: m.routes })),
      { path: "settings", element: <SettingsPage /> },
      { path: "*", element: <Empty title="Page not found" /> },
    ],
  },
]);

export function App() {
  return <RouterProvider router={router} />;
}
