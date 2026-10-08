export const config = {
  apiUrl: import.meta.env.VITE_API_URL ?? "http://localhost:5202",
  clerkKey: (import.meta.env.VITE_CLERK_PUBLISHABLE_KEY as string | undefined) || undefined,
  /** Dev sign-in buttons exist only in dev builds; a production build drops them even if the variable is set. */
  devStaffLogin: import.meta.env.DEV && import.meta.env.VITE_DEV_STAFF_LOGIN === "true",
};

export const APP_TITLE = "Admin";

/** Roles that can open the admin app (the API's BackOffice policy). */
export const ALLOWED_ROLES = ["assistant_manager", "manager", "admin"];

/** Edit the menu, options, devices and staff. Assistant managers get a read-only menu with availability/stock and reports. */
export const MANAGER_ROLES = ["manager", "admin"];

export const ROLE_LABELS: Record<string, string> = {
  admin: "Admin",
  manager: "Manager",
  assistant_manager: "Assistant manager",
  cashier: "Cashier",
  kitchen: "Kitchen",
};
