import { ClerkProvider, SignIn, useAuth, useUser } from "@clerk/react";
import { createContext, useContext, useMemo, useState, type ReactNode } from "react";
import { Button } from "./components/ui";
import { APP_TITLE, ALLOWED_ROLES, config } from "./config";

export interface StaffSession {
  name: string;
  /** From Clerk's public metadata (or the dev role). The API checks the role claim in the session token itself. */
  role: string | null;
  getToken: () => Promise<string | null>;
  signOut: () => void;
}

const SessionContext = createContext<StaffSession | null>(null);

export function useSession(): StaffSession {
  const session = useContext(SessionContext);
  if (!session) throw new Error("useSession must be used inside <AuthGate>");
  return session;
}

const DEV_ROLE_KEY = "admin.devRole";

function readDevRole(): string | null {
  try {
    return sessionStorage.getItem(DEV_ROLE_KEY);
  } catch {
    return null;
  }
}

/**
 * Staff sign-in. Production: Clerk. Development: also "Dev sign-in" buttons, which use the API's
 * Development-only `devstaff_<role>` tokens so the app can be tested before Clerk roles are set up.
 */
export function AuthGate({ children }: { children: ReactNode }) {
  const [devRole, setDevRole] = useState(readDevRole);

  const devSession = useMemo<StaffSession | null>(() => {
    if (!config.devStaffLogin || !devRole) return null;
    return {
      name: `Dev ${devRole}`,
      role: devRole,
      getToken: async () => `devstaff_${devRole}`,
      signOut: () => {
        try {
          sessionStorage.removeItem(DEV_ROLE_KEY);
        } catch {
          /* ignore */
        }
        setDevRole(null);
      },
    };
  }, [devRole]);

  if (devSession) return <SessionContext value={devSession}>{children}</SessionContext>;

  const devButtons = config.devStaffLogin ? (
    <DevSignIn
      onPick={(role) => {
        try {
          sessionStorage.setItem(DEV_ROLE_KEY, role);
        } catch {
          /* session-only */
        }
        setDevRole(role);
      }}
    />
  ) : null;

  if (!config.clerkKey) {
    return (
      <SignInLayout>
        {devButtons ?? <p className="text-xl">Sign-in isn't configured: set VITE_CLERK_PUBLISHABLE_KEY.</p>}
      </SignInLayout>
    );
  }

  return (
    <ClerkProvider publishableKey={config.clerkKey}>
      <ClerkGate devButtons={devButtons}>{children}</ClerkGate>
    </ClerkProvider>
  );
}

function ClerkGate({ children, devButtons }: { children: ReactNode; devButtons: ReactNode }) {
  const { isLoaded, isSignedIn, getToken, signOut } = useAuth();
  const { user } = useUser();

  const session = useMemo<StaffSession | null>(() => {
    if (!isSignedIn || !user) return null;
    const role = user.publicMetadata?.role;
    return {
      name: user.fullName || user.primaryEmailAddress?.emailAddress || "Staff",
      role: typeof role === "string" ? role : null,
      getToken: () => getToken(),
      signOut: () => void signOut(),
    };
  }, [isSignedIn, user, getToken, signOut]);

  if (!isLoaded) {
    return (
      <SignInLayout>
        <p>Loading sign-in…</p>
        {devButtons}
      </SignInLayout>
    );
  }
  if (!session) {
    return (
      <SignInLayout>
        <SignIn routing="hash" />
        {devButtons}
      </SignInLayout>
    );
  }
  return <SessionContext value={session}>{children}</SessionContext>;
}

function SignInLayout({ children }: { children: ReactNode }) {
  return (
    <div className="flex min-h-full flex-col items-center justify-center gap-8 p-8">
      <h1 className="text-5xl font-black uppercase">{APP_TITLE}</h1>
      {children}
    </div>
  );
}

function DevSignIn({ onPick }: { onPick: (role: string) => void }) {
  return (
    <div className="flex flex-col items-center gap-3 border-4 border-dashed border-black p-6">
      <p className="font-bold uppercase">Dev sign-in (development only)</p>
      <div className="flex gap-3">
        {ALLOWED_ROLES.map((role) => (
          <Button key={role} onClick={() => onPick(role)}>
            {role}
          </Button>
        ))}
      </div>
    </div>
  );
}
