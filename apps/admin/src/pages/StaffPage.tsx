import { ApiError } from "@kiosk/api-client";
import { useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import {
  errorText,
  inviteStaff,
  rejectSignup,
  removeStaffAccess,
  revokeInvitation,
  setStaffRole,
  staffKey,
  useStaff,
  type StaffMember,
} from "../api";
import { ErrorBox, Field, inputClass, TextInput } from "../components/form";
import { Button } from "../components/ui";
import { ROLE_LABELS } from "../config";

const label = (role: string | null | undefined) => (role ? (ROLE_LABELS[role] ?? role) : "No access");
const date = (iso: string | null | undefined) => (iso ? new Date(iso).toLocaleDateString() : "never");

/**
 * Two ways in, as most shops run it: invite someone by email with their role already chosen, or let them sign up
 * themselves and approve them here. Nobody gets access until a manager gives them a role.
 */
export function StaffPage() {
  const queryClient = useQueryClient();
  const staff = useStaff();
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  /** Resolves true when the action succeeded (errors are shown, not thrown). */
  const run = async (action: () => Promise<unknown>, success?: string): Promise<boolean> => {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      await action();
      if (success) setNotice(success);
      return true;
    } catch (e) {
      setError(errorText(e));
      return false;
    } finally {
      setBusy(false);
      await queryClient.invalidateQueries({ queryKey: staffKey });
    }
  };

  if (staff.error instanceof ApiError && staff.error.status === 503) {
    return (
      <div className="p-6 text-xl">
        <p className="font-bold">{staff.error.message}</p>
        <p className="mt-2">Put the Clerk secret key (sk_…) in the API's Clerk__SecretKey setting, then reload.</p>
      </div>
    );
  }

  const data = staff.data;
  const roles = data?.assignableRoles ?? [];

  return (
    <div className="flex max-w-6xl flex-col gap-8 p-4">
      <ErrorBox message={error} onDismiss={() => setError(null)} />
      {notice && <p className="border-4 border-black p-3 text-lg font-bold">{notice}</p>}
      {staff.isLoading && <p>Loading…</p>}
      {staff.isError && !(staff.error instanceof ApiError && staff.error.status === 503) && (
        <p className="font-bold">{errorText(staff.error)}</p>
      )}

      {data && (
        <>
          <section>
            <h2 className="mb-2 text-2xl font-black uppercase">Waiting for approval ({data.pendingApproval.length})</h2>
            <p className="mb-3">People who signed up themselves. Approve them with a role, or reject the sign-up (deletes it).</p>
            {data.pendingApproval.length === 0 && <p className="p-2">Nobody is waiting.</p>}
            <div className="flex flex-col gap-3">
              {data.pendingApproval.map((person) => (
                <PendingRow
                  key={person.id}
                  person={person}
                  roles={roles}
                  busy={busy}
                  onApprove={(role) => run(() => setStaffRole(person.id, role), `${person.name} can now sign in as ${label(role)}.`)}
                  onReject={() =>
                    confirm(`Reject and delete ${person.name}'s sign-up?`) && run(() => rejectSignup(person.id), `${person.name}'s sign-up was rejected.`)
                  }
                />
              ))}
            </div>
          </section>

          <InviteForm roles={roles} busy={busy} onInvite={(email, role) => run(() => inviteStaff(email, role), `Invitation sent to ${email}.`)} />

          {data.invitations.length > 0 && (
            <section>
              <h2 className="mb-2 text-2xl font-black uppercase">Invitations not accepted yet</h2>
              <table className="w-full text-left text-lg">
                <tbody>
                  {data.invitations.map((inv) => (
                    <tr key={inv.id} className="border-b-2 border-black">
                      <td className="p-2 font-bold">{inv.email}</td>
                      <td className="p-2">{label(inv.role)}</td>
                      <td className="p-2">sent {date(inv.createdAt)}</td>
                      <td className="p-2 text-right">
                        <Button disabled={busy} onClick={() => run(() => revokeInvitation(inv.id), `Invitation to ${inv.email} cancelled.`)}>
                          Cancel invitation
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </section>
          )}

          <section>
            <h2 className="mb-2 text-2xl font-black uppercase">Staff ({data.staff.length})</h2>
            <table className="w-full text-left text-lg">
              <thead>
                <tr className="border-b-4 border-black">
                  <th className="p-2">Name</th>
                  <th className="p-2">Email</th>
                  <th className="p-2">Role</th>
                  <th className="p-2">Last sign-in</th>
                  <th className="p-2" />
                </tr>
              </thead>
              <tbody>
                {data.staff.map((person) => (
                  <StaffRow
                    key={person.id}
                    person={person}
                    roles={roles}
                    busy={busy}
                    onChangeRole={(role) =>
                      confirm(`Make ${person.name} ${label(role)}? They'll be signed out if this lowers their access.`) &&
                      run(() => setStaffRole(person.id, role), `${person.name} is now ${label(role)}.`)
                    }
                    onRemove={() =>
                      confirm(`Remove ${person.name}'s access? They'll be signed out everywhere right away.`) &&
                      run(() => removeStaffAccess(person.id), `${person.name} no longer has access.`)
                    }
                  />
                ))}
              </tbody>
            </table>
          </section>
        </>
      )}
    </div>
  );
}

function RoleSelect({ value, roles, onChange, ariaLabel }: { value: string; roles: string[]; onChange: (r: string) => void; ariaLabel: string }) {
  return (
    <select className={`${inputClass} w-56`} value={value} onChange={(e) => onChange(e.target.value)} aria-label={ariaLabel}>
      {roles.map((r) => (
        <option key={r} value={r}>
          {label(r)}
        </option>
      ))}
    </select>
  );
}

function PendingRow({
  person,
  roles,
  busy,
  onApprove,
  onReject,
}: {
  person: StaffMember;
  roles: string[];
  busy: boolean;
  onApprove: (role: string) => void;
  onReject: () => void;
}) {
  const [role, setRole] = useState(roles.includes("cashier") ? "cashier" : (roles[0] ?? ""));
  return (
    <div className="flex flex-wrap items-center gap-3 border-4 border-black p-3">
      <div className="min-w-64 flex-1">
        <p className="text-xl font-bold">{person.name}</p>
        <p>
          {person.email} · signed up {date(person.createdAt)}
        </p>
      </div>
      <RoleSelect value={role} roles={roles} onChange={setRole} ariaLabel={`Role for ${person.name}`} />
      <Button variant="solid" disabled={busy || !role} onClick={() => onApprove(role)}>
        Approve
      </Button>
      <Button disabled={busy} onClick={onReject}>
        Reject
      </Button>
    </div>
  );
}

function StaffRow({
  person,
  roles,
  busy,
  onChangeRole,
  onRemove,
}: {
  person: StaffMember;
  roles: string[];
  busy: boolean;
  onChangeRole: (role: string) => void;
  onRemove: () => void;
}) {
  return (
    <tr className="border-b-2 border-black">
      <td className="p-2 font-bold">
        {person.name} {person.isYou && <span className="ml-1 border-2 border-black px-1 text-sm">you</span>}
      </td>
      <td className="p-2">{person.email}</td>
      <td className="p-2">
        {person.canManage ? (
          <RoleSelect value={person.role ?? ""} roles={roles} onChange={onChangeRole} ariaLabel={`Role for ${person.name}`} />
        ) : (
          label(person.role)
        )}
      </td>
      <td className="p-2">{date(person.lastSignInAt)}</td>
      <td className="p-2 text-right">
        {person.canManage && (
          <Button disabled={busy} onClick={onRemove}>
            Remove access
          </Button>
        )}
      </td>
    </tr>
  );
}

function InviteForm({ roles, busy, onInvite }: { roles: string[]; busy: boolean; onInvite: (email: string, role: string) => Promise<boolean> }) {
  const [email, setEmail] = useState("");
  const [role, setRole] = useState(roles.includes("cashier") ? "cashier" : (roles[0] ?? ""));

  return (
    <section>
      <h2 className="mb-2 text-2xl font-black uppercase">Invite someone</h2>
      <p className="mb-3">They get an email with a sign-up link. Their role is set the moment they create the account, so no approval step.</p>
      <form
        className="flex flex-wrap items-end gap-3 border-4 border-black p-4"
        onSubmit={(e) => {
          e.preventDefault();
          void onInvite(email.trim(), role).then((ok) => ok && setEmail(""));
        }}
      >
        <Field label="Email">
          <TextInput type="email" required value={email} onChange={(e) => setEmail(e.target.value)} placeholder="name@example.com" />
        </Field>
        <Field label="Role">
          <RoleSelect value={role} roles={roles} onChange={setRole} ariaLabel="Role for the invitation" />
        </Field>
        <Button type="submit" variant="solid" disabled={busy || !email.trim() || !role}>
          Send invitation
        </Button>
      </form>
    </section>
  );
}
