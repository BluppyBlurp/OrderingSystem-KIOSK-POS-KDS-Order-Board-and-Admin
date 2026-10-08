export const config = {
  apiUrl: import.meta.env.VITE_API_URL ?? "http://localhost:5202",
  clerkKey: (import.meta.env.VITE_CLERK_PUBLISHABLE_KEY as string | undefined) || undefined,
  /** Dev sign-in buttons exist only in dev builds; a production build drops them even if the variable is set. */
  devStaffLogin: import.meta.env.DEV && import.meta.env.VITE_DEV_STAFF_LOGIN === "true",
};

export const APP_TITLE = "Admin";

/** Roles the API's Admin policy accepts. */
export const ALLOWED_ROLES = ["manager", "admin"];
