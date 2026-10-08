import { useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { errorText, keys, registerDevice, revokeDevice, useDevices, type DeviceKind } from "../api";
import { ErrorBox, Field, inputClass, TextInput } from "../components/form";
import { Button } from "../components/ui";

/** Kiosks and order boards sign in with a device token. It is shown once, here, right after registering. */
export function DevicesPage() {
  const queryClient = useQueryClient();
  const devices = useDevices();
  const [name, setName] = useState("");
  const [kind, setKind] = useState<DeviceKind>("Kiosk");
  const [token, setToken] = useState<{ name: string; value: string } | null>(null);
  const [copied, setCopied] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const run = async (action: () => Promise<unknown>) => {
    setError(null);
    try {
      await action();
    } catch (e) {
      setError(errorText(e));
    } finally {
      await queryClient.invalidateQueries({ queryKey: keys.devices });
    }
  };

  return (
    <div className="flex max-w-5xl flex-col gap-6 p-4">
      <ErrorBox message={error} onDismiss={() => setError(null)} />

      <form
        className="flex items-end gap-3 border-4 border-black p-4"
        onSubmit={(e) => {
          e.preventDefault();
          void run(async () => {
            const registered = await registerDevice({ name: name.trim(), kind });
            setToken({ name: registered.device.name, value: registered.token });
            setCopied(false);
            setName("");
          });
        }}
      >
        <Field label="Device name">
          <TextInput value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Kiosk 2 (by the door)" />
        </Field>
        <Field label="Kind">
          <select className={`${inputClass} w-44`} value={kind} onChange={(e) => setKind(e.target.value as DeviceKind)}>
            <option value="Kiosk">Kiosk</option>
            <option value="Board">Order board</option>
          </select>
        </Field>
        <Button type="submit" variant="solid" disabled={!name.trim()}>
          Register
        </Button>
      </form>

      {token && (
        <div className="flex flex-col gap-3 border-8 border-black p-4">
          <p className="text-xl font-black">Token for {token.name}: shown only once</p>
          <p>Enter it on the device's setup screen now. It can't be shown again; if it's lost, revoke the device and register a new one.</p>
          <code className="break-all border-4 border-black bg-black p-3 text-lg text-white" data-testid="device-token">
            {token.value}
          </code>
          <div className="flex gap-3">
            <Button
              onClick={() =>
                navigator.clipboard.writeText(token.value).then(
                  () => setCopied(true),
                  () => setError("Couldn't copy; select the token and copy it by hand."),
                )
              }
            >
              {copied ? "Copied" : "Copy"}
            </Button>
            <Button onClick={() => setToken(null)}>I've saved it</Button>
          </div>
        </div>
      )}

      <table className="w-full text-left text-lg">
        <thead>
          <tr className="border-b-4 border-black">
            <th className="p-2">Name</th>
            <th className="p-2">Kind</th>
            <th className="p-2">Registered</th>
            <th className="p-2">Last seen</th>
            <th className="p-2">Status</th>
            <th className="p-2" />
          </tr>
        </thead>
        <tbody>
          {(devices.data ?? []).map((d) => (
            <tr key={d.id} className={`border-b-2 border-black ${d.isActive ? "" : "opacity-50"}`}>
              <td className="p-2 font-bold">{d.name}</td>
              <td className="p-2">{d.kind}</td>
              <td className="p-2">{new Date(d.createdAt).toLocaleDateString()}</td>
              <td className="p-2">{d.lastSeenAt ? new Date(d.lastSeenAt).toLocaleString() : "never"}</td>
              <td className="p-2">{d.isActive ? "Active" : "Revoked"}</td>
              <td className="p-2 text-right">
                {d.isActive && (
                  <Button onClick={() => confirm(`Revoke ${d.name}? It stops working immediately.`) && run(() => revokeDevice(d.id))}>
                    Revoke
                  </Button>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {devices.isLoading && <p>Loading…</p>}
    </div>
  );
}
